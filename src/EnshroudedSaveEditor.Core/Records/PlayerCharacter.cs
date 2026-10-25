using System.Buffers.Binary;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>Thrown when a player_character record does not match any layout we understand.</summary>
public sealed class UnsupportedLayoutException : KsdFormatException
{
    public UnsupportedLayoutException(string message) : base(message) { }
}

public readonly record struct SkillEntry(uint NodeId, byte Rank);

/// <summary>
/// The <c>player_character</c> record: opaque 58-byte prefix, level, XP, skills, then an opaque tail
/// (status list and inventory; the inventory is parsed in a later task).
/// </summary>
public sealed class PlayerCharacter
{
    public const int LevelOffset = 58;

    readonly byte[] _prefix;
    readonly byte[] _tail;

    public uint Level { get; set; }
    public uint Xp { get; set; }
    public List<SkillEntry> Skills { get; }

    PlayerCharacter(byte[] prefix, uint level, uint xp, List<SkillEntry> skills, byte[] tail)
    {
        _prefix = prefix; Level = level; Xp = xp; Skills = skills; _tail = tail;
    }

    public static PlayerCharacter Parse(byte[] data, Func<uint, bool> isItem)
    {
        ArgumentNullException.ThrowIfNull(isItem); // consumed by the inventory parser in a later task
        if (data.Length < LevelOffset + 16) throw new UnsupportedLayoutException("player_character is too short.");
        for (var o = 38; o <= 54; o += 4)
        {
            var f = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(o));
            if (!float.IsFinite(f) || f < 0f || f > 50f)
                throw new UnsupportedLayoutException($"Unexpected player_character header (float at {o}).");
        }
        var level = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LevelOffset));
        if (level < 1 || level > 200) throw new UnsupportedLayoutException("Level is out of range.");
        var xp = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LevelOffset + 4));
        var n = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LevelOffset + 8));
        if (n > 400) throw new UnsupportedLayoutException("Skill count is out of range.");
        var statusAt = LevelOffset + 12 + 5 * (int)n;
        if (statusAt + 4 > data.Length) throw new UnsupportedLayoutException("Skill list runs past the end of the data.");
        var skills = new List<SkillEntry>((int)n);
        for (var i = 0; i < n; i++)
        {
            var p = LevelOffset + 12 + 5 * i;
            var rank = data[p + 4];
            if (rank > 15) throw new UnsupportedLayoutException("Skill rank is out of range.");
            skills.Add(new SkillEntry(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)), rank));
        }
        var status = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(statusAt));
        if (status > 64) throw new UnsupportedLayoutException("Status list is out of range.");
        return new PlayerCharacter(data.AsSpan(0, LevelOffset).ToArray(), level, xp, skills,
            data.AsSpan(statusAt).ToArray());
    }

    public byte[] ToBytes()
    {
        var o = new byte[LevelOffset + 12 + 5 * Skills.Count + _tail.Length];
        _prefix.CopyTo(o, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(LevelOffset), Level);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(LevelOffset + 4), Xp);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(LevelOffset + 8), (uint)Skills.Count);
        for (var i = 0; i < Skills.Count; i++)
        {
            var p = LevelOffset + 12 + 5 * i;
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(p), Skills[i].NodeId);
            o[p + 4] = Skills[i].Rank;
        }
        _tail.CopyTo(o, LevelOffset + 12 + 5 * Skills.Count);
        return o;
    }
}
