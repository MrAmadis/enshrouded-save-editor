using System.Buffers.Binary;
using System.IO.Hashing;

namespace EnshroudedSaveEditor.Tests;

public static class Fixtures
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string RealRemote => Existing(Path.Combine(RepoRoot, "tests", "fixtures", "real", "remote"));

    public static string RealBackup => Existing(Path.Combine(RepoRoot, "tests", "fixtures", "real", "backup"));

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
