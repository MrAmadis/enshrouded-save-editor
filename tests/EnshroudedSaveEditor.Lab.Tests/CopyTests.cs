using EnshroudedSaveEditor.Tests;
using System.Security.Cryptography;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.Lab;

namespace EnshroudedSaveEditor.Lab.Tests;

public class CopyTests : IDisposable
{
    readonly string tmp = Fixtures.CopyToTemp();
    readonly string regPath = Path.Combine(Path.GetTempPath(), "lab-reg-" + Guid.NewGuid().ToString("N"), "copies.json");
    readonly LabCopyRegistry reg;

    public CopyTests() { reg = new LabCopyRegistry(regPath); }

    public void Dispose()
    {
        try { Directory.Delete(tmp, true); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(regPath)!, true); } catch { }
    }

    Dictionary<string, string> Hashes() =>
        Directory.GetFiles(tmp).ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void Copy_creates_next_file_with_new_identity()
    {
        var before = Hashes();
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "Test Items", reg);
        Assert.Equal("character3.ksd", Path.GetFileName(path));
        var src = KsdFile.Parse(File.ReadAllBytes(Path.Combine(tmp, "character1.ksd")));
        var cp = KsdFile.Parse(File.ReadAllBytes(path));
        var cd = CharacterData.Parse(cp["character_data"].GetData());
        Assert.Equal("Test Items", cd.Name);
        Assert.Equal(3, cd.SlotIndex);
        Assert.Equal(cp.Guid, cd.Guid);
        Assert.NotEqual(src.Guid, cp.Guid);
        foreach (var n in new[] { "player_character", "player_knowledge", "fogofwar" })
            Assert.Equal(src[n].Block.ToArray(), cp[n].Block.ToArray());
        Assert.All(cp.Sections, s => Assert.True(s.ChecksumValid));
        Assert.True(reg.IsWritableCopy(path));
        foreach (var (name, hash) in before) Assert.Equal(hash, Hashes()[name]);
        Assert.Equal(before.Count + 1, Hashes().Count);
    }

    [Fact]
    public void Copy_never_overwrites_an_existing_file()
    {
        var existing = Path.Combine(tmp, "character3.ksd");
        var junk = RandomNumberGenerator.GetBytes(64);
        File.WriteAllBytes(existing, junk);
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "X", reg);
        Assert.Equal("character4.ksd", Path.GetFileName(path));
        Assert.Equal(junk, File.ReadAllBytes(existing));
    }

    [Fact]
    public void Lab_refuses_unregistered_target()
    {
        var p = Path.Combine(tmp, "character2.ksd");
        var h = Hashes();
        var ex = Assert.Throws<InvalidOperationException>(() => reg.EnsureWritable(p));
        Assert.Contains("not a Lab copy", ex.Message);
        Assert.Equal(h, Hashes());
    }

    [Fact]
    public void Lab_refuses_when_guid_no_longer_matches()
    {
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "X", reg);
        reg.EnsureWritable(path);
        File.WriteAllBytes(path, File.ReadAllBytes(Path.Combine(tmp, "character0.ksd")));
        Assert.False(reg.IsWritableCopy(path));
        Assert.Throws<InvalidOperationException>(() => reg.EnsureWritable(path));
    }

    [Fact]
    public void Registry_persists_across_instances()
    {
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "X", reg);
        Assert.True(new LabCopyRegistry(regPath).IsWritableCopy(path));
        Assert.Single(new LabCopyRegistry(regPath).List());
    }

    [Fact]
    public void Watch_copies_changed_copies_only_once_per_change()
    {
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "X", reg);
        var outDir = Path.Combine(Path.GetTempPath(), "lab-out-" + Guid.NewGuid().ToString("N"));
        var watcher = new CopyWatcher(reg, outDir);
        var t0 = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Single(watcher.Poll(t0));
        Assert.Empty(watcher.Poll(t0.AddSeconds(2)));
        var f = KsdFile.Parse(File.ReadAllBytes(path));
        f.Revision++;
        File.WriteAllBytes(path, f.ToBytes());
        var made = watcher.Poll(t0.AddSeconds(4));
        Assert.Single(made);
        Assert.Equal(Path.Combine(outDir, "character3", "20260102-030409.ksd"), made[0]);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(made[0]));
    }

    [Fact]
    public void Watch_refuses_save_like_out_folders_and_accepts_a_normal_one()
    {
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "X", reg);
        Assert.Throws<InvalidOperationException>(() => new CopyWatcher(reg, tmp));
        Assert.Throws<InvalidOperationException>(() => new CopyWatcher(reg, Path.Combine(tmp, "backup")));
        Assert.Throws<InvalidOperationException>(() => new CopyWatcher(reg, Path.Combine(tmp, "a", "b")));
        var steam = Path.Combine(Path.GetTempPath(), "x-" + Guid.NewGuid().ToString("N"), "userdata", "1", "1203620", "remote", "backup");
        Assert.Throws<InvalidOperationException>(() => new CopyWatcher(reg, steam));
        var ok = Path.Combine(Path.GetTempPath(), "lab-ok-" + Guid.NewGuid().ToString("N"));
        _ = new CopyWatcher(reg, ok);
        Assert.False(Directory.Exists(ok));
    }
}
