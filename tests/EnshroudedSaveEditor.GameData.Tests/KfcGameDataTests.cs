using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.GameData.Install;
using EnshroudedSaveEditor.GameData.Kfc;

namespace EnshroudedSaveEditor.Tests;

/// <summary>Loads the install's game data once (cold, into a temp cache) for the tests that compare it to the reference.</summary>
public sealed class InstallDataFixture : IDisposable
{
    readonly Lazy<KfcGameData> _data;

    public InstallDataFixture() =>
        _data = new Lazy<KfcGameData>(() => KfcGameData.Load(Install!, CacheDir));

    public GameInstall? Install { get; } = GameInstall.Find(null);
    public string CacheDir { get; } = Path.Combine(Path.GetTempPath(), "ese-kfc-" + Guid.NewGuid().ToString("N"));

    public KfcGameData Data
    {
        get
        {
            Assert.SkipWhen(Install is null, "Enshrouded is not installed");
            return _data.Value;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(CacheDir, true); } catch (IOException) { }
    }
}

public sealed class KfcGameDataTests(InstallDataFixture fixture) : IClassFixture<InstallDataFixture>, IDisposable
{
    readonly string _cache = Path.Combine(Path.GetTempPath(), "ese-kfc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_cache, true); } catch (IOException) { }
    }

    static readonly ReferenceGameData Reference = ReferenceGameData.Load();

    /// <summary>
    /// At least 95 % of the reference items are in the install, and at least <paramref name="share"/> of those have equal
    /// fields. The reference is an older game build (2026-10-15, the install 2026-10-20): items removed from the game since
    /// can't be present, and renamed or rebalanced ones differ (docs/research/kfc-format.md, "Match with the reference").
    /// </summary>
    static void AssertItemsMatch<T>(IItemCatalog items, double share, Func<ItemInfo, T> fields)
    {
        var mismatches = new List<string>();
        int present = 0, matched = 0;
        foreach (var want in Reference.Items.All)
        {
            if (items.Find(want.Id) is not { } got) continue;
            present++;
            if (EqualityComparer<T>.Default.Equals(fields(got), fields(want))) matched++;
            else if (mismatches.Count < 25) mismatches.Add($"{want.Id:x8} {want.InternalName}: want {fields(want)}, got {fields(got)}");
        }
        var total = Reference.Items.All.Count;
        Assert.True(present >= total * 0.95, $"Only {present} of {total} reference items are in the install.");
        Assert.True(matched >= present * share,
            $"{matched} of the {present} reference items in the install match. First mismatches:\n" + string.Join("\n", mismatches));
    }

    [Fact]
    public void Items_match_reference()
    {
        AssertItemsMatch(fixture.Data.Items, 0.95, i => (i.Name, i.MaxStack, i.Category, i.MinLevel, i.MaxLevel));
    }

    [Fact]
    public void Item_slots_weapon_types_and_rarities_match_reference()
    {
        AssertItemsMatch(fixture.Data.Items, 0.95, i => (i.Slot, i.WeaponType, i.BaseRarity));
    }

    [Fact]
    public void The_editing_rules_see_items_as_with_the_reference()
    {
        AssertItemsMatch(fixture.Data.Items, 0.99,
            i => (InventoryLayout.KindOf(i), i.FixedLevel, InventoryLayout.ExtraCapacity(i), i.MaxStack, i.Category));
        foreach (var backpack in InventoryLayout.BackpackRows.Keys)
            Assert.Contains(fixture.Data.Items.All, i => i.InternalName == backpack && InventoryLayout.ExtraCapacity(i) > 0);
    }

    [Fact]
    public void Tree_matches_reference()
    {
        var got = fixture.Data.Tree;
        var want = Reference.Tree;
        Assert.Equal(want.Nodes.Select(n => n.Id).Order(), got.Nodes.Select(n => n.Id).Order());
        Assert.All(want.Nodes, w =>
        {
            var g = got.Find(w.Id)!;
            Assert.Equal((w.Id, w.Cost, w.MaxRank, w.Type, w.Class), (g.Id, g.Cost, g.MaxRank, g.Type, g.Class));
            Assert.True(Math.Abs(g.X - w.X) <= 0.5 && Math.Abs(g.Y - w.Y) <= 0.5,
                $"node {w.Id} ({w.Name}) at ({g.X}, {g.Y}), reference ({w.X}, {w.Y})");
        });
        static IEnumerable<(uint, uint)> Undirected(SkillGraph t) =>
            t.Links.Select(l => l.A < l.B ? (l.A, l.B) : (l.B, l.A)).Order();
        Assert.Equal(Undirected(want), Undirected(got));
        // The labels' positions are the game's (they differ from the reference's by up to ~400 units); names and classes match.
        Assert.Equal(want.Labels.Select(l => (l.Name, l.Class)).Order(), got.Labels.Select(l => (l.Name, l.Class)).Order());
    }

