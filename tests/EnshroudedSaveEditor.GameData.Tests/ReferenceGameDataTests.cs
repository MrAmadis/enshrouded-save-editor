using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class ReferenceGameDataTests
{
    static readonly string[] Characters = ["character0.ksd", "character1.ksd", "character2.ksd"];

    static PlayerKnowledge Knowledge(KsdFile ksd) => PlayerKnowledge.Parse(ksd["player_knowledge"].GetData());

    [Fact]
    public void Items_load_with_categories_and_limits()
    {
        var g = ReferenceGameData.Load();
        Assert.Equal(3526, g.Items.All.Count);
        var arrow = g.Items.Find(0xf12faa48)!;
        Assert.Equal("Copper Arrow", arrow.Name);
        Assert.Equal(ItemCategory.Ammunition, arrow.Category);
        Assert.Equal(500, arrow.MaxStack);
        Assert.Equal(250, g.Items.Find(0x2b9d5b6f)!.MaxStack);                                  // Copper Ore
        var bow = g.Items.Find(0x29433573)!;
        Assert.Equal((EquipSlot.Weapon, "RangedBow", 1, 50), (bow.Slot, bow.WeaponType, bow.MinLevel, bow.MaxLevel));
        Assert.Equal(EquipSlot.Ring, g.Items.Find(0xf29b7aa9)!.Slot);                           // Ring of Health
    }

    [Fact]
    public void Item_fields_are_mapped()
    {
        var g = ReferenceGameData.Load();
        var arrow = g.Items.Find(0xf12faa48)!;
        Assert.Equal(Rarity.Common, arrow.BaseRarity);
        Assert.Null(arrow.WeaponType);
        Assert.Equal("Ammo_T2_Arrow_Copper", arrow.InternalName);
        Assert.Equal("An arrow with a copper tip.", arrow.Description);
        Assert.Null(g.Items.Find(0xdeadbeef));
        Assert.Equal(50, g.LevelCap);
        Assert.Null(g.XpPerLevel);
        Assert.Equal("Reference data (Kimikozi, build of 2026-10-15)", g.Source);
        Assert.Same(g, ReferenceGameData.Load());
    }

    [Fact]
    public void Tree_loads_nodes_links_labels_sparks()
    {
        var t = ReferenceGameData.Load().Tree;
        Assert.Equal(243, t.Nodes.Count);
        Assert.Equal(312, t.Links.Count);
        Assert.Equal(12, t.Labels.Count);
        Assert.Equal(12, t.Sparks.Count());
        Assert.Equal(65, t.Nodes.Count(n => n.Type == SkillNodeType.Attribute));
        Assert.Equal(43, t.Nodes.Count(n => n.Type == SkillNodeType.Ability));
        Assert.Contains(t.Labels, l => l.Name == "WIZARD" && l.Class == SkillClass.Mage);
    }

    [Fact]
    public void Tree_lookup_neighbors_and_max_rank()
    {
        var t = ReferenceGameData.Load().Tree;
        Assert.All(t.Nodes, n => Assert.True(n.MaxRank >= 1));
        Assert.Contains(t.Nodes, n => n.MaxRank == 3);
        Assert.All(t.Links, l =>
        {
            Assert.Contains(t.Neighbors(l.A), n => n.Id == l.B);
            Assert.Contains(t.Neighbors(l.B), n => n.Id == l.A);
        });
        var spark = t.Find(2398930034)!;
        Assert.Equal((SkillNodeType.Spark, SkillClass.None, 1), (spark.Type, spark.Class, spark.MaxRank));
        Assert.Null(t.Find(1));
        Assert.Empty(t.Neighbors(1));
    }

    [Theory, InlineData("character0.ksd", 18), InlineData("character1.ksd", 35), InlineData("character2.ksd", 10)]
    public void Rewards_found_in_knowledge_sum_as_observed(string file, int points)
    {
        var knowledge = Knowledge(KsdFile.Parse(Fixtures.Read(file)));
        var sum = ReferenceGameData.Load().Rewards.Values.Where(r => knowledge.Contains(r.Id)).Sum(r => r.Points);
        Assert.Equal(points, sum);
    }

    [Fact]
    public void Every_saved_skill_and_item_id_is_known()
    {
        var g = ReferenceGameData.Load();
        var missing = new HashSet<uint>();
        foreach (var file in Characters)
        {
            var ksd = KsdFile.Parse(Fixtures.Read(file));
            var knowledge = Knowledge(ksd);
            var pc = PlayerCharacter.Parse(ksd["player_character"].GetData(), id => g.Items.Find(id) is not null);
            Assert.All(pc.Skills, s => Assert.NotNull(g.Tree.Find(s.NodeId)));
            foreach (var id in pc.Containers.SelectMany(c => c.Records).Select(r => r.ItemId))
            {
                if (g.Items.Find(id) is not null) continue;
                Assert.True(knowledge.Contains(id), $"{file}: item {id:x8} is neither in the item list nor in player_knowledge.");
                missing.Add(id);
            }
        }
        // Seven real ids are absent from the reference item list (appearance items known to the character).
        Assert.Equal(
            new uint[] { 0x01e879a5, 0x4d3ad296, 0x20a5e804, 0x07d48488, 0xda771a05, 0xf262a2a9 }.Order(),
            missing.Order());
    }
}
