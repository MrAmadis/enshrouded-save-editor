using System.Security.Cryptography;
using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Lab;

/// <summary>
/// Snapshots every registered Lab copy whose bytes changed since the last poll into
/// out/stem/yyyyMMdd-HHmmss.ksd. Reads copies; writes only new files under the out folder.
/// </summary>
public sealed class CopyWatcher
{
    readonly LabCopyRegistry registry;
    readonly string outDir;

    /// <summary>
    /// Rule: outDir must not be a Steam/Saved Games save location, nor equal to or below any folder
    /// that holds a registered copy or a character*.ksd file.
    /// </summary>
    public CopyWatcher(LabCopyRegistry registry, string outDir)
    {
        this.registry = registry;
        this.outDir = outDir;
        EnsureSafeOutDir(registry, outDir);
    }

    public static void EnsureSafeOutDir(LabCopyRegistry registry, string outDir)
    {
        var full = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string Refuse() => $"Refusing --out {outDir}: it is a save folder or inside one.";
        var norm = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var sep = Path.DirectorySeparatorChar;
        if (norm.Contains($"{sep}userdata{sep}", StringComparison.OrdinalIgnoreCase)
            || norm.Contains($"{sep}1203620{sep}", StringComparison.OrdinalIgnoreCase)
            || norm.Contains($"{sep}Saved Games{sep}Enshrouded{sep}", StringComparison.OrdinalIgnoreCase)
            || norm.Contains($"{sep}keen games{sep}Enshrouded{sep}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Refuse());

        foreach (var e in registry.List())
        {
            var dir = Path.GetDirectoryName(e.Path);
            if (dir is not null && IsSameOrUnder(full, dir)) throw new InvalidOperationException(Refuse());
        }
        for (var d = full; d is not null; d = Path.GetDirectoryName(d))
        {
            try
            {
                if (Directory.Exists(d) && Directory.EnumerateFiles(d, "character*.ksd").Any())
                    throw new InvalidOperationException(Refuse());
            }
            catch (UnauthorizedAccessException) { }
        }
    }

    static bool IsSameOrUnder(string path, string dir)
    {
        dir = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(dir, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    readonly Dictionary<string, string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Poll(DateTime now)
    {
        var made = new List<string>();
        foreach (var entry in registry.List())
        {
            byte[] bytes;
            try { bytes = ReadShared(entry.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (_seen.TryGetValue(entry.Path, out var last) && last == hash) continue;
            _seen[entry.Path] = hash;

            var dir = Path.Combine(outDir, Path.GetFileNameWithoutExtension(entry.Path));
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, now.ToString("yyyyMMdd-HHmmss") + ".ksd");
            try
            {
                using var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                fs.Write(bytes);
                made.Add(target);
            }
            catch (IOException) when (File.Exists(target)) { /* same-second snapshot already exists; keep it */ }
        }
        return made;
    }

    static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var b = new byte[fs.Length];
        fs.ReadExactly(b);
        return b;
    }
}
