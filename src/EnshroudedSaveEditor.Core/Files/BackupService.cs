using System.Globalization;

namespace EnshroudedSaveEditor.Core.Files;

public sealed record BackupInfo(string Path, DateTimeOffset Created);

/// <summary>Plain-copy backups: root\fileStem\yyyyMMdd-HHmmss-fff.ksd. Never overwrites a backup.</summary>
public sealed class BackupService(string backupRoot)
{
    const string StampFormat = "yyyyMMdd-HHmmss-fff";

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnshroudedSaveEditor", "backups");

    string DirFor(string characterPath) =>
        Path.Combine(backupRoot, Path.GetFileNameWithoutExtension(characterPath));

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

    public void Restore(BackupInfo b, string characterPath)
    {
        Backup(characterPath);
        var tmp = characterPath + ".tmp";
        File.Copy(b.Path, tmp, overwrite: true);
        File.Move(tmp, characterPath, overwrite: true);
    }
}
