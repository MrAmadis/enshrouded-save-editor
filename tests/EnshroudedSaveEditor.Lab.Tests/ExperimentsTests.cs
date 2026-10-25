using System.Security.Cryptography;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.Lab;
using EnshroudedSaveEditor.Tests;

namespace EnshroudedSaveEditor.Lab.Tests;

public class ExperimentsTests : IDisposable
{
    sealed class FakeProbe(bool gameRunning) : IProcessProbe
    {
        public bool IsGameRunning() => gameRunning;
        public bool IsSteamRunning() => false;
    }

    readonly string tmp = Fixtures.CopyToTemp();
    readonly string scratch = Path.Combine(Path.GetTempPath(), "lab-exp-" + Guid.NewGuid().ToString("N"));
    readonly LabCopyRegistry reg;
    readonly BackupService backups;

    public ExperimentsTests()
    {
        reg = new LabCopyRegistry(Path.Combine(scratch, "copies.json"));
        backups = new BackupService(Path.Combine(scratch, "backups"));
    }

    public void Dispose()
    {
        try { Directory.Delete(tmp, true); } catch { }
        try { Directory.Delete(scratch, true); } catch { }
    }

    Dictionary<string, string> Hashes() =>
        Directory.GetFiles(tmp).ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    (CharacterData Cd, PlayerCharacter Pc, KsdFile File) Load(string path)
    {
        var f = KsdFile.Parse(File.ReadAllBytes(path));
        Assert.All(f.Sections, s => Assert.True(s.ChecksumValid));
        var data = ReferenceGameData.Load();
        return (CharacterData.Parse(f["character_data"].GetData()),
            PlayerCharacter.Parse(f["player_character"].GetData(), id => data.Items.Find(id) is not null), f);
    }

    Dictionary<string, string> RunAll(out StringWriter output)
    {
        output = new StringWriter();
        Experiments.Run(tmp, reg, backups, new FakeProbe(false), output);
        return reg.List().ToDictionary(e => Load(e.Path).Cd.Name, e => Path.GetFileName(e.Path));
    }

    [Fact]
    public void Experiments_build_expected_copies()
    {
        var before = Hashes();
        var byName = RunAll(out var output);
        var after = Hashes();

        foreach (var (name, hash) in before) Assert.Equal(hash, after[name]);
        Assert.Equal(before.Count + 6, after.Count);
        foreach (var n in Experiments.AllNames) Assert.True(byName.ContainsKey(n), n);
        Assert.Equal(6, reg.List().Count);
        Assert.Equal(40, Experiments.OverspendName.Length);
        Assert.Contains("character3.ksd", output.ToString());

        string P(string name) => Path.Combine(tmp, byName[name]);
        var src2 = Load(Path.Combine(tmp, "character2.ksd"));

        var plain = Load(P(Experiments.PlainName));
        Assert.Equal(src2.Pc.Level, plain.Pc.Level);
        Assert.Equal(src2.Pc.Skills, plain.Pc.Skills);

        var repack = Load(P(Experiments.RepackName));
        Assert.Equal(9u, repack.Pc.Level);
        Assert.Equal(0u, repack.Pc.Xp);

        var slots = Load(P(Experiments.SlotsName));
        var inv = slots.Pc.Container(ContainerKind.Inventory)!.Records;
        var extra = slots.Pc.Container(ContainerKind.Extra)!.Records;
        Assert.Equal(40, inv.Count);
        Assert.Equal(8, extra.Count);
        Assert.All(inv, r => { Assert.Equal(0xf12faa48u, r.ItemId); Assert.Equal(r.Slot + 1, r.Count); });
        Assert.All(extra, r => { Assert.Equal(0xf12faa48u, r.ItemId); Assert.Equal(100 + r.Slot, r.Count); });
        Assert.Equal(src2.Pc.Container(ContainerKind.Equipped)!.Records.Count, slots.Pc.Container(ContainerKind.Equipped)!.Records.Count);

        var items = Load(P(Experiments.ItemsName));
        var all = items.Pc.Containers.SelectMany(c => c.Records).ToList();
        Assert.Contains(all, r => r.ItemId == 0x2b9d5b6f && r.Count == 250);
        var bow = Assert.Single(all, r => r.ItemId == 0x29433573 && r.Level == 20);
        Assert.Equal((byte)4, bow.Rarity!.Value.Rarity);
        var helmet = Assert.Single(all, r => r.ItemId == 0xff1b25e5 && r.Level == 10);
        Assert.Equal((byte)3, helmet.Rarity!.Value.Rarity);
        Assert.Contains(all, r => r.ItemId == 0xf29b7aa9);
        Assert.Contains(all, r => r.ItemId == 0xccd46f7a && r.Count == 20);
        Assert.Contains(all, r => r.ItemId == 0xe988b200 && r.Count == 600);

        var skills = Load(P(Experiments.SkillsName));
        var src1 = Load(Path.Combine(tmp, "character1.ksd"));
        var newlyLearned = skills.Pc.Skills.Where(e => e.Rank >= 1 && !src1.Pc.Skills.Any(o => o.NodeId == e.NodeId && o.Rank >= 1)).ToList();
        Assert.Equal(10, newlyLearned.Count);
        Assert.Contains(newlyLearned, e => e.Rank == 2);
        Assert.DoesNotContain("WARNING", output.ToString());

        var over = Load(P(Experiments.OverspendName));
        Assert.Equal(Experiments.OverspendName, over.Cd.Name);
        Assert.Contains(src2.Pc.Skills, e => e.Rank == 0);
        Assert.DoesNotContain(over.Pc.Skills, e => e.Rank == 0);
        Assert.Equal(src2.Pc.Skills.Count, over.Pc.Skills.Count);
    }

