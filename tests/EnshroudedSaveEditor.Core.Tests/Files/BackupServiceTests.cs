using System.Security.Cryptography;
using System.Text;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Tests;

/// <summary>
/// Review fix 2: the Steam remote folder and a local keen-games folder both hold <c>character0.ksd</c>, so backups are
/// kept per source folder, and a restore refuses a backup of another character.
/// </summary>
public class BackupServiceTests : IDisposable
{
    readonly string _steam = Fixtures.CopyToTemp();
    readonly string _local = Fixtures.CopyLocalToTemp();
    readonly string _backupRoot = Path.Combine(Path.GetTempPath(), "enshrouded-backups-" + Guid.NewGuid().ToString("N"));
    BackupService Backups => new(_backupRoot);
    string SteamC0 => Path.Combine(_steam, "character0.ksd");
    string LocalC0 => Path.Combine(_local, "character0.ksd");

    public void Dispose()
    {
        foreach (var d in new[] { _steam, _local, _backupRoot })
            try { Directory.Delete(d, true); } catch { }
    }

    static byte[] GuidOf(string path) => KsdFile.Parse(File.ReadAllBytes(path)).Guid;

    static string Key(string characterPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(characterPath))!.ToLowerInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..8];
    }

    [Fact]
    public void Same_named_characters_in_other_folders_keep_their_own_backups()
    {
        Assert.NotEqual(GuidOf(SteamC0), GuidOf(LocalC0));
        var steamBackup = Backups.Backup(SteamC0);
        var localBackup = Backups.Backup(LocalC0);

        Assert.Equal(Path.Combine(_backupRoot, "character0-" + Key(SteamC0)), Path.GetDirectoryName(steamBackup));
        Assert.Equal(Path.Combine(_backupRoot, "character0-" + Key(LocalC0)), Path.GetDirectoryName(localBackup));
        Assert.Equal(steamBackup, Assert.Single(Backups.List(SteamC0)).Path);
        Assert.Equal(localBackup, Assert.Single(Backups.List(LocalC0)).Path);
        Assert.Equal(File.ReadAllBytes(SteamC0), File.ReadAllBytes(steamBackup));
        Assert.Equal(Key(SteamC0), Key(SteamC0.ToUpperInvariant()));
    }

    [Fact]
    public void Restore_refuses_a_backup_from_another_folder()
    {
        Backups.Backup(SteamC0);
        var steamBackup = Assert.Single(Backups.List(SteamC0));
        var target = File.ReadAllBytes(LocalC0);

        var e = Assert.Throws<InvalidOperationException>(() => Backups.Restore(steamBackup, LocalC0));

        Assert.Contains("isn't one of this file's backups", e.Message);
        Assert.Equal(target, File.ReadAllBytes(LocalC0));
        Assert.Empty(Backups.List(LocalC0));
    }

    [Fact]
    public void Restore_refuses_a_backup_of_another_character()
    {
        // A backup in this file's own folder, taken while the file held another character.
        var own = File.ReadAllBytes(LocalC0);
        File.Copy(SteamC0, LocalC0, overwrite: true);
        Backups.Backup(LocalC0);
        File.WriteAllBytes(LocalC0, own);
        var foreign = Assert.Single(Backups.List(LocalC0));

        var e = Assert.Throws<InvalidOperationException>(() => Backups.Restore(foreign, LocalC0));

        Assert.Contains("different character", e.Message);
        Assert.Equal(own, File.ReadAllBytes(LocalC0));
        Assert.Single(Backups.List(LocalC0));                      // refused before the safety backup
    }

    [Fact]
    public void Restore_refuses_when_a_guid_cannot_be_read()
    {
        Backups.Backup(LocalC0);
        var b = Assert.Single(Backups.List(LocalC0));
        File.WriteAllBytes(LocalC0, [1, 2, 3]);

        Assert.Contains("GUID", Assert.Throws<InvalidOperationException>(() => Backups.Restore(b, LocalC0)).Message);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(LocalC0));
    }
}
