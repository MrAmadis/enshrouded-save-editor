using System.Security.Cryptography;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Files;

public enum SaveResult { Saved, GameRunning, ChangedOnDisk, ReadOnly, VerifyFailed }

/// <summary>The three records as bytes, plus the change count: what <see cref="SaveSession.Restore"/> puts back (undo).</summary>
public sealed record SessionSnapshot(byte[] Character, byte[] Player, byte[] Knowledge, int ChangeCount);

public sealed class SaveSession
{
    const string CharacterSection = "character_data";
    const string PlayerSection = "player_character";
    const string KnowledgeSection = "player_knowledge";

    byte[] _hash;
    byte[] _origCharacter;
    byte[] _origPlayer;
    byte[]? _origKnowledge;
    readonly Func<uint, bool> _isItem;

    SaveSession(string path, KsdFile file, byte[] hash, CharacterData character, PlayerCharacter player,
        PlayerKnowledge knowledge, byte[] origCharacter, byte[] origPlayer, byte[]? origKnowledge,
        string? readOnlyReason, Func<uint, bool> isItem)
    {
        Path = path; File = file; _hash = hash; Character = character; Player = player; Knowledge = knowledge;
        _origCharacter = origCharacter; _origPlayer = origPlayer; _origKnowledge = origKnowledge;
        ReadOnlyReason = readOnlyReason; _isItem = isItem;
    }

    public string Path { get; }
    public KsdFile File { get; }
    public CharacterData Character { get; set; }
    public PlayerCharacter Player { get; private set; }
    public PlayerKnowledge Knowledge { get; private set; }
    public bool ReadOnly => ReadOnlyReason is not null;
    public string? ReadOnlyReason { get; }
    public int ChangeCount { get; private set; }

    public void MarkChanged() => ChangeCount++;

    /// <summary>The records as they stand now, for <see cref="Restore"/>.</summary>
    public SessionSnapshot Snapshot() => new(Character.ToBytes(), Player.ToBytes(), Knowledge.ToBytes(), ChangeCount);

    /// <summary>
    /// Puts back a <see cref="Snapshot"/> of this session. The records are read back from its bytes, and nothing
    /// changes unless all three parse, <c>player_character</c> writes the same bytes again and its inventory is
    /// recognised as before; otherwise it throws <see cref="KsdFormatException"/>.
    /// </summary>
    public void Restore(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var character = CharacterData.Parse(snapshot.Character);
        var player = PlayerCharacter.Parse(snapshot.Player, _isItem);
        var knowledge = PlayerKnowledge.Parse(snapshot.Knowledge);
        if (player.InventoryStatus != Player.InventoryStatus || !player.ToBytes().AsSpan().SequenceEqual(snapshot.Player))
            throw new KsdFormatException("The snapshot doesn't read back as it was taken.");
        Character = character;
        Player = player;
        Knowledge = knowledge;
        ChangeCount = snapshot.ChangeCount;
    }

    public static SaveSession Open(string path, Func<uint, bool> isItem)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var hash = SHA256.HashData(bytes);
        var file = KsdFile.Parse(bytes);

        var problems = new List<string>();
        if (file.Version != SupportedVersions.Container)
            problems.Add($"unsupported container version {file.Version}");
        foreach (var s in file.Sections)
        {
            if (!s.ChecksumValid) problems.Add($"checksum mismatch in '{s.Name}'");
            if (SupportedVersions.Sections.TryGetValue(s.Name, out var v) && s.DataVersion != v)
                problems.Add($"unsupported data version {s.DataVersion} in '{s.Name}'");
        }

        var cd = file.Find(CharacterSection) ?? throw new KsdFormatException("The save has no character_data section.");
        var pc = file.Find(PlayerSection) ?? throw new KsdFormatException("The save has no player_character section.");
        var kn = file.Find(KnowledgeSection);

        byte[] cdBytes, pcBytes;
        byte[]? knBytes = null;
        CharacterData character;
        PlayerCharacter player;
        PlayerKnowledge knowledge;
        try
        {
            cdBytes = cd.GetData();
            pcBytes = pc.GetData();
            character = CharacterData.Parse(cdBytes);
            player = PlayerCharacter.Parse(pcBytes, isItem);
            if (kn is not null)
            {
                knBytes = kn.GetData();
                knowledge = PlayerKnowledge.Parse(knBytes);
            }
            else
            {
                knowledge = PlayerKnowledge.Parse([100, 0, 0, 0, 0, 0, 0, 0]);
            }
        }
        catch (KsdFormatException e)
        {
            throw new KsdFormatException($"Cannot read this character: {e.Message}");
        }

        var reason = problems.Count == 0 ? null : "Opened read-only: " + string.Join("; ", problems) + ".";
        return new SaveSession(path, file, hash, character, player, knowledge, cdBytes, pcBytes, knBytes, reason, isItem);
    }

    public SaveResult Save(BackupService backups, IProcessProbe probe)
    {
        if (ReadOnly) return SaveResult.ReadOnly;
        if (probe.IsGameRunning()) return SaveResult.GameRunning;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(System.IO.File.ReadAllBytes(Path)), _hash))
            return SaveResult.ChangedOnDisk;

        var backup = backups.Backup(Path);

        var newCharacter = Character.ToBytes();
        var newPlayer = Player.ToBytes();
        var newKnowledge = _origKnowledge is null ? null : Knowledge.ToBytes();
        if (!newCharacter.AsSpan().SequenceEqual(_origCharacter)) File[CharacterSection].SetData(newCharacter);
        if (!newPlayer.AsSpan().SequenceEqual(_origPlayer)) File[PlayerSection].SetData(newPlayer);
        if (newKnowledge is not null && !newKnowledge.AsSpan().SequenceEqual(_origKnowledge)) File[KnowledgeSection].SetData(newKnowledge);

        File.Revision += 1;
        File.SavedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var intended = File.Sections.Select(s => (s.Name, Data: s.GetData())).ToList();
        var output = File.ToBytes();
        var tmp = Path + ".tmp";
        try
        {
            System.IO.File.WriteAllBytes(tmp, output);
            System.IO.File.Replace(tmp, Path, null);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) try { System.IO.File.Delete(tmp); } catch (IOException) { }
        }

        if (!Verify(intended, File.Revision))
        {
            System.IO.File.Copy(backup, Path, overwrite: true);
            return SaveResult.VerifyFailed;
        }

        _hash = SHA256.HashData(output);
        _origCharacter = newCharacter;
        _origPlayer = newPlayer;
        if (newKnowledge is not null) _origKnowledge = newKnowledge;
        ChangeCount = 0;
        return SaveResult.Saved;
    }

    bool Verify(List<(string Name, byte[] Data)> intended, uint revision)
    {
        try
        {
            var back = KsdFile.Parse(System.IO.File.ReadAllBytes(Path));
            if (back.Revision != revision || back.Sections.Count != intended.Count) return false;
            foreach (var (name, data) in intended)
            {
                var s = back.Find(name);
                if (s is null || !s.ChecksumValid || !s.GetData().AsSpan().SequenceEqual(data)) return false;
            }
            return true;
        }
        catch (KsdFormatException) { return false; }
        catch (IOException) { return false; }
    }
}