    [Fact]
    public void Experiments_refuse_while_the_game_runs()
    {
        var before = Hashes();
        Assert.Throws<InvalidOperationException>(() => Experiments.Run(tmp, reg, backups, new FakeProbe(true), new StringWriter()));
        Assert.Equal(before, Hashes());
        Assert.Empty(reg.List());
    }

    [Fact]
    public void Experiments_refuse_a_second_run()
    {
        Experiments.Run(tmp, reg, backups, new FakeProbe(false), new StringWriter());
        var before = Hashes();
        var ex = Assert.Throws<InvalidOperationException>(() => Experiments.Run(tmp, reg, backups, new FakeProbe(false), new StringWriter()));
        Assert.Contains("already exist", ex.Message);
        Assert.Equal(before, Hashes());
        Assert.Equal(6, reg.List().Count);
    }

    [Fact]
    public void Experiments_build_into_a_separate_target()
    {
        var local = Fixtures.CopyLocalToTemp();
        try
        {
            var srcBefore = Hashes();
            var localBefore = File.ReadAllBytes(Path.Combine(local, "character0.ksd"));
            Experiments.Run(tmp, local, reg, backups, new FakeProbe(false), new StringWriter());

            Assert.Equal(srcBefore, Hashes());
            Assert.Equal(localBefore, File.ReadAllBytes(Path.Combine(local, "character0.ksd")));
            for (var n = 1; n <= 6; n++)
            {
                var path = Path.Combine(local, $"character{n}.ksd");
                Assert.True(File.Exists(path), path);
                var cd = Load(path).Cd;
                Assert.Equal(n, cd.SlotIndex);
                Assert.Equal(new byte[] { 0xa8, 0x23, 0xb2, 0x28 }, cd.StorageTag);
            }
            Assert.False(File.Exists(Path.Combine(local, "character7.ksd")));
            Assert.Equal(6, reg.List().Count);
        }
        finally { try { Directory.Delete(local, true); } catch { } }
    }

    [Fact]
    public void Experiments_refuse_a_target_without_a_game_made_character()
    {
        var empty = Path.Combine(Path.GetTempPath(), "lab-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Experiments.Run(tmp, empty, reg, backups, new FakeProbe(false), new StringWriter()));
            Assert.Contains("Create one local character in the game first", ex.Message);
            Assert.Empty(Directory.GetFiles(empty));
        }
        finally { try { Directory.Delete(empty, true); } catch { } }
    }
}
