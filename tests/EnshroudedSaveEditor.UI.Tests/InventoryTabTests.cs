using AngleSharp.Dom;
using Bunit;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.UI.Screens;
using EnshroudedSaveEditor.UI.Services;
using EnshroudedSaveEditor.UI.Tabs;
using Microsoft.Extensions.DependencyInjection;

namespace EnshroudedSaveEditor.Tests;

/// <summary>Owner's rule: temp copies of the fixtures, a temp app data root and a fake process probe only.</summary>
public sealed class InventoryTabTests : IDisposable
{
    const uint CopperOre = 0x2b9d5b6f;            // Materials, max 250
    const uint BronzeKey = 0x7d95401f;            // max 5
    const uint RingOfHealth = 0xf29b7aa9;         // Equipment/Ring
    const uint LesserRingOfHealth = 0x3993797a;   // character2 Equipped position 10
    const uint AlchemistHat = 0xe5a9ac59;         // character2 Equipped position 5
    const string EquipLocked = "Equipping into an empty slot and unequipping aren't available yet: swap with an item that fits instead";

    readonly string _root = Fixtures.CopyToTemp();
    readonly string _appData = Path.Combine(Path.GetTempPath(), "enshrouded-appdata-" + Guid.NewGuid().ToString("N"));
    readonly List<string> _dirs = [];
    readonly BunitContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
        foreach (var d in _dirs.Append(_root).Append(_appData))
            try { Directory.Delete(d, true); } catch { }
    }

    // character2: Amadis level 7. The backpack (Inventory 0–15, 32–39, Extra 0–15) is empty; the action bar is full:
    // Scorching Wand at 16 (level 11, Legendary, 352/363), Health Potion ×9 of 20 at 22, Fireball I ×87 of 500 at 30.
    EditorService Start(int character = 2)
    {
        var options = new EditorOptions([_root], _appData, new EditorServiceTests.FakeProbe(), null);
        var editor = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        _ctx.Services.AddSingleton(editor);
        _ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        editor.Open(C(character));
        return editor;
    }

    string C(int n) => Path.Combine(_root, $"character{n}.ksd");
    static ItemRef Inv(byte slot) => new(ContainerKind.Inventory, slot);
    static ItemRef Eq(byte position) => new(ContainerKind.Equipped, position);
    static ItemRef Extra(byte slot) => new(ContainerKind.Extra, slot);
    static string Slot(ItemRef r) => $"[data-ref='{r.Kind}:{r.Slot}']";

    static IEnumerable<SlotView> AllSlots(SessionView v) => v.Equipped.Concat(v.Backpack).Concat(v.Hotbar).Concat(v.Overflow);
    static ItemView? ItemAt(SessionView v, ItemRef r) => AllSlots(v).Single(s => s.Ref == r).Item;

    static IElement Chip(IRenderedComponent<InventoryTab> cut, string label) =>
        cut.FindAll(".detail .stack .chip").Single(c => c.TextContent.Trim() == label);

    /// <summary>Changes the temp copy with Core directly, the way a game save would (its backups go to a temp folder).</summary>
    void Plant(string path, Action<SaveSession> change)
    {
        var s = SaveSession.Open(path, Fixtures.ItemIds().Contains);
        change(s);
        s.MarkChanged();
        var backups = Path.Combine(Path.GetTempPath(), "enshrouded-plant-" + Guid.NewGuid().ToString("N"));
        _dirs.Add(backups);
        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(backups), new EditorServiceTests.FakeProbe()));
    }

    [Fact]
    public void Selecting_a_stack_shows_stepper_with_max()
    {
        Start();
        var cut = _ctx.Render<InventoryTab>();
        Assert.Empty(cut.FindAll(".detail .stack"));
        Assert.Contains("Select an item", cut.Find(".detail").TextContent);

        cut.Find(Slot(Inv(30))).Click();

        Assert.Contains("sel", cut.Find(Slot(Inv(30))).ClassList);
        Assert.Equal("Fireball I", cut.Find(".detail .name").TextContent.Trim());
        Assert.Equal("Ammunition · Rare", cut.Find(".detail .sub").TextContent.Trim());
        Assert.Equal("Action bar · slot 15", cut.Find(".detail .where").TextContent.Trim());
        Assert.Equal("87", cut.Find(".detail .stack input").GetAttribute("value"));
        Assert.Contains("max 500", cut.Find(".detail .stack").TextContent);
        Assert.Equal(["1", "Half", "Max"], cut.FindAll(".detail .stack .chip").Select(c => c.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".detail .level"));          // a stack has no level, rarity or durability
        Assert.Empty(cut.FindAll(".detail .rarity"));
        Assert.Empty(cut.FindAll(".detail .durability"));
        Assert.False(cut.Find("button.remove").HasAttribute("disabled"));
    }

    [Fact]
    public void Max_chip_sets_count_to_max_stack()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();
        cut.Find(Slot(Inv(30))).Click();

        Chip(cut, "Max").Click();

        Assert.Equal(500, ItemAt(editor.Current, Inv(30))!.Count);
        Assert.Equal(["Fireball I 87 → 500"], editor.Current.Pending);
        Assert.Equal("500", cut.Find(".detail .stack input").GetAttribute("value"));
        Assert.True(Chip(cut, "Max").HasAttribute("disabled"));       // already at the value: no "Already …" edit
        Assert.True(cut.Find(".detail .stack button.plus").HasAttribute("disabled"));
        Assert.Equal("500", cut.Find(Slot(Inv(30)) + " .n").TextContent.Trim());

        Chip(cut, "Half").Click();
        Assert.Equal(250, ItemAt(editor.Current, Inv(30))!.Count);
        Chip(cut, "1").Click();
        Assert.Equal(1, ItemAt(editor.Current, Inv(30))!.Count);
        Assert.True(Chip(cut, "1").HasAttribute("disabled"));
        Assert.True(cut.Find(".detail .stack button.minus").HasAttribute("disabled"));
        Assert.Equal(["Fireball I 87 → 500", "Fireball I 500 → 250", "Fireball I 250 → 1"], editor.Current.Pending);
    }

    [Fact]
    public void Half_rounds_up_and_typing_the_same_count_is_skipped()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();
        cut.Find(Slot(Inv(22))).Click();                                   // Health Potion ×9 of 20

        Assert.False(Chip(cut, "Half").HasAttribute("disabled"));
        cut.Find(".detail .stack input").Change("9");
        Assert.Empty(editor.Current.Pending);
        Assert.Empty(cut.FindAll(".banner"));

        Chip(cut, "Half").Click();
        Assert.Equal(10, ItemAt(editor.Current, Inv(22))!.Count);          // 20 / 2
        cut.Find(".detail .stack button.plus").Click();
        Assert.Equal(11, ItemAt(editor.Current, Inv(22))!.Count);

        var (_, key) = editor.AddItem(BronzeKey, 1, null, null, null);
        cut.Find(Slot(key)).Click();
        Chip(cut, "Half").Click();
        Assert.Equal(3, ItemAt(editor.Current, key)!.Count);               // ⌈5 / 2⌉
    }

    [Fact]
    public void Gear_shows_level_rarity_and_durability()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Inv(16))).Click();                                   // Scorching Wand, level 11, Legendary, 352/363

        Assert.Equal("Scorching Wand", cut.Find(".detail .name").TextContent.Trim());
        Assert.Empty(cut.FindAll(".detail .stack"));                       // gear doesn't stack
        Assert.Equal("11", cut.Find(".detail .level input").GetAttribute("value"));
        Assert.Contains("levels 1–50", cut.Find(".detail .level").TextContent);
        Assert.Equal("Legendary", cut.Find(".detail .rarity select").GetAttribute("value"));
        var durability = cut.FindAll(".detail .durability input");
        Assert.Equal(["352", "363"], durability.Select(i => i.GetAttribute("value")));

        cut.Find(".detail .level button.plus").Click();
        cut.Find(".detail .rarity select").Change("Epic");
        cut.Find(".detail .rarity select").Change("Epic");                 // the same rarity again: skipped
        cut.FindAll(".detail .durability input")[0].Change("363");
        cut.FindAll(".detail .durability input")[1].Change("300");         // the current value follows a lower maximum

        Assert.Equal(
        [
            "Scorching Wand level 11 → 12",
            "Scorching Wand Legendary → Epic",
            "Scorching Wand durability 352/363 → 363/363",
            "Scorching Wand durability 363/363 → 300/300",
        ], editor.Current.Pending);
        Assert.Empty(cut.FindAll(".banner"));
        cut.Find(".detail .level input").Change("51");
        Assert.Contains("Enter a number from 1 to 50", cut.Find(".detail .level").TextContent);
        cut.Find(".detail .level input").Change("50");
        Assert.True(cut.Find(".detail .level button.plus").HasAttribute("disabled"));
        Assert.Equal(50, ItemAt(editor.Current, Inv(16))!.Level);
    }

    // Session B: the game sets a fixed-level item's level (base plus upgrades), so it is shown, not edited.
    [Fact]
    public void Fixed_level_gear_shows_its_level_read_only()
    {
        Start();
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Eq(5))).Click();                                     // Alchemist Apprentice Hat: level 10, base 5
        Assert.Empty(cut.FindAll(".detail .level input, .detail .level button"));
        Assert.Equal("10", cut.Find(".detail .level .value").TextContent.Trim());
        Assert.Contains("set by the game: base 5 plus upgrades", cut.Find(".detail .level").TextContent);
        Assert.NotNull(cut.Find(".detail .rarity select"));                // its rarity can still change

        cut.Find(Slot(Inv(20))).Click();                                   // Torch: level 1, base 1
        Assert.Equal("1", cut.Find(".detail .level .value").TextContent.Trim());
        Assert.Equal("set by the game", cut.Find(".detail .level .max").TextContent.Trim());
    }

    [Fact]
    public void Remove_from_backpack_removes_the_stack_and_clears_the_selection()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();
        cut.Find(Slot(Inv(30))).Click();

        cut.Find("button.remove").Click();

        Assert.Null(ItemAt(editor.Current, Inv(30)));
        Assert.Equal(["Removed Fireball I ×87"], editor.Current.Pending);
        Assert.Contains("Select an item", cut.Find(".detail").TextContent);
        Assert.Contains("addslot", cut.Find(Slot(Inv(30))).ClassList);
    }

    [Fact]
    public void Equipped_shows_16_positions_with_the_appearance_ones_apart()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();

        var equipped = cut.FindAll(".equipped [data-ref^='Equipped:']");
        Assert.Equal(16, equipped.Count);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => $"Equipped:{i}").Order(), equipped.Select(e => e.GetAttribute("data-ref")!).Order());
        var appearance = cut.FindAll(".appearance [data-ref^='Equipped:']");
        Assert.Equal(["Equipped:0", "Equipped:1", "Equipped:2", "Equipped:3", "Equipped:4"], appearance.Select(e => e.GetAttribute("data-ref")));
        Assert.Contains("Appearance", cut.Find(".appearance").TextContent);
        Assert.Equal(InventoryLayout.EquipPositions.Select(p => (string?)p.Label),
            Enumerable.Range(0, 16).Select(i => cut.Find(Slot(Eq((byte)i))).GetAttribute("data-label")));
        Assert.Equal("pos://" + InventoryLayout.EquipPositions[0], cut.Find(Slot(Eq(0)) + " img").GetAttribute("src"));   // empty: the position glyph
        Assert.Contains("empty", cut.Find(Slot(Eq(0))).ClassList);
        Assert.Empty(cut.FindAll(".equipped .addslot"));                   // new items go into the backpack only

        Assert.Equal(AlchemistHat, editor.Current.Equipped[5].Item!.Id);
        cut.Find(Slot(Eq(5))).Click();
        Assert.Equal("Alchemist Apprentice Hat", cut.Find(".detail .name").TextContent.Trim());
        Assert.Equal("Equipped · Helmet", cut.Find(".detail .where").TextContent.Trim());
        Assert.Empty(cut.FindAll("button.remove"));                         // taking gear off isn't available
        Assert.Equal("10", cut.Find(".detail .level .value").TextContent.Trim());
    }

    [Fact]
    public void Backpack_grid_follows_the_view_order_with_a_plus_in_free_slots()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();

        var grid = cut.FindAll(".backpack .bag .slot");
        Assert.Equal(editor.Current.Backpack.Select(s => $"{s.Ref.Kind}:{s.Ref.Slot}"), grid.Select(e => e.GetAttribute("data-ref")));
        Assert.Equal(40, grid.Count);
        Assert.All(grid, e => Assert.Contains("addslot", e.ClassList));
        Assert.All(grid, e => Assert.Equal("+", e.TextContent.Trim()));
        Assert.Contains("0 / 40", cut.Find(".backpack .ph").TextContent);
        Assert.Equal("+ Add item", cut.Find("button.add-item").TextContent.Trim());
    }

    [Fact]
    public void Action_bar_rows_render_16_slots()
    {
        Start();
        var cut = _ctx.Render<InventoryTab>();

        Assert.Equal("Action bar", cut.Find(".action-bar h4").TextContent.Trim());
        var slots = cut.FindAll(".action-bar .slot");
        Assert.Equal(16, slots.Count);
        Assert.Equal(InventoryLayout.HotbarSlots.Select(s => $"Inventory:{s}"), slots.Select(e => e.GetAttribute("data-ref")));
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], slots.Take(8).Select(e => e.QuerySelector(".k")!.TextContent.Trim()));
        Assert.All(slots.Skip(8), e => Assert.Null(e.QuerySelector(".k")));
        Assert.Equal(2, cut.FindAll(".action-bar .bag-grid").Count);       // two rows of 8
        Assert.Equal("87", cut.Find(Slot(Inv(30)) + " .n").TextContent.Trim());
        Assert.Contains("r3", cut.Find(Slot(Inv(30))).ClassList);          // Fireball I is Rare
        Assert.Empty(cut.FindAll(Slot(Inv(16)) + " .n"));                  // gear: no count badge
    }

    [Fact]
    public void No_overflow_strip_when_nothing_is_outside_the_backpack()
    {
        Start();
        Assert.Empty(_ctx.Render<InventoryTab>().FindAll(".outside"));
    }

    [Fact]
    public void Overflow_record_planted_past_the_extension_can_be_moved_into_a_free_slot()
    {
        Plant(C(2), s => s.Player.Container(ContainerKind.Extra)!.Records.Add(new ItemRecord(20, CopperOre, 5)));
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();

        Assert.Contains("Outside the backpack", cut.Find(".outside").TextContent);
        var outside = Assert.Single(cut.FindAll(".outside .slot"));
        Assert.Equal("Extra:20", outside.GetAttribute("data-ref"));
        outside.Click();
        Assert.Equal("Outside the backpack", cut.Find(".detail .where").TextContent.Trim());

        cut.Find(Slot(Extra(20))).DragStart();
        cut.Find(Slot(Inv(3))).Drop();

        Assert.Empty(cut.FindAll(".outside"));
        Assert.Empty(editor.Current.Overflow);
        Assert.Equal((CopperOre, 5), (ItemAt(editor.Current, Inv(3))!.Id, ItemAt(editor.Current, Inv(3))!.Count));
        Assert.Equal(["Moved Copper Ore"], editor.Current.Pending);
        Assert.Contains("sel", cut.Find(Slot(Inv(3))).ClassList);            // the moved stack stays selected
    }

    [Fact]
    public void Dragging_within_the_backpack_moves_or_swaps()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Inv(30))).DragStart();
        cut.Find(Slot(Inv(35))).Drop();
        cut.Find(Slot(Inv(35))).DragStart();
        cut.Find(Slot(Inv(22))).Drop();

        Assert.Equal(["Moved Fireball I", "Swapped Fireball I and Health Potion"], editor.Current.Pending);
        Assert.Equal("Fireball I", ItemAt(editor.Current, Inv(22))!.Name);
        Assert.Equal("Health Potion", ItemAt(editor.Current, Inv(35))!.Name);
        Assert.Contains("sel", cut.Find(Slot(Inv(22))).ClassList);
        Assert.Equal("Fireball I", cut.Find(".detail .name").TextContent.Trim());
    }

    [Fact]
    public void Dragging_a_ring_onto_an_equipped_ring_swaps_them()
    {
        var editor = Start();
        editor.AddItem(RingOfHealth, 1, null, null, Inv(35));
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Inv(35))).DragStart();
        cut.Find(Slot(Eq(10))).Drop();

        Assert.Equal(RingOfHealth, editor.Current.Equipped[10].Item!.Id);
        Assert.Equal(LesserRingOfHealth, ItemAt(editor.Current, Inv(35))!.Id);
        Assert.Equal("Swapped Ring of Health and Lesser Ring of Health", editor.Current.Pending[^1]);
        Assert.Empty(cut.FindAll(".banner"));
        Assert.Contains("sel", cut.Find(Slot(Eq(10))).ClassList);
        Assert.Equal("Equipped · Ring", cut.Find(".detail .where").TextContent.Trim());
    }

    [Fact]
    public void Dropping_into_an_empty_equipped_position_shows_the_refusal()
    {
        var editor = Start();
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Inv(30))).DragStart();
        cut.Find(Slot(Eq(0))).Drop();

        Assert.Equal(EquipLocked, cut.Find(".banner").TextContent.Trim());
        Assert.Empty(editor.Current.Pending);
        Assert.Equal("Fireball I", ItemAt(editor.Current, Inv(30))!.Name);

        cut.Find(Slot(Eq(5))).DragStart();                                  // unequipping into a free slot: refused too
        cut.Find(Slot(Inv(0))).Drop();
        Assert.Equal(EquipLocked, cut.Find(".banner").TextContent.Trim());
        Assert.Empty(editor.Current.Pending);

        cut.Find(Slot(Inv(30))).DragStart();                                // a good edit clears the message
        cut.Find(Slot(Inv(0))).Drop();
        Assert.Empty(cut.FindAll(".banner"));
    }

    [Fact]
    public void Add_disabled_for_InPlaceOnly_character()
    {
        File.WriteAllBytes(C(2), Fixtures.InPlaceOnlyCharacter2());
        var editor = Start();
        Assert.Equal(InventoryStatus.InPlaceOnly, editor.Current.Inventory);
        var cut = _ctx.Render<InventoryTab>();

        Assert.Equal(InventoryEditor.NeedsFull, cut.Find(".banner").TextContent.Trim());
        Assert.True(cut.Find("button.add-item").HasAttribute("disabled"));
        Assert.Equal(InventoryEditor.NeedsFull, cut.Find("button.add-item").GetAttribute("title"));
        Assert.All(cut.FindAll(".addslot"), e => Assert.True(e.HasAttribute("disabled")));
        Assert.All(cut.FindAll(".slot"), e => Assert.NotEqual("true", e.GetAttribute("draggable")));

        cut.Find(Slot(Inv(30))).Click();
        Assert.True(cut.Find("button.remove").HasAttribute("disabled"));
        Chip(cut, "Max").Click();                                           // same-size edits still work
        Assert.Equal(["Fireball I 87 → 500"], editor.Current.Pending);
    }

    [Fact]
    public void A_full_character_can_drag_add_and_remove()
    {
        Start();
        var cut = _ctx.Render<InventoryTab>();

        Assert.Empty(cut.FindAll(".banner"));
        Assert.False(cut.Find("button.add-item").HasAttribute("disabled"));
        Assert.Equal("true", cut.Find(Slot(Inv(30))).GetAttribute("draggable"));
        Assert.Equal("true", cut.Find(Slot(Eq(5))).GetAttribute("draggable"));
        Assert.All(cut.FindAll(".addslot"), e => Assert.False(e.HasAttribute("disabled")));
    }

    [Fact]
    public void Unreadable_inventory_says_so()
    {
        var file = KsdFile.Parse(File.ReadAllBytes(C(2)));
        var raw = file["player_character"].GetData();
        var statusEnd = PlayerCharacter.LevelOffset + 12 + 5 * 30 + 4 + 27 + 30;   // as InventoryEditorTests.NotFound_inventory_rejects_every_edit
        file["player_character"].SetData(raw.AsSpan(0, statusEnd).ToArray().Concat(new byte[64]).ToArray());
        File.WriteAllBytes(C(2), file.ToBytes());
        var editor = Start();
        Assert.Equal(InventoryStatus.NotFound, editor.Current.Inventory);

        var cut = _ctx.Render<InventoryTab>();

        Assert.Equal("Inventory couldn't be read for this character.", cut.Find(".banner").TextContent.Trim());
        Assert.Empty(cut.FindAll(".slot"));
        Assert.Empty(cut.FindAll("button.add-item"));
    }

    [Fact]
    public void Read_only_session_disables_every_control()
    {
        File.WriteAllBytes(C(2), Fixtures.PatchBlock(File.ReadAllBytes(C(2)), "player_character", b => b[10] = 6));
        var editor = Start();
        Assert.True(editor.Current.ReadOnly);
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Inv(16))).Click();                                    // items can still be looked at

        Assert.Equal("Scorching Wand", cut.Find(".detail .name").TextContent.Trim());
        Assert.All(cut.FindAll(".detail input, .detail button, .detail select, button.add-item, .addslot"),
            e => Assert.True(e.HasAttribute("disabled")));
        Assert.All(cut.FindAll(".slot"), e => Assert.NotEqual("true", e.GetAttribute("draggable")));
    }

    [Fact]
    public void Empty_slot_plus_opens_add_item_for_that_slot()
    {
        Start(1);                                                           // character1: action bar slots 30 and 31 are free
        var cut = _ctx.Render<InventoryTab>();

        cut.Find(Slot(Inv(30))).Click();

        Assert.NotNull(cut.Find(".add-dialog"));
        Assert.Equal("Action bar · slot 15", cut.Find(".add-dialog .goes-to").TextContent.Trim());
        cut.Find(".add-dialog button.close").Click();
        Assert.Empty(cut.FindAll(".add-dialog"));

        cut.Find(Slot(Extra(1))).Click();
        Assert.Equal("Backpack · slot 26", cut.Find(".add-dialog .goes-to").TextContent.Trim());
    }

    [Fact]
    public void Unknown_items_show_their_id_and_one_of_them_is_not_a_stack()
    {
        Start(1);                                                           // character1: unknown appearance pieces, one each
        var cut = _ctx.Render<InventoryTab>();

        Assert.Empty(cut.FindAll(Slot(Eq(0)) + " .n"));
        cut.Find(Slot(Eq(0))).Click();
        Assert.Equal("Unknown item 01e879a5", cut.Find(".detail .name").TextContent.Trim());
        Assert.Equal("Unknown", cut.Find(".detail .sub").TextContent.Trim());
        Assert.Empty(cut.FindAll(".detail .stack"));
        Assert.Equal("Equipped · Helmet appearance", cut.Find(".detail .where").TextContent.Trim());
    }

    [Fact]
    public void Editor_hosts_the_inventory_tab()
    {
        Start();
        var cut = _ctx.Render<Editor>();

        cut.FindAll("button.tab").Single(t => t.TextContent == "Inventory").Click();

        Assert.NotNull(cut.Find(".inventory"));
        cut.Find(Slot(Inv(30))).Click();
        Chip(cut.FindComponent<InventoryTab>(), "Max").Click();
        Assert.Contains("Fireball I 87 → 500", cut.Find(".pending").TextContent);
    }
}