    [Fact]
    public void Skill_text_matches_reference()
    {
        var got = fixture.Data.Tree;
        var nodes = Reference.Tree.Nodes;
        Assert.True(nodes.Count(w => got.Find(w.Id)!.Name == w.Name) >= nodes.Count * 0.95);
        // Values filled in like the reference's ("10% / 20% / 30% (by level)"); it shows only rank 1 where a text has two.
        Assert.True(nodes.Count(w => got.Find(w.Id)!.Description == w.Description) >= nodes.Count * 0.9);
        Assert.DoesNotContain(got.Nodes, n => n.Description.Contains("%k") || n.Description.Contains('?'));
        var mason = got.Find(802872125)!;
        Assert.Equal(("Mason", "Tools deal 10% / 20% / 30% (by level) increased damage against stone.\nThis includes stone terrain and gemstone veins."),
            (mason.Name, mason.Description));
        Assert.Equal(["10%", "20%", "30%"], mason.RankValues);
    }

    [Fact]
    public void Every_saved_skill_and_item_id_is_known()
    {
        var g = fixture.Data;
        var missing = new HashSet<uint>();
        foreach (var file in new[] { "character0.ksd", "character1.ksd", "character2.ksd" })
        {
            var ksd = KsdFile.Parse(Fixtures.Read(file));
            var pc = PlayerCharacter.Parse(ksd["player_character"].GetData(), id => g.Items.Find(id) is not null);
            Assert.All(pc.Skills, s => Assert.NotNull(g.Tree.Find(s.NodeId)));
            foreach (var id in pc.Containers.SelectMany(c => c.Records).Select(r => r.ItemId))
                if (g.Items.Find(id) is null) missing.Add(id);
        }
        Assert.Empty(missing.Select(id => id.ToString("x8")));
    }

    [Fact]
    public void Rewards_and_level_table_present()
    {
        var g = fixture.Data;
        Assert.All(Reference.Rewards.Values, r =>
        {
            Assert.True(g.Rewards.TryGetValue(r.Id, out var got), $"reward {r.Id} ({r.Name}) is missing");
            Assert.Equal(r.Points, got!.Points);
        });
        Assert.Equal(50, g.LevelCap);
        IReadOnlyList<uint>? xp = null;
        try { xp = g.XpPerLevel; } catch (NotSupportedException) { }
        if (xp is not null)
        {
            Assert.Equal(g.LevelCap - 1, xp.Count);
            Assert.All(xp, v => Assert.True(v > 0));
        }
    }

    // Map spec §6.2: markers whose hint or visible state is one Bool of player knowledge, less every marker with an id
    // that is also some marker's completed state.
    [Fact]
    public void Locations_are_the_markers_player_knowledge_shows()
    {
        var locations = fixture.Data.Locations;
        Assert.Contains(locations, l => l.Name == "Fishing Island" && l.Knowledge.SequenceEqual([0xa545279fu]));
        Assert.DoesNotContain(locations, l => l.Knowledge.Contains(0x667b66b6u));   // a Gem Forge's hint, a Night Temple's completed state
        Assert.DoesNotContain(locations, l => l.Knowledge.Contains(0x01165547u));   // an Elixir Well's completed state (its skill points)
        Assert.DoesNotContain(locations, l => l.Knowledge.Any(fixture.Data.Rewards.ContainsKey));
        Assert.All(locations, l => Assert.NotEmpty(l.Knowledge));
    }

    [Fact]
    public void Source_names_the_install()
    {
        Assert.Equal("Your Enshrouded install", fixture.Data.Source);
    }

    [Fact]
    public void A_cached_load_equals_the_cold_load()
    {
        var cold = fixture.Data;
        var cached = KfcGameData.Load(fixture.Install!, fixture.CacheDir);
        Assert.Equal(cold.Items.All, cached.Items.All);
        Assert.Equal(cold.Tree.Nodes.Select(Comparable), cached.Tree.Nodes.Select(Comparable));
        Assert.Equal(cold.Tree.Links, cached.Tree.Links);
        Assert.Equal(cold.Tree.Labels, cached.Tree.Labels);
        Assert.Equal(cold.Rewards.Values.OrderBy(r => r.Id), cached.Rewards.Values.OrderBy(r => r.Id));
        Assert.Equal(cold.LevelCap, cached.LevelCap);
        Assert.Equal(cold.Locations.Select(Comparable), cached.Locations.Select(Comparable));
        Assert.Throws<NotSupportedException>(() => cached.XpPerLevel);
    }

    [Fact]
    public void A_corrupt_cache_is_rebuilt()
    {
        var cold = fixture.Data;
        Directory.CreateDirectory(_cache);
        var file = Path.Combine(_cache, Path.GetFileName(Directory.GetFiles(fixture.CacheDir).Single()));
        File.WriteAllText(file, "{ not a cache");
        var rebuilt = KfcGameData.Load(fixture.Install!, _cache);
        Assert.Equal(cold.Items.All, rebuilt.Items.All);
        Assert.NotEqual("{ not a cache", File.ReadAllText(file));
    }

    static string Comparable(MapLocation l) => $"{l.Name}|{string.Join(",", l.Knowledge)}";

    static string Comparable(SkillNode n) =>
        $"{n.Id}|{n.Name}|{n.Description}|{n.Type}|{n.Class}|{n.Cost}|{n.MaxRank}|{n.X}|{n.Y}|{string.Join(",", n.RankValues)}";
}
