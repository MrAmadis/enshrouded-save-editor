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

    // A second change within the same second finds that second's snapshot taken: it is kept, not overwritten.
    [Fact]
    public void Watch_keeps_a_same_second_snapshot()
    {
        var path = CharacterCopier.Copy(tmp, "character1.ksd", "X", reg);
        var outDir = Path.Combine(Path.GetTempPath(), "lab-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var watcher = new CopyWatcher(reg, outDir);
            var t0 = new DateTime(2026, 1, 2, 3, 4, 5);
            var first = Assert.Single(watcher.Poll(t0));
            var snapshot = File.ReadAllBytes(first);
            var f = KsdFile.Parse(File.ReadAllBytes(path));
            f.Revision++;
            File.WriteAllBytes(path, f.ToBytes());

            Assert.Empty(watcher.Poll(t0));
            Assert.Equal(snapshot, File.ReadAllBytes(first));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(first)!));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
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
        var local = Path.Combine(Path.GetTempPath(), "x-" + Guid.NewGuid().ToString("N"), "Saved Games", "keen games", "Enshrouded", "default", "backup");
        Assert.Throws<InvalidOperationException>(() => new CopyWatcher(reg, local));
        var ok = Path.Combine(Path.GetTempPath(), "lab-ok-" + Guid.NewGuid().ToString("N"));
        _ = new CopyWatcher(reg, ok);
        Assert.False(Directory.Exists(ok));
    }

    [Fact]
    public void Copy_to_target_folder_uses_next_local_number_and_tag()
    {
        var local = Fixtures.CopyLocalToTemp();
        try
        {
            var srcFile = Path.Combine(tmp, "character2.ksd");
            var localFile = Path.Combine(local, "character0.ksd");
            var srcBefore = File.ReadAllBytes(srcFile);
            var localBefore = File.ReadAllBytes(localFile);
            var tag = CharacterData.Parse(KsdFile.Parse(localBefore)["character_data"].GetData()).StorageTag;

            var path = CharacterCopier.CopyTo(srcFile, local, "Test Plain", reg, tag);

            Assert.Equal(Path.Combine(local, "character1.ksd"), path);
            var f = KsdFile.Parse(File.ReadAllBytes(path));
            var cd = CharacterData.Parse(f["character_data"].GetData());
            Assert.Equal(1, cd.SlotIndex);
            Assert.Equal("Test Plain", cd.Name);
            Assert.Equal(new byte[] { 0xa8, 0x23, 0xb2, 0x28 }, cd.StorageTag);
            Assert.Equal(f.Guid, cd.Guid);
            Assert.NotEqual(KsdFile.Parse(srcBefore).Guid, f.Guid);
            Assert.NotEqual(KsdFile.Parse(localBefore).Guid, f.Guid);
            Assert.True(reg.IsWritableCopy(path));
            Assert.Equal(srcBefore, File.ReadAllBytes(srcFile));
            Assert.Equal(localBefore, File.ReadAllBytes(localFile));
        }
        finally { try { Directory.Delete(local, true); } catch { } }
    }

    [Fact]
    public void Copy_into_a_separate_target_uses_the_local_tag()
    {
        var local = Fixtures.CopyLocalToTemp();
        try
        {
            var srcBefore = Hashes();
            var localBefore = File.ReadAllBytes(Path.Combine(local, "character0.ksd"));
            var o = new StringWriter(); var err = new StringWriter();

            var code = Cli.Run(["copy", "--source", tmp, "--target", local, "--from", "character1.ksd", "--name", "Try Me"], o, err, regPath);

            Assert.Equal(0, code);
            Assert.Equal("", err.ToString());
            var path = Path.Combine(local, "character1.ksd");
            var cd = CharacterData.Parse(KsdFile.Parse(File.ReadAllBytes(path))["character_data"].GetData());
            Assert.Equal("Try Me", cd.Name);
            Assert.Equal(1, cd.SlotIndex);
            Assert.Equal(new byte[] { 0xa8, 0x23, 0xb2, 0x28 }, cd.StorageTag);
            Assert.True(reg.IsWritableCopy(path));
            Assert.Equal(srcBefore, Hashes());
            Assert.Equal(localBefore, File.ReadAllBytes(Path.Combine(local, "character0.ksd")));
        }
        finally { try { Directory.Delete(local, true); } catch { } }
    }

    [Fact]
    public void Copy_into_a_target_without_a_game_made_character_is_refused()
    {
        var empty = Path.Combine(Path.GetTempPath(), "lab-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var o = new StringWriter(); var err = new StringWriter();
            var code = Cli.Run(["copy", "--source", tmp, "--target", empty, "--from", "character1.ksd", "--name", "X"], o, err, regPath);
            Assert.Equal(1, code);
            Assert.Contains("Create one local character in the game first", err.ToString());
            Assert.Empty(Directory.GetFiles(empty));
            Assert.Empty(reg.List());
        }
        finally { try { Directory.Delete(empty, true); } catch { } }
    }

    [Fact]
    public void Copy_with_the_old_remote_form_still_works()
    {
        var before = Hashes();
        var code = Cli.Run(["copy", "--remote", tmp, "--from", "character1.ksd", "--name", "Old Form"], new StringWriter(), new StringWriter(), regPath);
        Assert.Equal(0, code);
        Assert.Equal("Old Form", CharacterData.Parse(KsdFile.Parse(File.ReadAllBytes(Path.Combine(tmp, "character3.ksd")))["character_data"].GetData()).Name);
        foreach (var (name, hash) in before) Assert.Equal(hash, Hashes()[name]);
        Assert.Single(reg.List());
    }
}
