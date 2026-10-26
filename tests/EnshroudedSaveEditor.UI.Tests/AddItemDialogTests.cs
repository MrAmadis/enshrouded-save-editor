using System.Globalization;
using Bunit;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.UI.Services;
using EnshroudedSaveEditor.UI.Tabs;
using Microsoft.Extensions.DependencyInjection;

namespace EnshroudedSaveEditor.Tests;

/// <summary>Owner's rule: temp copies of the fixtures, a temp app data root and a fake process probe only.</summary>
public sealed class AddItemDialogTests : IDisposable
{
    const uint CopperOre = 0x2b9d5b6f;            // Materials, max 250
    const uint WolfsSnarlLongbow = 0x29433573;    // Weapons/RangedBow, levels 1–50
    const uint RingOfHealth = 0xf29b7aa9;         // Equipment/Ring, levels 10–10
    const uint Torch = 0xcf128197;                // Weapons; character2's own torch has a level and no rarity
    const uint WaterPouch = 0xe4831ad8;           // levels 0–0; character1's has a level field (flags 05)

    readonly string _root = Fixtures.CopyToTemp();
    readonly string _appData = Path.Combine(Path.GetTempPath(), "enshrouded-appdata-" + Guid.NewGuid().ToString("N"));
    readonly BunitContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
        foreach (var d in new[] { _root, _appData })
            try { Directory.Delete(d, true); } catch { }
    }

    // character2: Amadis level 7, an empty backpack of 40 slots (Inventory 0–15, 32–39, Extra 0–15).
    EditorService Start(int character = 2)
    {
        var options = new EditorOptions([_root], _appData, new EditorServiceTests.FakeProbe(), null);
        var editor = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        _ctx.Services.AddSingleton(editor);
        _ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        editor.Open(Path.Combine(_root, $"character{character}.ksd"));
        return editor;
    }

    IRenderedComponent<InventoryTab> OpenDialog()
    {
        var cut = _ctx.Render<InventoryTab>();
        cut.Find("button.add-item").Click();
        Assert.NotNull(cut.Find(".add-dialog"));
        return cut;
    }

    static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
    static string Id(uint id) => id.ToString("x8", CultureInfo.InvariantCulture);
    static List<string> TileNames(IRenderedComponent<InventoryTab> cut) =>
        cut.FindAll(".add-dialog .tile .t b").Select(t => t.TextContent.Trim()).ToList();

    static void Search(IRenderedComponent<InventoryTab> cut, string text) => cut.Find(".add-dialog .search input").Input(text);

    [Fact]
    public void Add_dialog_filters_by_category_and_search()
    {
        var editor = Start();
        var catalog = editor.Catalog();
        var cut = OpenDialog();

        // All, then every category the catalog has, each with its count
        var cats = cut.FindAll(".add-dialog .cat:not(.subc)");
        Assert.Equal("All", cats[0].GetAttribute("data-category"));
        Assert.Equal(Count(catalog.Count), cats[0].QuerySelector(".count")!.TextContent.Trim());
        Assert.Contains("on", cats[0].ClassList);
        Assert.Equal(catalog.Select(c => c.Category.ToString()).Distinct().Order(), cats.Skip(1).Select(c => c.GetAttribute("data-category")!).Order());
        foreach (var cat in cats.Skip(1))
            Assert.Equal(Count(catalog.Count(c => c.Category.ToString() == cat.GetAttribute("data-category"))), cat.QuerySelector(".count")!.TextContent.Trim());
        Assert.Empty(cut.FindAll(".add-dialog .cat.subc"));
        Assert.Equal(120, cut.FindAll(".add-dialog .tile").Count);          // the list stops at 120 tiles
        Assert.Contains($"Showing 120 of {Count(catalog.Count)}", cut.Find(".add-dialog .list").TextContent);
        Assert.Equal("Search all items…", cut.Find(".add-dialog .search input").GetAttribute("placeholder"));

        // Weapons expand into weapon types
        cut.Find(".add-dialog [data-category='Weapons']").Click();
        var types = cut.FindAll(".add-dialog .cat.subc");
        Assert.Equal(catalog.Where(c => c.Category == ItemCategory.Weapons).Select(c => c.SubCategory).Distinct().Order(),
            types.Select(t => t.GetAttribute("data-sub")!).Order());
        var weaponIds = catalog.Where(c => c.Category == ItemCategory.Weapons).Select(c => Id(c.Id)).ToHashSet();
        Assert.All(cut.FindAll(".add-dialog .tile"), t => Assert.Contains(t.GetAttribute("data-id")!, weaponIds));
        Assert.Contains("Showing 120 of 417", cut.Find(".add-dialog .list").TextContent);
        var bows = cut.Find(".add-dialog [data-sub='RangedBow']");
        Assert.Equal(("Bows", "49"), (bows.QuerySelector(".label")!.TextContent.Trim(), bows.QuerySelector(".count")!.TextContent.Trim()));
        Assert.Equal("Other", cut.Find(".add-dialog [data-sub='']").QuerySelector(".label")!.TextContent.Trim());

        bows.Click();
        Assert.Contains("on", cut.Find(".add-dialog [data-sub='RangedBow']").ClassList);
        var names = TileNames(cut);
        Assert.Equal(49, names.Count);
        Assert.Contains("Wolf's Snarl Longbow", names);
        var bowIds = catalog.Where(c => c.SubCategory == "RangedBow").Select(c => Id(c.Id)).Order();
        Assert.Equal(bowIds, cut.FindAll(".add-dialog .tile").Select(t => t.GetAttribute("data-id")!).Order());
        Assert.Equal("Lv 1–50 · Common", cut.Find($".add-dialog .tile[data-id='{Id(WolfsSnarlLongbow)}'] small").TextContent.Trim());

        // Equipment expands into slots; the slotless ones show as Other
        cut.Find(".add-dialog [data-category='Equipment']").Click();
        Assert.Equal("Other", cut.Find(".add-dialog [data-sub='None']").QuerySelector(".label")!.TextContent.Trim());
        Assert.Equal("223", cut.Find(".add-dialog [data-sub='None']").QuerySelector(".count")!.TextContent.Trim());
        Assert.NotNull(cut.Find(".add-dialog [data-sub='Helmet']"));
        Assert.Empty(cut.FindAll(".add-dialog [data-sub='RangedBow']"));

        // The search covers every item, by name or internal name
        Search(cut, "copper ore");
        Assert.Equal(["Copper Ore"], TileNames(cut));
        Assert.Equal("Lv 13 · Common", cut.Find(".add-dialog .tile small").TextContent.Trim());
        Assert.Contains("on", cut.Find(".add-dialog [data-category='All']").ClassList);
        Search(cut, "limestone");
        Assert.Equal("Common", cut.Find(".add-dialog .tile[data-id='cd603dcd'] small").TextContent.Trim());   // no level range
        Search(cut, "weapon_t1_2h_bowepic_02");
        Assert.Equal(["Wolf's Snarl Longbow"], TileNames(cut));

        // Rarity chips filter by the base rarity, and a second click clears them
        Search(cut, "");
        cut.FindAll(".add-dialog .rarities .chip").Single(c => c.TextContent.Trim() == "Legendary").Click();
        Assert.All(cut.FindAll(".add-dialog .tile"), t => Assert.Equal("Legendary", t.GetAttribute("data-rarity")));
        Assert.NotEmpty(cut.FindAll(".add-dialog .tile"));
        cut.FindAll(".add-dialog .rarities .chip").Single(c => c.TextContent.Trim() == "Legendary").Click();
        Assert.Contains(cut.FindAll(".add-dialog .tile"), t => t.GetAttribute("data-rarity") != "Legendary");
    }

    [Fact]
    public void Customization_never_listed()
    {
        var editor = Start();
        var hair = ReferenceGameData.Load().Items.All.First(i => i.Category == ItemCategory.Customization);
        var cut = OpenDialog();

        Assert.Empty(cut.FindAll(".add-dialog [data-category='Customization']"));
        Assert.Equal(Count(editor.Catalog().Count), cut.Find(".add-dialog [data-category='All'] .count").TextContent.Trim());

        Search(cut, hair.Name);
        Assert.Empty(cut.FindAll(".add-dialog .tile"));
        Assert.Contains("No items match.", cut.Find(".add-dialog .list").TextContent);
        Search(cut, "VC_");
        Assert.Empty(cut.FindAll(".add-dialog .tile"));
    }

    [Fact]
    public void Adding_selects_the_new_stack()
    {
        var editor = Start();
        var cut = OpenDialog();
        Assert.Empty(cut.FindAll(".add-dialog .adding .name"));
        Assert.True(cut.Find(".add-dialog button.add").HasAttribute("disabled"));   // nothing picked yet

        Search(cut, "copper ore");
        cut.Find(".add-dialog .tile").Click();

        Assert.Contains("sel", cut.Find(".add-dialog .tile").ClassList);
        Assert.Equal("Copper Ore", cut.Find(".add-dialog .adding .name").TextContent.Trim());
        Assert.Equal("Materials · Common", cut.Find(".add-dialog .adding .sub").TextContent.Trim());
        Assert.Equal("1", cut.Find(".add-dialog .how-many input").GetAttribute("value"));
        Assert.Contains("max 250", cut.Find(".add-dialog .how-many").TextContent);
        Assert.Empty(cut.FindAll(".add-dialog .level"));                    // a stack gets no level or rarity
        Assert.Empty(cut.FindAll(".add-dialog .rarity"));
        Assert.Equal("Backpack · first empty slot (1)", cut.Find(".add-dialog .goes-to").TextContent.Trim());
        Assert.Equal("Add to backpack", cut.Find(".add-dialog button.add").TextContent.Trim());

        cut.Find(".add-dialog .how-many input").Change("10");
        cut.Find(".add-dialog button.add").Click();

        Assert.Empty(cut.FindAll(".add-dialog"));
        Assert.Equal(["Added Copper Ore ×10"], editor.Current.Pending);
        Assert.Contains("sel", cut.Find("[data-ref='Inventory:0']").ClassList);
        Assert.Equal("Copper Ore", cut.Find(".detail .name").TextContent.Trim());
        Assert.Equal("10", cut.Find(".detail .stack input").GetAttribute("value"));

        cut.FindAll(".detail .stack .chip").Single(c => c.TextContent.Trim() == "Max").Click();
        Assert.Equal(["Added Copper Ore ×10", "Copper Ore 10 → 250"], editor.Current.Pending);
    }

    [Fact]
    public void Gear_is_added_one_at_a_time_at_the_chosen_level_and_rarity()
    {
        var editor = Start();
        var cut = OpenDialog();
        cut.Find(".add-dialog [data-category='Weapons']").Click();
        cut.Find(".add-dialog [data-sub='RangedBow']").Click();

        cut.Find($".add-dialog .tile[data-id='{Id(WolfsSnarlLongbow)}']").Click();

        Assert.Equal("Bows · Common", cut.Find(".add-dialog .adding .sub").TextContent.Trim());
        Assert.Equal("1", cut.Find(".add-dialog .how-many input").GetAttribute("value"));
        Assert.All(cut.FindAll(".add-dialog .how-many input, .add-dialog .how-many button"), e => Assert.True(e.HasAttribute("disabled")));
        Assert.Contains("gear doesn't stack", cut.Find(".add-dialog .how-many").TextContent);
        Assert.Equal("7", cut.Find(".add-dialog .level input").GetAttribute("value"));   // the character's level, within 1–50
        Assert.Contains("levels 1–50", cut.Find(".add-dialog .level").TextContent);
        Assert.Equal("Common", cut.Find(".add-dialog .rarity select").GetAttribute("value"));
        Assert.Equal("Common (its usual)", cut.Find(".add-dialog .rarity option[value='Common']").TextContent.Trim());

        cut.Find(".add-dialog .level input").Change("51");
        Assert.Contains("Enter a number from 1 to 50", cut.Find(".add-dialog .level").TextContent);
        cut.Find(".add-dialog .level input").Change("20");
        cut.Find(".add-dialog .rarity select").Change("Legendary");
        cut.Find(".add-dialog button.add").Click();

        var added = editor.Current.Backpack[0];
        Assert.Equal((WolfsSnarlLongbow, 1, 20, Rarity.Legendary), (added.Item!.Id, added.Item.Count, added.Item.Level, added.Item.Rarity));
        Assert.Equal(["Added Wolf's Snarl Longbow"], editor.Current.Pending);
        Assert.Equal("Wolf's Snarl Longbow", cut.Find(".detail .name").TextContent.Trim());
        Assert.Equal("20", cut.Find(".detail .level input").GetAttribute("value"));
    }

    [Fact]
    public void Level_and_rarity_are_offered_only_where_the_new_record_has_them()
    {
        var editor = Start();
        var cut = OpenDialog();

        Search(cut, "Ring of Health");
        cut.Find($".add-dialog .tile[data-id='{Id(RingOfHealth)}']").Click();
        Assert.Equal("10", cut.Find(".add-dialog .level .value").TextContent.Trim());     // fixed at 10
        Assert.Equal("Uncommon", cut.Find(".add-dialog .rarity select").GetAttribute("value"));

        Search(cut, "Tool_Torch01");
        cut.Find($".add-dialog .tile[data-id='{Id(Torch)}']").Click();
        Assert.Equal((1, false), editor.AddFields(Torch));
        Assert.Equal("1", cut.Find(".add-dialog .level .value").TextContent.Trim());      // fixed at 1
        Assert.Empty(cut.FindAll(".add-dialog .rarity"));

        cut.Find(".add-dialog button.add").Click();
        Assert.Equal((Torch, 1), (editor.Current.Backpack[0].Item!.Id, editor.Current.Backpack[0].Item!.Level));
    }

    // Session B: "Bow Of The Serpent" is five items, each of a fixed level the game enforces.
    [Fact]
    public void Fixed_level_variants_show_their_own_level_and_rarity_and_add_at_it()
    {
        var editor = Start();
        var cut = OpenDialog();
        Search(cut, "Bow Of The Serpent");

        Assert.Equal(["Lv 16 · Rare", "Lv 20 · Epic", "Lv 21 · Rare", "Lv 26 · Rare", "Lv 36 · Rare"],
            cut.FindAll(".add-dialog .tile small").Select(s => s.TextContent.Trim()).Order());

        cut.Find(".add-dialog .tile[data-id='1cf81b28']").Click();
        Assert.Equal("Bows · Rare", cut.Find(".add-dialog .adding .sub").TextContent.Trim());
        Assert.Empty(cut.FindAll(".add-dialog .level input, .add-dialog .level button"));
        Assert.Equal("21", cut.Find(".add-dialog .level .value").TextContent.Trim());
        Assert.Equal("set by the game", cut.Find(".add-dialog .level .max").TextContent.Trim());

        cut.Find(".add-dialog button.add").Click();
        Assert.Equal((0x1cf81b28u, 21), (editor.Current.Backpack[0].Item!.Id, editor.Current.Backpack[0].Item!.Level));
        Assert.Equal("21", cut.Find(".detail .level .value").TextContent.Trim());
    }

    // The dialog used to send a level of at least 1 for every item, so a 0–0 item got level 1 instead of its 0.
    [Fact]
    public void A_0_to_0_item_is_added_at_level_0()
    {
        var editor = Start(1);
        var cut = OpenDialog();
        Search(cut, "Water Pouch");
        cut.Find($".add-dialog .tile[data-id='{Id(WaterPouch)}']").Click();
        Assert.Equal("0", cut.Find(".add-dialog .level .value").TextContent.Trim());

        cut.Find(".add-dialog button.add").Click();
        var added = editor.Current.Backpack.Single(s => s.Ref == new ItemRef(ContainerKind.Extra, 1)).Item!;   // its first free slot
        Assert.Equal((WaterPouch, 0), (added.Id, added.Level));
    }

    [Fact]
    public void No_room_in_the_backpack_says_so_and_adds_nothing()
    {
        var editor = Start();
        for (var i = 0; i < editor.Current.Backpack.Count; i++)
            editor.AddItem(CopperOre, 1, null, null, null);
        var pending = editor.Current.Pending.Count;
        var cut = OpenDialog();

        Search(cut, "copper ore");
        cut.Find(".add-dialog .tile").Click();

        Assert.Equal("No room in the backpack", cut.Find(".add-dialog .goes-to").TextContent.Trim());
        Assert.True(cut.Find(".add-dialog button.add").HasAttribute("disabled"));
        Assert.Equal(pending, editor.Current.Pending.Count);
    }

    [Fact]
    public void Close_adds_nothing()
    {
        var editor = Start();
        var cut = OpenDialog();
        Search(cut, "copper ore");
        cut.Find(".add-dialog .tile").Click();

        cut.Find(".add-dialog button.close").Click();

        Assert.Empty(cut.FindAll(".add-dialog"));
        Assert.Empty(editor.Current.Pending);
    }
}
