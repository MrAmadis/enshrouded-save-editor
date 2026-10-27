using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData.Install;
using EnshroudedSaveEditor.GameData.Kfc;

namespace EnshroudedSaveEditor.Tests;

public sealed class KfcCacheTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "ese-cache-" + Guid.NewGuid().ToString("N"));

    public KfcCacheTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() => Directory.Delete(_tmp, true);

    string CachePath => Path.Combine(_tmp, "cache", "install.json");

    static readonly KfcSnapshot Sample = new(
        [new ItemInfo(0xf12faa48, "Copper Arrow", ItemCategory.Ammunition, Rarity.Common, 500, 12, 12, EquipSlot.None, null,
            "Ammo_T2_Arrow_Copper", "An arrow with a copper tip.", 0, false)],
        new KfcTreeSnapshot(
            [new SkillNode(1, "Spark", "", SkillNodeType.Spark, SkillClass.None, 0, 1, 1.5, 2.5, []),
             new SkillNode(2, "Mason", "10% / 20% / 30% (by level)", SkillNodeType.Passive, SkillClass.None, 1, 3, 3, 4, ["10%", "20%", "30%"])],
            [[1, 2]],
            [new ArchetypeLabel("WIZARD", SkillClass.Mage, 6643, 4709)]),
        [new SkillReward(18240839, 3, "", "")],
        50,
        [new MapLocation("Fishing Island", [0xa545279f])]);

    [Fact]
    public void A_snapshot_round_trips_under_its_key()
    {
        KfcCache.Write(CachePath, "key-1", Sample);
        var back = KfcCache.Read(CachePath, "key-1")!;
        Assert.Equal(Sample.Items, back.Items);
        Assert.Equal(Sample.Rewards, back.Rewards);
        Assert.Equal(50, back.LevelCap);
        var tree = back.Tree!.ToGraph();
        Assert.Equal(["10%", "20%", "30%"], tree.Find(2)!.RankValues);
        Assert.Equal((1u, 2u), tree.Links.Single());
        Assert.Equal(Sample.Tree!.Labels, tree.Labels);
        Assert.Equal(1.5, tree.Find(1)!.X);
        var location = Assert.Single(back.Locations!);
        Assert.Equal("Fishing Island", location.Name);
        Assert.Equal([0xa545279fu], location.Knowledge);
    }

    [Fact]
    public void Pieces_that_could_not_be_read_stay_missing()
    {
        KfcCache.Write(CachePath, "key-1", new KfcSnapshot(null, null, null, null, null));
        var back = KfcCache.Read(CachePath, "key-1")!;
        Assert.Equal((null, null, null, null, null), (back.Items, back.Tree, back.Rewards, back.LevelCap, back.Locations));
    }

    [Fact]
    public void A_stale_corrupt_or_missing_cache_is_not_used()
    {
        Assert.Null(KfcCache.Read(CachePath, "key-1"));
        KfcCache.Write(CachePath, "key-1", Sample);
        Assert.Null(KfcCache.Read(CachePath, "key-2"));
        File.WriteAllText(CachePath, "{ not json");
        Assert.Null(KfcCache.Read(CachePath, "key-1"));
        File.WriteAllText(CachePath, """{"key":"key-1","format":1,"tree":{"nodes":[],"links":[[1,2]],"labels":[]}}""");
        Assert.Null(KfcCache.Read(CachePath, "key-1"));
    }

    /// <summary>A stand-in game folder: the cache key needs only the files' sizes and times.</summary>
    GameInstall FakeInstall()
    {
        var game = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(game);
        foreach (var f in new[] { "enshrouded.exe", "enshrouded.kfc", "enshrouded.kfc_resources" })
            File.WriteAllText(Path.Combine(game, f), "x");
        return GameInstall.Find(game)!;
    }

    [Fact]
    public void A_complete_read_is_cached()
    {
        var install = FakeInstall();
        var reads = 0;
        KfcSnapshot Read(GameInstall _) { reads++; return Sample; }

        KfcGameData.Load(install, Path.Combine(_tmp, "cache"), Read);
        var again = KfcGameData.Load(install, Path.Combine(_tmp, "cache"), Read);

        Assert.Equal(1, reads);
        Assert.Equal(Sample.Items, again.Items.All);
    }

    // A piece may fail only this time (a .dat file Steam is writing on a patch day): it is read again on the next start.
    [Fact]
    public void A_read_with_a_missing_piece_is_not_cached()
    {
        var install = FakeInstall();
        var cache = Path.Combine(_tmp, "cache");
        var reads = 0;
        KfcSnapshot Read(GameInstall _) { reads++; return Sample with { Rewards = null }; }

        var first = KfcGameData.Load(install, cache, Read);
        var second = KfcGameData.Load(install, cache, Read);

        Assert.Equal(2, reads);
        Assert.False(File.Exists(CachePath));
        Assert.Throws<NotSupportedException>(() => second.Rewards);
        Assert.Equal(Sample.Items, first.Items.All);
    }

    [Fact]
    public void A_cache_with_a_missing_piece_is_read_again()
    {
        var install = FakeInstall();
        KfcCache.Write(CachePath, KfcCache.Key(install), Sample with { Tree = null });
        var reads = 0;

        var data = KfcGameData.Load(install, Path.Combine(_tmp, "cache"), _ => { reads++; return Sample; });

        Assert.Equal(1, reads);
        Assert.Equal(2, data.Tree.Nodes.Count);
        Assert.NotNull(KfcCache.Read(CachePath, KfcCache.Key(install))!.Tree);
    }

    [Fact]
    public void The_key_follows_the_size_and_time_of_the_kfc_and_the_exe()
    {
        var install = FakeInstall();
        var game = install.Dir;
        var key = KfcCache.Key(install);
        Assert.Equal(key, KfcCache.Key(install));

        File.SetLastWriteTimeUtc(Path.Combine(game, "enshrouded.kfc"), new DateTime(2026, 10, 21, 13, 32, 0, DateTimeKind.Utc));
        var afterKfc = KfcCache.Key(install);
        Assert.NotEqual(key, afterKfc);
        File.WriteAllText(Path.Combine(game, "enshrouded.exe"), "xx");
        var afterExe = KfcCache.Key(install);
        Assert.NotEqual(afterKfc, afterExe);
        File.WriteAllText(Path.Combine(game, "enshrouded.kfc_resources"), "changed");
        Assert.Equal(afterExe, KfcCache.Key(install));
    }
}
