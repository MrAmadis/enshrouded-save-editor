using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EnshroudedSaveEditor.Core.Files;

public sealed record BackupInfo(string Path, DateTimeOffset Created);

/// <summary>
/// Plain-copy backups: <c>root\&lt;fileStem&gt;-&lt;folder key&gt;\yyyyMMdd-HHmmss-fff.ksd</c>. The folder key is the first
/// 8 hex digits of the SHA-256 of the character's lower-cased full folder path, so same-named characters in different
/// save folders (Steam's <c>remote</c> and a local profile both hold <c>character0.ksd</c>) keep separate backups.
/// Never overwrites a backup.
/// </summary>
public sealed class BackupService(string backupRoot)
{
    const string StampFormat = "yyyyMMdd-HHmmss-fff";
    const int GuidOffset = 12, GuidLength = 16;
    static readonly byte[] Magic = "SV2C"u8.ToArray();

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnshroudedSaveEditor", "backups");

    string DirFor(string characterPath) =>
        Path.Combine(backupRoot, $"{Path.GetFileNameWithoutExtension(characterPath)}-{FolderKey(characterPath)}");

    static string FolderKey(string characterPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(characterPath)) ?? "";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..8];
    }

    public string Backup(string characterPath)
    {
        var dir = DirFor(characterPath);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
        for (var i = 0; ; i++)
        {
            var name = i == 0 ? $"{stamp}.ksd" : $"{stamp}-{i}.ksd";
            var target = Path.Combine(dir, name);
            try
            {
                using var src = new FileStream(characterPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                src.CopyTo(dst);
                return target;
            }
            catch (IOException) when (File.Exists(target)) { }
        }
    }

    public IReadOnlyList<BackupInfo> List(string characterPath)
    {
        var dir = DirFor(characterPath);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.ksd")
            .Select(p => new BackupInfo(p, StampOf(p) ?? new DateTimeOffset(File.GetLastWriteTimeUtc(p), TimeSpan.Zero)))
            .OrderByDescending(b => b.Created)
            .ThenByDescending(b => b.Path, StringComparer.Ordinal)
            .ToList();
    }

    static DateTimeOffset? StampOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length >= StampFormat.Length && DateTime.TryParseExact(name[..StampFormat.Length], StampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t))
            return new DateTimeOffset(t);
        return null;
    }

    /// <summary>
    /// Puts <paramref name="b"/> back over <paramref name="characterPath"/>, after a backup of the current file. Refused
    /// with <see cref="InvalidOperationException"/>, before anything is written, when <paramref name="b"/> isn't in this
    /// file's backup folder (<see cref="List"/>), or when its container GUID differs from the file's own or either can't
    /// be read.
    /// </summary>
    public void Restore(BackupInfo b, string characterPath)
    {
        ArgumentNullException.ThrowIfNull(b);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(b.Path)), Path.GetFullPath(DirFor(characterPath)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("that backup isn't one of this file's backups.");
        var backupGuid = GuidOf(b.Path) ?? throw new InvalidOperationException("the backup's save GUID can't be read.");
        var currentGuid = GuidOf(characterPath) ?? throw new InvalidOperationException("this file's save GUID can't be read.");
        if (!backupGuid.AsSpan().SequenceEqual(currentGuid))
            throw new InvalidOperationException("it belongs to a different character (the save GUIDs differ).");

        Backup(characterPath);
        var tmp = characterPath + ".tmp";
        File.Copy(b.Path, tmp, overwrite: true);
        File.Move(tmp, characterPath, overwrite: true);
    }

    /// <summary>The container GUID from the file's header, or null when it isn't an SV2C file.</summary>
    static byte[]? GuidOf(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var header = new byte[GuidOffset + GuidLength];
        if (f.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length) return null;
        return header.AsSpan(0, Magic.Length).SequenceEqual(Magic) ? header[GuidOffset..] : null;
    }
}
