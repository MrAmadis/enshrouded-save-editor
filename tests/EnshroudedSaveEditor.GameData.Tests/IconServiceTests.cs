using System.Xml.Linq;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class IconServiceTests : IDisposable
{
    const uint CopperOre = 0x2b9d5b6f;
    const uint WolfsSnarlLongbow = 0x29433573;
    const uint AdventurerHelmet = 0xff1b25e5;
    const uint SmallBackpackNew = 0xde38058e;
    const uint ImprovedHook = 0x6aa6a1d4;

    readonly string _cache = Path.Combine(Path.GetTempPath(), "enshrouded-icons-" + Guid.NewGuid().ToString("N"));
    readonly ReferenceGameData _game = ReferenceGameData.Load();

    public void Dispose()
    {
        try { Directory.Delete(_cache, true); } catch { }
    }

    ItemInfo Item(uint id) => _game.Items.Find(id)!;

    string FileFor(string url)
    {
        Assert.StartsWith("https://icons.ese/", url);
        return Path.Combine(_cache, url["https://icons.ese/".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Item_icons_are_glyphs_written_to_the_cache_on_first_use()
    {
        var icons = new IconService(_cache);
        Assert.False(Directory.Exists(_cache));

        var url = icons.ItemIcon(CopperOre, Item(CopperOre));

        Assert.Equal("https://icons.ese/glyph/materials.svg", url);
        var svg = XDocument.Load(FileFor(url)).Root!;
        Assert.Equal("{http://www.w3.org/2000/svg}svg", svg.Name.ToString());
        Assert.Equal("0 0 24 24", (string?)svg.Attribute("viewBox"));
        Assert.Equal("none", (string?)svg.Attribute("fill"));
        Assert.Equal("1.6", (string?)svg.Attribute("stroke-width"));
        Assert.Equal("round", (string?)svg.Attribute("stroke-linecap"));
    }

    [Fact]
    public void Items_get_their_slot_type_or_category_glyph()
    {
        var icons = new IconService(_cache);
        Assert.Equal("https://icons.ese/glyph/bow.svg", icons.ItemIcon(WolfsSnarlLongbow, Item(WolfsSnarlLongbow)));
        Assert.Equal("https://icons.ese/glyph/helmet.svg", icons.ItemIcon(AdventurerHelmet, Item(AdventurerHelmet)));
        Assert.Equal("https://icons.ese/glyph/backpack.svg", icons.ItemIcon(SmallBackpackNew, Item(SmallBackpackNew)));
        Assert.Equal("https://icons.ese/glyph/hook.svg", icons.ItemIcon(ImprovedHook, Item(ImprovedHook)));
        Assert.Equal("https://icons.ese/glyph/unknown.svg", icons.ItemIcon(0xdeadbeef, null));
    }

    [Fact]
    public void Every_reference_item_and_skill_has_a_well_formed_glyph()
    {
        var icons = new IconService(_cache);
        var urls = _game.Items.All.Select(i => icons.ItemIcon(i.Id, i))
            .Concat(_game.Tree.Nodes.Select(icons.SkillIcon))
            .Concat(InventoryLayout.EquipPositions.Select(icons.PositionIcon))
            .Append(icons.ItemIcon(0xdeadbeef, null))
            .Distinct().ToList();

        Assert.True(urls.Count >= Enum.GetValues<ItemCategory>().Length, $"only {urls.Count} glyphs");
        foreach (var url in urls)
        {
            var svg = XDocument.Load(FileFor(url)).Root!;
            Assert.Equal("svg", svg.Name.LocalName);
            Assert.NotEmpty(svg.Elements());
        }
    }

    [Fact]
    public void Skill_icons_follow_the_node_type()
    {
        var icons = new IconService(_cache);
        var byType = _game.Tree.Nodes.GroupBy(n => n.Type).ToDictionary(g => g.Key, g => g.Select(icons.SkillIcon).Distinct().Single());
        Assert.Equal("https://icons.ese/glyph/spark.svg", byType[SkillNodeType.Spark]);
        Assert.Equal("https://icons.ese/glyph/attribute.svg", byType[SkillNodeType.Attribute]);
        Assert.Equal("https://icons.ese/glyph/ability.svg", byType[SkillNodeType.Ability]);
        Assert.Equal("https://icons.ese/glyph/passive.svg", byType[SkillNodeType.Passive]);
    }

    [Fact]
    public void Equipped_positions_get_the_glyph_of_what_they_hold()
    {
        var icons = new IconService(_cache);
        var p = InventoryLayout.EquipPositions;
        Assert.Equal("https://icons.ese/glyph/helmet.svg", icons.PositionIcon(p[0]));
        Assert.Equal("https://icons.ese/glyph/arms.svg", icons.PositionIcon(p[7]));
        Assert.Equal("https://icons.ese/glyph/ring.svg", icons.PositionIcon(p[11]));
        Assert.Equal("https://icons.ese/glyph/glider.svg", icons.PositionIcon(p[14]));
        Assert.Equal("https://icons.ese/glyph/backpack.svg", icons.PositionIcon(p[15]));
    }

    [Fact]
    public void A_stale_cached_glyph_is_rewritten()
    {
        var url = new IconService(_cache).ItemIcon(CopperOre, Item(CopperOre));
        var file = FileFor(url);
        var fresh = File.ReadAllText(file);
        File.WriteAllText(file, "<svg/>");

        new IconService(_cache).ItemIcon(CopperOre, Item(CopperOre));

        Assert.Equal(fresh, File.ReadAllText(file));
    }
}
