using System.Security.Cryptography;
using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Lab;

/// <summary>
/// Snapshots every registered Lab copy whose bytes changed since the last poll into
/// out/stem/yyyyMMdd-HHmmss.ksd. Reads copies; writes only new files under the out folder.
/// </summary>
public sealed class CopyWatcher(LabCopyRegistry registry, string outDir)
{
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
