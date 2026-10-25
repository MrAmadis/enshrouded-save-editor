using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Hashing;
using System.Text.Json;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Tests;

public static class Fixtures
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string RealRemote => Existing(Path.Combine(RepoRoot, "tests", "fixtures", "real", "remote"));

    public static string RealBackup => Existing(Path.Combine(RepoRoot, "tests", "fixtures", "real", "backup"));

    /// <summary>The local (non-Steam) fixture folder: one game-made character0.ksd ("Test").</summary>
    public static string RealLocal => Existing(Path.Combine(RepoRoot, "tests", "fixtures", "real", "local"));

    /// <summary>Copies the local fixture to a fresh temp folder.</summary>
    public static string CopyLocalToTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "enshrouded-tests-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(RealLocal))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        return dir;
    }

    /// <summary>Copies the real remote fixtures to a fresh temp folder so tests can write freely.</summary>
    public static string CopyToTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "enshrouded-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(RealRemote))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        return dir;
    }

    public static byte[] Read(string fileName) => File.ReadAllBytes(Path.Combine(RealRemote, fileName));

    /// <summary>
    /// The whole character2.ksd file with its player_character end anchor broken (<c>02 00 03 00 …</c> → <c>09 …</c>),
    /// as InventoryTests.Unrecognised_tail_falls_back_to_in_place_edits does, so its inventory opens
    /// <c>InPlaceOnly</c>. Write it to a temp folder to open it as a session.
    /// </summary>
    public static byte[] InPlaceOnlyCharacter2()
    {
        var file = KsdFile.Parse(Read("character2.ksd"));
        var section = file["player_character"];
        var data = section.GetData();
        data[^22] = 0x09;
        section.SetData(data);
        return file.ToBytes();
    }

    static readonly Lazy<IReadOnlySet<uint>> ItemIdSet = new(() => LoadItemIds(ItemDbPath));

    static string ItemDbPath => Path.Combine(RepoRoot, "tests", "fixtures", "real", "itemdb2.json");

    /// <summary>The item ids (the hex keys of the <c>items</c> object) in the reference itemdb2.json.</summary>
    public static IReadOnlySet<uint> ItemIds()
    {
        if (!File.Exists(ItemDbPath))
            Assert.Skip($"Reference data missing: {ItemDbPath}");
        return ItemIdSet.Value;
    }

    private static IReadOnlySet<uint> LoadItemIds(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var ids = new HashSet<uint>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateObject())
            ids.Add(uint.Parse(item.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return ids.ToFrozenSet();
    }

    /// <summary>
    /// Patches the raw block bytes (header included) of one section, then rewrites that
    /// section's checksum in the table. Returns a modified copy.
    /// </summary>
    public static byte[] PatchBlock(byte[] file, string section, Action<byte[]> patch)
    {
        var copy = (byte[])file.Clone();
        var tableAt = 0x28 + (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(0x28));
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(0x2C));
        for (var i = 0; i < count; i++)
        {
            var at = tableAt + i * 20;
            var nameOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(at));
            var nameLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(at + 4));
            if (System.Text.Encoding.UTF8.GetString(copy, at + nameOff, nameLen) != section) continue;

            var dataOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(at + 8));
            var dataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(at + 12));
            var block = copy.AsSpan(dataOff, dataSize).ToArray();
            patch(block);
            block.CopyTo(copy, dataOff);
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(at + 16), (uint)XxHash3.HashToUInt64(block));
            return copy;
        }
        throw new ArgumentException($"No section '{section}'.", nameof(section));
    }

    private static string Existing(string path)
    {
        if (!Directory.Exists(path))
            Assert.Skip($"Fixture folder missing: {path}");
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EnshroudedSaveEditor.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
