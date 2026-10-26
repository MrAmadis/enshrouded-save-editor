using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class InventoryEditorTests : IDisposable
{
    sealed class FakeProbe : IProcessProbe
    {
        public bool IsGameRunning() => false;
        public bool IsSteamRunning() => false;
    }

    const uint FireballI = 0xe988b200;          // character2 Inventory 30, ×87, max 500
    const uint HealthPotion = 0xccd46f7a;       // character2 Inventory 22, ×9, max 20
    const uint IceBolt = 0xbce7922f;            // character2 Inventory 31, ×42
    const uint CopperOre = 0x2b9d5b6f;          // max 250
    const uint WolfsSnarlLongbow = 0x29433573;  // Weapons/Weapon/RangedBow, levels 1–50
    const uint RingOfHealth = 0xf29b7aa9;       // Equipment/Ring, levels 10–10, base Uncommon
    const uint WaxTorch = 0xe9430638;           // Weapons/Weapon/OneHandWeapon, levels 3–3
    const uint WaterPouch = 0xe4831ad8;         // character2 Inventory 28, flags 04 (bit-2 bytes); levels 0–0
    const uint AdventurerHelmet = 0xff1b25e5;   // Equipment/Helmet, levels 10–10
    const uint ThreadbarePants = 0xecc1c60c;    // Equipment/Legs, levels 0–0 (the only such gear)
    const uint ScorchingWand = 0xe589d7a4;      // character2 Inventory 16, flags 33, durability 352/363
    const uint AlchemistHat = 0xe5a9ac59;       // character2 Equipped position 5 (record 0)
    const uint SmallBackpackNew = 0xde38058e;   // Backpack_T1_NEW, 2 rows: character1/2 Equipped position 15
    const uint TinyBackpack = 0x1fa74ed7;       // Backpack_T0, 1 row
    const uint LargeBackpack = 0x3b624853;      // Backpack_T3_NEW, 4 rows
    const uint HerosShield = 0x0262764a;        // Equipment/Shield
    const uint ImprovedHook = 0x6aa6a1d4;       // Tools, Tool_Grappling_Hook_improved
    const uint AdvancedGlider = 0x534ba901;     // Tools, Tool_Glider_T2
    const uint TalionsBow = 0x9ecd0c33;         // Weapons/RangedBow, levels 1–50
    const uint BowOfTheSerpent21 = 0x1cf81b28;  // Weapons/RangedBow, levels 21–21: one of five items of that name
    const uint EagleEyeTrousers = 0x88a1302d;   // Equipment/Legs, levels 30–50
    const uint GemOfStormsEye = 0xe52a5ac0;     // WeaponGem, levels 1–105 in the data; character1 Inventory 7, level 16

    const string NeedsFull = "Adding and moving items isn't available for this character yet";
    const string EquipLocked = "Equipping into an empty slot and unequipping aren't available yet: swap with an item that fits instead";

    readonly string _tmp = Fixtures.CopyToTemp();
    readonly string _backupRoot = Path.Combine(Path.GetTempPath(), "enshrouded-backups-" + Guid.NewGuid().ToString("N"));
    readonly IItemCatalog _items = ReferenceGameData.Load().Items;

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
        try { Directory.Delete(_backupRoot, true); } catch { }
    }

    static Func<uint, bool> IsItem => Fixtures.ItemIds().Contains;
    SaveSession Open(string f = "character2.ksd") => SaveSession.Open(Path.Combine(_tmp, f), IsItem);
    InventoryEditor Ed(SaveSession s) => new(s, _items);
    ItemInfo Item(uint id) => _items.Find(id)!;
    static ItemRef Inv(byte slot) => new(ContainerKind.Inventory, slot);
    static ItemRef Eq(byte slot) => new(ContainerKind.Equipped, slot);
    static ItemRef Extra(byte slot) => new(ContainerKind.Extra, slot);

    /// <summary>What the session would write, read back.</summary>
    static PlayerCharacter Reparsed(SaveSession s) => PlayerCharacter.Parse(s.Player.ToBytes(), IsItem);

    static List<ItemRecord> Records(PlayerCharacter pc, ContainerKind k) => pc.Container(k)!.Records;

    static EditRejectedException Rejected(Action edit) => Assert.Throws<EditRejectedException>(edit);

    [Fact]
    public void At_treats_ghosts_and_gaps_as_empty()
    {
        var e = Ed(Open());
        Assert.Null(e.At(Inv(15)));                                // Bronze Sword ×0 (ghost)
        Assert.Null(e.At(Inv(35)));                                // no record
        Assert.Null(e.At(Extra(0)));
        Assert.Equal(FireballI, e.At(Inv(30))!.ItemId);
        Assert.Equal(AlchemistHat, e.At(Eq(5))!.ItemId);
    }

    // An Equipped ref is a position (InventoryLayout.EquipPositions), not the record's slot byte.
    [Fact]
    public void Equipped_slots_are_addressed_by_position()
    {
        var e = Ed(Open());                                        // character2: mask ffe0, positions 5–15
        foreach (var p in new byte[] { 0, 1, 2, 3, 4, 16, 255 }) Assert.Null(e.At(Eq(p)));
        Assert.Equal(AlchemistHat, e.At(Eq(5))!.ItemId);
        Assert.Equal(0x3993797au, e.At(Eq(10))!.ItemId);            // Lesser Ring of Health
        Assert.Equal(0x53bf5455u, e.At(Eq(12))!.ItemId);            // Spectral Ward
        Assert.Equal(SmallBackpackNew, e.At(Eq(15))!.ItemId);
        Assert.Equal(0x01e879a5u, Ed(Open("character1.ksd")).At(Eq(0))!.ItemId);   // an appearance item
    }

    [Fact]
    public void SetCount_edits_and_caps_at_max_stack()
    {
        var s = Open(); var e = Ed(s);
        e.SetCount(Inv(30), 120);                                  // Fireball I 87 → 120
        Assert.Equal(120, e.At(Inv(30))!.Count);
        Assert.Equal(1, s.ChangeCount);

        Assert.Equal(500, Item(FireballI).MaxStack);
        Assert.Equal("The count must be between 1 and 500.", Rejected(() => e.SetCount(Inv(30), 501)).Message);
        e.SetCount(Inv(30), 500);
        Assert.Equal(2, s.ChangeCount);
        Assert.Equal(500, Records(Reparsed(s), ContainerKind.Inventory).Single(r => r.Slot == 30).Count);
    }

    [Fact]
    public void SetCount_rejects_invalid()
    {
        var s = Open(); var e = Ed(s);
        var before = s.Player.ToBytes();
        foreach (var bad in new[] { 0, -1, 501 })
            Rejected(() => e.SetCount(Inv(30), bad));
        Assert.Equal("Already 87", Rejected(() => e.SetCount(Inv(30), 87)).Message);
        Rejected(() => e.SetCount(Inv(35), 5));                    // empty slot
        Rejected(() => e.SetCount(Inv(15), 1));                    // ghost counts as empty
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    [Fact]
    public void SetCount_on_an_unknown_item_allows_up_to_65535()
    {
        var s = Open("character0.ksd"); var e = Ed(s);
        var r = Inv(25);                                           // f262a2a9 ×5: missing from the reference data
        Assert.Null(_items.Find(e.At(r)!.ItemId));
        Rejected(() => e.SetCount(r, 65536));
        Rejected(() => e.SetCount(r, 0));
        e.SetCount(r, 65535);
        Assert.Equal(65535, Records(Reparsed(s), ContainerKind.Inventory).Single(x => x.Slot == 25).Count);
    }

    [Fact]
    public void Gear_fields_edit_in_range_and_only_where_present()
    {
        var s = Open(); var e = Ed(s);
        var wand = Inv(16);                                        // Scorching Wand: level 11, 352/363, rarity 5/5
        e.SetItemLevel(wand, 30);
        e.SetRarity(wand, Rarity.Rare);
        e.SetDurability(wand, 400, 400);
        Assert.Equal(3, s.ChangeCount);

        var before = s.Player.ToBytes();
        Rejected(() => e.SetItemLevel(wand, 0));                   // levels 1–50
        Rejected(() => e.SetItemLevel(wand, 51));
        Rejected(() => e.SetRarity(wand, (Rarity)9));
        Rejected(() => e.SetDurability(wand, 401, 400));
        Rejected(() => e.SetDurability(wand, 0, 0));
        var fireball = Inv(30);                                    // a plain record: no fields
        Rejected(() => e.SetItemLevel(fireball, 10));
        Rejected(() => e.SetRarity(fireball, Rarity.Epic));
        Rejected(() => e.SetDurability(fireball, 1, 1));
        Assert.Equal(3, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());

        var w = Records(Reparsed(s), ContainerKind.Inventory).Single(r => r.Slot == 16);
        Assert.Equal((byte)30, w.Level);
        Assert.Equal(((byte)3, (byte)5), w.Rarity);                // the extra byte is kept
        Assert.Equal((400u, 400u), w.Durability);
        Assert.Equal(0x33, w.Flags);
    }

    [Fact]
    public void Equipped_records_allow_in_place_edits()
    {
        var s = Open(); var e = Ed(s);
        e.SetRarity(Eq(5), Rarity.Epic);                           // Alchemist Apprentice Hat
        e.SetDurability(Eq(12), 50, 70);                           // Spectral Ward: 68/68
        Assert.Equal(2, s.ChangeCount);
        var eq = Records(Reparsed(s), ContainerKind.Equipped);
        Assert.Equal(11, eq.Count);
        Assert.Equal(((byte)Rarity.Epic, (byte)5), eq[0].Rarity);
        Assert.Equal((50u, 70u), eq[7].Durability);
    }

    // Session B (docs/research/in-game-findings.md): the game sets the level of fixed-level gear (reference
    // MinLevel == MaxLevel) to its base plus upgrades, and rewrites any other. This chest is 5 + 5 upgrades.
    [Fact]
    public void Fixed_level_gear_refuses_level_edits()
    {
        var s = Open(); var e = Ed(s);
        var before = s.Player.ToBytes();
        Assert.Equal("This item's level is set by the game: base 5 plus upgrades.",
            Rejected(() => e.SetItemLevel(Eq(6), 12)).Message);    // Alchemist Apprentice Chest: level 10, reference 5–5
        Assert.Equal("This item's level is set by the game: base 1 plus upgrades.",
            Rejected(() => e.SetItemLevel(Inv(20), 3)).Message);   // Torch: level 1, reference 1–1
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    // Session B: Talion's Bow (1–50) kept the levels the editor wrote, 50 and 18.
    [Fact]
    public void Ranged_gear_takes_levels_within_its_range()
    {
        var s = Open(); var e = Ed(s);
        const string OneToFifty = "The level must be between 1 and 50.";
        var bow = e.Add(Item(TalionsBow), 1, 18, null, null);
        e.SetItemLevel(bow, 50);
        e.SetItemLevel(bow, 1);
        Assert.Equal(OneToFifty, Rejected(() => e.SetItemLevel(bow, 51)).Message);
        Assert.Equal(OneToFifty, Rejected(() => e.SetItemLevel(bow, 0)).Message);
        Assert.Equal(OneToFifty, Rejected(() => e.Add(Item(TalionsBow), 1, 51, null, null)).Message);

        var trousers = e.Add(Item(EagleEyeTrousers), 1, null, null, null);
        Assert.Equal((byte)30, e.At(trousers)!.Level);             // the character's level 7, clamped to 30–50
        Assert.Equal("The level must be between 30 and 50.", Rejected(() => e.SetItemLevel(trousers, 29)).Message);
        Assert.Equal("The level must be between 30 and 50.", Rejected(() => e.Add(Item(EagleEyeTrousers), 1, 29, null, null)).Message);
        Assert.Equal(4, s.ChangeCount);
        Assert.Equal((byte)1, Records(Reparsed(s), ContainerKind.Inventory).Single(r => r.Slot == bow.Slot).Level);

    }

    // Leftover checks, 2026-10-26: the game rewrote gems written at 80 and 105 to 55, though their data range is 1–105.
    [Fact]
    public void No_item_level_goes_past_55()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        Assert.Equal((1, 105), (Item(GemOfStormsEye).MinLevel, Item(GemOfStormsEye).MaxLevel));
        e.SetItemLevel(Inv(7), 55);                                // Gem of Storm's Eye, level 16
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.SetItemLevel(Inv(7), 56)).Message);
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.Add(Item(GemOfStormsEye), 1, 80, null, null)).Message);
        Assert.Equal((1, 55), InventoryLayout.LevelRange(Item(GemOfStormsEye)));
        Assert.Equal((1, 50), InventoryLayout.LevelRange(Item(TalionsBow)));
        Assert.Equal((5, 5), InventoryLayout.LevelRange(Item(AlchemistHat)));          // fixed: its base only
        Assert.Equal((1, 55), InventoryLayout.LevelRange(null));                       // missing from the game data
    }

    [Fact]
    public void An_item_missing_from_the_game_data_takes_levels_1_to_55()
    {
        var s = Open(); var e = Ed(s);
        e.At(Inv(16))!.ItemId = 0xdeadbeef;                        // the Scorching Wand's record, as an unknown item
        e.SetItemLevel(Inv(16), 55);
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.SetItemLevel(Inv(16), 56)).Message);
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.SetItemLevel(Inv(16), 0)).Message);
        Assert.Equal((byte)55, e.At(Inv(16))!.Level);
    }

    // Session B: the game rewrote level 22 to 21 on the level-21 Bow Of The Serpent.
    [Fact]
    public void Adding_fixed_level_gear_writes_its_base_level()
    {
        var s = Open(); var e = Ed(s);
        var first = e.Add(Item(BowOfTheSerpent21), 1, null, null, null);
        Assert.Equal((byte)21, e.At(first)!.Level);
        e.At(first)!.Level = 22;                                   // a record at the level the editor used to write
        var second = e.Add(Item(BowOfTheSerpent21), 1, null, null, null);
        Assert.Equal((byte)21, e.At(second)!.Level);               // not the same-id record's 22
        var third = e.Add(Item(BowOfTheSerpent21), 1, 21, null, null);   // the base level, given
        Assert.Equal("This item's level is set by the game: base 21 plus upgrades.",
            Rejected(() => e.Add(Item(BowOfTheSerpent21), 1, 22, null, null)).Message);

        var hat = e.Add(Item(AlchemistHat), 1, null, null, null);
        Assert.Equal(4, s.ChangeCount);
        var inv = Records(Reparsed(s), ContainerKind.Inventory);
        Assert.Equal((byte)21, inv.Single(r => r.Slot == third.Slot).Level);
        var newHat = inv.Single(r => r.Slot == hat.Slot);
        Assert.Equal((byte)5, newHat.Level);                       // base 5, not the worn hat's 10 (5 upgrades)
        Assert.Equal(((byte)Rarity.Uncommon, (byte)0), newHat.Rarity);   // and no upgrades
    }

    [Fact]
    public void Add_puts_stack_in_first_free_slot_and_increments_count_byte()
    {
        var s = Open(); var e = Ed(s);
        Assert.Equal(Inv(0), e.Add(Item(CopperOre), 250, null, null, null));
        Assert.Equal(1, s.ChangeCount);
        Assert.Equal(250, e.At(Inv(0))!.Count);

        var bytes = s.Player.ToBytes();
        var re = PlayerCharacter.Parse(bytes, IsItem);
        Assert.Equal(InventoryStatus.Full, re.InventoryStatus);
        var inv = Records(re, ContainerKind.Inventory);
        Assert.Equal(22, inv.Count);
        Assert.Equal((0, CopperOre, 250, 0x00), ((int)inv[0].Slot, inv[0].ItemId, (int)inv[0].Count, (int)inv[0].Flags));

        // Inventory header: after 30 skills, status entries of 27 and 30 bytes, the Equipped header and records.
        var header = PlayerCharacter.LevelOffset + 12 + 5 * 30 + 4 + 27 + 30 + 4
            + Records(re, ContainerKind.Equipped).Sum(r => r.EncodedLength);
        Assert.Equal(0x05, bytes[header + 4]);
        Assert.Equal(22, bytes[header + 5]);                       // n2
        Assert.Equal(0, bytes[header + 7]);                        // n3
    }

    [Fact]
    public void Add_into_ghost_slot_replaces_it()
    {
        var s = Open(); var e = Ed(s);
        Assert.Equal(Inv(15), e.Add(Item(CopperOre), 10, null, null, Inv(15)));   // Bronze Sword ghost
        var inv = Records(Reparsed(s), ContainerKind.Inventory);
        Assert.Equal(21, inv.Count);
        Assert.Equal((15, CopperOre, 10), ((int)inv[0].Slot, inv[0].ItemId, (int)inv[0].Count));
    }

    [Fact]
    public void Add_gear_uses_level_and_rarity()
    {
        var s = Open(); var e = Ed(s);
        var at = e.Add(Item(WolfsSnarlLongbow), 1, 20, Rarity.Epic, null);
        var bow = Records(Reparsed(s), ContainerKind.Inventory).Single(r => r.Slot == at.Slot);
        Assert.Equal(WolfsSnarlLongbow, bow.ItemId);
        Assert.Equal(0x11, bow.Flags & 0x11);                      // bits 0 (level) and 4 (rarity)
        Assert.Equal((byte)20, bow.Level);
        Assert.Equal(((byte)Rarity.Epic, (byte)0), bow.Rarity);
    }

    [Fact]
    public void Add_rejects_bad_counts_levels_rarities_and_targets()
    {
        var s = Open(); var e = Ed(s);
        var before = s.Player.ToBytes();
        ItemInfo bow = Item(WolfsSnarlLongbow), ore = Item(CopperOre);
        Rejected(() => e.Add(ore, 0, null, null, null));
        Rejected(() => e.Add(ore, 251, null, null, null));
        Rejected(() => e.Add(bow, 2, 20, Rarity.Epic, null));        // gear stacks to 1
        Rejected(() => e.Add(bow, 1, 51, null, null));               // levels 1–50
        Rejected(() => e.Add(bow, 1, 0, null, null));
        Rejected(() => e.Add(bow, 1, 20, (Rarity)0, null));
        Rejected(() => e.Add(ore, 1, 13, null, null));               // a material has no level field
        Rejected(() => e.Add(ore, 1, null, Rarity.Rare, null));      // …or rarity field
        Rejected(() => e.Add(ore, 1, null, null, Inv(30)));          // taken
        Rejected(() => e.Add(ore, 1, null, null, Inv(40)));          // past the backpack
        Assert.Equal("There's no backpack extension slot 16.",       // Small Backpack _NEW: Extra 0–15
            Rejected(() => e.Add(ore, 1, null, null, Extra(16))).Message);
        Assert.Equal("New items go into the backpack.", Rejected(() => e.Add(ore, 1, null, null, Eq(11))).Message);
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    // T12-R2: by default the backpack's base rows (Inventory 0–15, then 32–39), then its extension (Extra 0 to
    // capacity − 1); never the action bar (Inventory 16–31).
    [Fact]
    public void Add_fills_the_backpack_then_its_extension_and_then_has_no_room()
    {
        var s = Open(); var e = Ed(s);                             // character2: 16–31 full, 15, 32–34 and 37 ghosts
        var expected = Enumerable.Range(0, 16).Select(i => Inv((byte)i))
            .Concat(Enumerable.Range(32, 8).Select(i => Inv((byte)i)))
            .Concat(Enumerable.Range(0, 16).Select(i => Extra((byte)i))).ToList();
        var actionBar = Enumerable.Range(16, 16).Select(i => e.At(Inv((byte)i))).ToList();
        foreach (var at in expected) Assert.Equal(at, e.Add(Item(CopperOre), 1, null, null, null));
        Assert.Equal(40, s.ChangeCount);

        var before = s.Player.ToBytes();
        Assert.Equal("No room in the backpack", Rejected(() => e.Add(Item(CopperOre), 1, null, null, null)).Message);
        Assert.Equal(40, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
        var re = PlayerCharacter.Parse(before, IsItem);
        Assert.Equal(InventoryStatus.Full, re.InventoryStatus);
        Assert.Equal(Enumerable.Range(0, 40), Records(re, ContainerKind.Inventory).Select(r => (int)r.Slot));
        Assert.DoesNotContain(Records(re, ContainerKind.Inventory), r => r.IsGhost);
        Assert.Equal(Enumerable.Range(0, 16), Records(re, ContainerKind.Extra).Select(r => (int)r.Slot));
        Assert.Equal(actionBar, Enumerable.Range(16, 16).Select(i => e.At(Inv((byte)i))));
    }

    [Fact]
    public void Add_skips_the_action_bar_unless_targeted()
    {
        var s = Open("character1.ksd"); var e = Ed(s);             // base rows full; action bar 30, 31 free; Extra 0 taken
        Assert.Null(e.At(Inv(30)));
        Assert.Equal(Extra(1), e.Add(Item(CopperOre), 5, null, null, null));
        Assert.Null(e.At(Inv(30)));
        Assert.Equal(Inv(30), e.Add(Item(CopperOre), 6, null, null, Inv(30)));
        Assert.Equal(Extra(9), e.Add(Item(CopperOre), 7, null, null, Extra(9)));
        var re = Reparsed(s);
        Assert.Equal(InventoryStatus.Full, re.InventoryStatus);
        Assert.Equal([(1, 5), (9, 7)], Records(re, ContainerKind.Extra).Where(r => r.ItemId == CopperOre).Select(r => ((int)r.Slot, (int)r.Count)));
        Assert.Equal(6, Records(re, ContainerKind.Inventory).Single(r => r.Slot == 30).Count);
    }

    // T12-R2: the extension holds 8 × the rows of the equipped backpack (position 15), by internal name.
    [Fact]
    public void Extra_capacity_follows_the_equipped_backpack()
    {
        Assert.Equal(16, Ed(Open()).ExtraCapacity);                // character2: Small Backpack (Backpack_T1_NEW)
        Assert.Equal(8, Ed(Open("character0.ksd")).ExtraCapacity); // Small Backpack (Backpack_T1)

        var s = Open(); var e = Ed(s);
        var backpack = Records(s.Player, ContainerKind.Equipped)[^1];
        backpack.ItemId = LargeBackpack;
        Assert.Equal(32, e.ExtraCapacity);
        Assert.Equal(Extra(31), e.Add(Item(CopperOre), 1, null, null, Extra(31)));
        backpack.ItemId = CopperOre;                               // not a backpack: no extension
        Assert.Equal(0, e.ExtraCapacity);
        Assert.Equal("There's no backpack extension slot 0.", Rejected(() => e.Add(Item(CopperOre), 1, null, null, Extra(0))).Message);
        Assert.Equal("There's no backpack extension slot 0.", Rejected(() => e.Move(Inv(30), Extra(0))).Message);
    }

    [Fact]
    public void A_list_never_grows_past_255_records()
    {
        var s = Open(); var e = Ed(s);
        var inv = s.Player.Container(ContainerKind.Inventory)!.Records;
        inv.Clear();
        for (var slot = 0; slot < 256; slot++)
            if (slot != 39) inv.Add(new ItemRecord((byte)slot, CopperOre, 1));   // 255 records; slot 39 free
        s.Player.Container(ContainerKind.Extra)!.Records.Add(new ItemRecord(0, CopperOre, 1));

        var before = s.Player.ToBytes();
        Assert.Equal("No room in the backpack", Rejected(() => e.Add(Item(CopperOre), 1, null, null, null)).Message);
        Assert.Equal("No room in the backpack", Rejected(() => e.Move(Extra(0), Inv(39))).Message);
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    [Fact]
    public void Move_swaps_two_inventory_stacks()
    {
        var s = Open(); var e = Ed(s);
        e.Move(Inv(22), Inv(30));                                  // Health Potion ×9 ↔ Fireball I ×87
        Assert.Equal(1, s.ChangeCount);
        var inv = Records(Reparsed(s), ContainerKind.Inventory);
        Assert.Equal(21, inv.Count);
        Assert.Equal((FireballI, 87), inv.Where(r => r.Slot == 22).Select(r => (r.ItemId, (int)r.Count)).Single());
        Assert.Equal((HealthPotion, 9), inv.Where(r => r.Slot == 30).Select(r => (r.ItemId, (int)r.Count)).Single());
    }

    [Fact]
    public void Move_into_an_empty_ghost_or_extra_slot()
    {
        var s = Open(); var e = Ed(s);
        e.Move(Inv(22), Inv(35));                                  // empty
        e.Move(Inv(30), Inv(32));                                  // ghost: replaced
        e.Move(Inv(31), Extra(3));                                 // into the extra slots
        Assert.Equal(3, s.ChangeCount);

        var re = Reparsed(s);
        var inv = Records(re, ContainerKind.Inventory);
        Assert.Equal(19, inv.Count);                               // 21 − the ghost − Ice Bolt
        Assert.DoesNotContain(inv, r => r.Slot is 22 or 30 or 31);
        Assert.Equal(HealthPotion, inv.Single(r => r.Slot == 35).ItemId);
        Assert.Equal((FireballI, 87), inv.Where(r => r.Slot == 32).Select(r => (r.ItemId, (int)r.Count)).Single());
        Assert.Equal((3, IceBolt, 42), Records(re, ContainerKind.Extra).Select(r => ((int)r.Slot, r.ItemId, (int)r.Count)).Single());

        e.Move(Extra(3), Inv(31));                                 // and back
        Assert.Empty(Records(Reparsed(s), ContainerKind.Extra));
    }

    [Fact]
    public void Move_rejects_empty_source_same_slot_and_missing_target()
    {
        var s = Open(); var e = Ed(s);
        var before = s.Player.ToBytes();
        Rejected(() => e.Move(Inv(35), Inv(36)));                  // nothing there
        Rejected(() => e.Move(Inv(15), Inv(36)));                  // a ghost is empty
        Rejected(() => e.Move(Inv(30), Inv(30)));
        Rejected(() => e.Move(Inv(30), Inv(40)));                  // past the backpack
        Rejected(() => e.Move(Inv(30), Extra(16)));                // past the extension (Extra 0–15)
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    [Fact]
    public void Remove_drops_the_record()
    {
        var s = Open(); var e = Ed(s);
        e.Remove(Inv(30));
        Assert.Null(e.At(Inv(30)));
        Rejected(() => e.Remove(Inv(30)));                         // already empty
        Rejected(() => e.Remove(Inv(15)));                         // ghost
        Assert.Equal(1, s.ChangeCount);
        var inv = Records(Reparsed(s), ContainerKind.Inventory);
        Assert.Equal(20, inv.Count);
        Assert.DoesNotContain(inv, r => r.ItemId == FireballI);
    }

    // T12-R3: an item swaps with the Equipped item in the position of its kind; the mask and record count stay.
    [Fact]
    public void Gear_swaps_with_the_equipped_item_in_its_position()
    {
        var s = Open(); var e = Ed(s);                             // character2, mask 0005ffe0
        var helmet = e.Add(Item(AdventurerHelmet), 1, 10, Rarity.Rare, null);
        var ring = e.Add(Item(RingOfHealth), 1, null, null, null);
        var shield = e.Add(Item(HerosShield), 1, null, null, null);
        var hook = e.Add(Item(ImprovedHook), 1, null, null, null);
        var glider = e.Add(Item(AdvancedGlider), 1, null, null, Extra(4));
        Assert.Equal([Inv(0), Inv(1), Inv(2), Inv(3)], new[] { helmet, ring, shield, hook });

        e.Move(helmet, Eq(5));                                     // into Equipped
        e.Move(Eq(11), ring);                                      // from Equipped onto a fitting item: the same swap
        e.Move(Eq(10), Eq(11));                                    // ring ↔ ring
        e.Move(shield, Eq(12));                                    // Hero's Shield for the Spectral Ward …
        e.Move(shield, Eq(12));                                    // … and the ward back (wards go in the shield position)
        e.Move(hook, Eq(13));
        e.Move(glider, Eq(14));                                    // from the extension
        Assert.Equal(12, s.ChangeCount);

        Assert.Equal(AdventurerHelmet, e.At(Eq(5))!.ItemId);
        Assert.Equal(AlchemistHat, e.At(helmet)!.ItemId);
        Assert.Equal((RingOfHealth, 0x3993797au), (e.At(Eq(10))!.ItemId, e.At(Eq(11))!.ItemId));
        Assert.Equal(0x0120840du, e.At(ring)!.ItemId);             // Lesser Ring of Mana
        Assert.Equal((0x53bf5455u, HerosShield), (e.At(Eq(12))!.ItemId, e.At(shield)!.ItemId));
        Assert.Equal((ImprovedHook, 0x55bf8eb8u), (e.At(Eq(13))!.ItemId, e.At(hook)!.ItemId));
        Assert.Equal((AdvancedGlider, 0x3d4a78ddu), (e.At(Eq(14))!.ItemId, e.At(glider)!.ItemId));

        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(_backupRoot), new FakeProbe()));
        var back = Open();
        Assert.Equal(InventoryStatus.Full, back.Player.InventoryStatus);
        Assert.Equal(0x0005ffe0u, back.Player.EquippedMask);
        var eq = Records(back.Player, ContainerKind.Equipped);
        Assert.Equal(Enumerable.Range(0, 11), eq.Select(r => (int)r.Slot));   // slot bytes stay the record index
        Assert.Equal([AdventurerHelmet, RingOfHealth, 0x3993797au, 0x53bf5455u, ImprovedHook, AdvancedGlider, SmallBackpackNew],
            new[] { 0, 5, 6, 7, 8, 9, 10 }.Select(i => eq[i].ItemId));
        Assert.Equal((byte)10, eq[0].Level);
        var b = Ed(back);
        Assert.Equal([AlchemistHat, 0x0120840du, HerosShield, 0x55bf8eb8u], new[] { helmet, ring, shield, hook }.Select(r => b.At(r)!.ItemId));
        Assert.Equal(0x3d4a78ddu, b.At(glider)!.ItemId);
    }

    [Fact]
    public void Appearance_items_swap_by_the_piece_they_dress()
    {
        var s = Open("character0.ksd"); var e = Ed(s);             // positions 0–4: the Exclusive Hollow set
        var hat = e.Add(Item(0xc52f14b3), 1, null, null, null);    // Blacksmith Hat (Vanity_TX_Blacksmith_Helmet)
        Assert.Equal("Blacksmith Hat doesn't go in the Chest appearance slot.", Rejected(() => e.Move(hat, Eq(1))).Message);
        Assert.Equal("Blacksmith Hat doesn't go in the Helmet slot.", Rejected(() => e.Move(hat, Eq(5))).Message);
        e.Move(hat, Eq(0));
        Assert.Equal(0xc52f14b3u, e.At(Eq(0))!.ItemId);
        Assert.Equal(0xba08f81du, e.At(hat)!.ItemId);              // Exclusive Hollow Helmet
        Assert.Equal(InventoryStatus.Full, Reparsed(s).InventoryStatus);
    }

    [Fact]
    public void Swaps_reject_items_that_do_not_fit()
    {
        var s = Open(); var e = Ed(s);
        var before = s.Player.ToBytes();
        Assert.Equal("Scorching Wand doesn't go in the Helmet slot.", Rejected(() => e.Move(Inv(16), Eq(5))).Message);
        Assert.Equal("Scorching Wand doesn't go in the Helmet slot.", Rejected(() => e.Move(Eq(5), Inv(16))).Message);
        Assert.Equal("Alchemist Apprentice Hat doesn't go in the Chest slot.", Rejected(() => e.Move(Eq(5), Eq(6))).Message);
        Rejected(() => e.Move(Eq(10), Eq(12)));                    // ring ↔ ward
        Rejected(() => e.Move(Eq(13), Eq(14)));                    // hook ↔ glider
        Rejected(() => e.Move(Inv(30), Eq(15)));                   // Fireball I is no backpack
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());

        var s1 = Open("character1.ksd"); var e1 = Ed(s1);          // appearance items missing from the game data
        Assert.Equal("This item doesn't go in the Chest appearance slot.", Rejected(() => e1.Move(Eq(0), Eq(1))).Message);
        Assert.Equal(0, s1.ChangeCount);
    }

    // T12-R3: these change the Equipped mask and record count, and when the game changes header byte 2 alongside
    // isn't known (docs/research/in-game-findings.md), so they stay disabled.
    [Fact]
    public void Equipping_into_an_empty_position_and_unequipping_stay_locked()
    {
        var s = Open(); var e = Ed(s);                             // positions 0–4 are empty
        var vanity = e.Add(Item(0xc52f14b3), 1, null, null, null); // Blacksmith Hat: fits position 0
        var before = s.Player.ToBytes();
        foreach (var edit in new Action[]
        {
            () => e.Move(vanity, Eq(0)),                           // into an empty position
            () => e.Move(Eq(5), Inv(35)),                          // out to an empty slot
            () => e.Move(Eq(5), Inv(15)),                          // … or onto a ghost
            () => e.Move(Eq(10), Extra(0)),
            () => e.Remove(Eq(5)),
        })
            Assert.Equal(EquipLocked, Rejected(edit).Message);
        Assert.Equal("New items go into the backpack.", Rejected(() => e.Add(Item(RingOfHealth), 1, null, null, Eq(11))).Message);
        Assert.Equal(1, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    // The "Test Skills" autosave with gear positions 5–12 empty (git-ignored capture; skipped when missing).
    [Fact]
    public void Gear_cannot_go_into_an_emptied_position()
    {
        var path = Path.Combine(_tmp, "capture.ksd");
        File.WriteAllBytes(path, Fixtures.ReadCapture("local/character5/20261025-141219.ksd"));
        var s = SaveSession.Open(path, IsItem); var e = Ed(s);
        Assert.Equal(InventoryStatus.Full, s.Player.InventoryStatus);
        Assert.Null(e.At(Eq(5)));
        var helmet = Records(s.Player, ContainerKind.Inventory).Concat(Records(s.Player, ContainerKind.Extra))
            .Single(r => r.ItemId == AdventurerHelmet);
        var kind = Records(s.Player, ContainerKind.Inventory).Contains(helmet) ? ContainerKind.Inventory : ContainerKind.Extra;
        Assert.Equal(EquipLocked, Rejected(() => e.Move(new ItemRef(kind, helmet.Slot), Eq(5))).Message);
        Assert.Equal(EquipLocked, Rejected(() => e.Move(Eq(13), Extra(10))).Message);
        Assert.Equal(0, s.ChangeCount);
    }

    // The extension shrinks with a smaller backpack, so a swap may not leave items past the new capacity.
    [Fact]
    public void Backpack_swaps_keep_the_extension_in_range()
    {
        var s = Open(); var e = Ed(s);                             // Small Backpack _NEW: 16 extension slots
        var tiny = e.Add(Item(TinyBackpack), 1, null, null, null);
        var ore = e.Add(Item(CopperOre), 3, null, null, Extra(10));
        var before = s.Player.ToBytes();
        const string TooSmall = "Tiny Backpack gives 8 backpack extension slots: move the items out of the others first.";
        Assert.Equal(TooSmall, Rejected(() => e.Move(tiny, Eq(15))).Message);
        Assert.Equal(2, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());

        e.Move(ore, Extra(7));
        e.Move(tiny, Eq(15));
        Assert.Equal(8, e.ExtraCapacity);
        Assert.Equal(SmallBackpackNew, e.At(tiny)!.ItemId);
        Assert.Equal("There's no backpack extension slot 8.", Rejected(() => e.Add(Item(CopperOre), 1, null, null, Extra(8))).Message);

        var s2 = Open(); var e2 = Ed(s2);
        var tinyOut = e2.Add(Item(TinyBackpack), 1, null, null, Extra(12));
        Assert.Equal(TooSmall, Rejected(() => e2.Move(tinyOut, Eq(15))).Message);   // the old one would land in slot 12 of 8
        var large = e2.Add(Item(LargeBackpack), 1, null, null, null);
        e2.Move(large, Eq(15));
        Assert.Equal(32, e2.ExtraCapacity);
        Assert.Equal(Extra(20), e2.Add(Item(CopperOre), 1, null, null, Extra(20)));
        var re = Reparsed(s2);
        Assert.Equal(InventoryStatus.Full, re.InventoryStatus);
        Assert.Equal(LargeBackpack, Records(re, ContainerKind.Equipped)[^1].ItemId);
        Assert.Equal([12, 20], Records(re, ContainerKind.Extra).Select(r => (int)r.Slot));
    }

    [Fact]
    public void InPlaceOnly_allows_count_but_not_add()
    {
        var path = Path.Combine(_tmp, "inplace.ksd");
        File.WriteAllBytes(path, Fixtures.InPlaceOnlyCharacter2());
        var s = SaveSession.Open(path, IsItem); var e = Ed(s);
        Assert.False(s.ReadOnly);
        Assert.Equal(InventoryStatus.InPlaceOnly, s.Player.InventoryStatus);

        e.SetCount(Inv(30), 500);                                  // Fireball I 87 → 500
        e.SetItemLevel(Inv(16), 30);                               // same-size field edit
        e.SetRarity(Eq(5), Rarity.Epic);                           // the Hat: positions come from the header before the run
        Assert.Equal(3, s.ChangeCount);

        var before = s.Player.ToBytes();
        Assert.Equal(NeedsFull, Rejected(() => e.Add(Item(CopperOre), 1, null, null, null)).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Add(Item(CopperOre), 1, null, null, Inv(35))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Move(Inv(22), Inv(35))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Move(Inv(22), Inv(30))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Move(Eq(10), Eq(11))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Remove(Inv(22))).Message);
        Assert.Equal(3, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());

        var inv = Records(PlayerCharacter.Parse(before, IsItem), ContainerKind.Inventory);
        Assert.Equal(500, inv.Single(r => r.Slot == 30).Count);
        Assert.Equal((byte)30, inv.Single(r => r.Slot == 16).Level);
    }

    [Fact]
    public void NotFound_inventory_rejects_every_edit()
    {
        var path = Path.Combine(_tmp, "character2.ksd");
        var file = KsdFile.Parse(File.ReadAllBytes(path));
        var raw = file["player_character"].GetData();
        var statusEnd = PlayerCharacter.LevelOffset + 12 + 5 * 30 + 4 + 27 + 30;
        file["player_character"].SetData(raw.AsSpan(0, statusEnd).ToArray().Concat(new byte[64]).ToArray());
        File.WriteAllBytes(path, file.ToBytes());

        var s = Open(); var e = Ed(s);
        Assert.Equal(InventoryStatus.NotFound, s.Player.InventoryStatus);
        Assert.Null(e.At(Inv(30)));
        const string Unread = "Inventory couldn't be read for this character.";
        Assert.Equal(Unread, Rejected(() => e.SetCount(Inv(30), 5)).Message);
        Assert.Equal(Unread, Rejected(() => e.SetItemLevel(Inv(16), 5)).Message);
        Assert.Equal(Unread, Rejected(() => e.SetRarity(Inv(16), Rarity.Rare)).Message);
        Assert.Equal(Unread, Rejected(() => e.SetDurability(Inv(16), 1, 1)).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Add(Item(CopperOre), 1, null, null, null)).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Move(Inv(22), Inv(35))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Remove(Inv(22))).Message);
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void Read_only_session_rejects_every_edit()
    {
        // Break the container version byte (offset 4): unsupported version → opened read-only.
        var path = Path.Combine(_tmp, "character2.ksd");
        var bytes = File.ReadAllBytes(path);
        bytes[4] ^= 0x7F;
        File.WriteAllBytes(path, bytes);
        var s = Open(); var e = Ed(s);
        Assert.True(s.ReadOnly);
        var expected = "This save is read-only: " + s.ReadOnlyReason;
        foreach (var edit in new Action[]
        {
            () => e.SetCount(Inv(30), 120),
            () => e.SetItemLevel(Inv(16), 30),
            () => e.SetRarity(Inv(16), Rarity.Rare),
            () => e.SetDurability(Inv(16), 363, 363),
            () => e.Remove(Inv(30)),
            () => e.Move(Inv(22), Inv(30)),
            () => e.Add(Item(CopperOre), 1, null, null, null),
        })
            Assert.Equal(expected, Rejected(edit).Message);
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void New_items_without_a_template_get_the_default_fields()
    {
        var pc = Open().Player;                                    // character2, level 7, with every record removed
        foreach (var c in pc.Containers) c.Records.Clear();

        var ring = NewItemFields.Create(Item(RingOfHealth), 3, 1, null, null, pc, _items);
        Assert.Equal((3, RingOfHealth, 1, 0x11), ((int)ring.Slot, ring.ItemId, (int)ring.Count, (int)ring.Flags));
        Assert.Equal((byte)10, ring.Level);                        // the character's level, clamped to 10–10
        Assert.Equal(((byte)Rarity.Uncommon, (byte)0), ring.Rarity);   // base rarity

        var bow = NewItemFields.Create(Item(WolfsSnarlLongbow), 4, 1, 20, Rarity.Epic, pc, _items);
        Assert.Equal(0x33, bow.Flags);
        Assert.Equal((byte)20, bow.Level);
        Assert.Equal((100u, 100u), bow.Durability);                // no weapon record to copy from
        Assert.Equal(((byte)Rarity.Epic, (byte)0), bow.Rarity);
        Assert.Equal((0u, (byte)0), bow.Gem);                      // an empty socket

        var ore = NewItemFields.Create(Item(CopperOre), 5, 250, null, null, pc, _items);
        Assert.Equal((5, CopperOre, 250, 0x00), ((int)ore.Slot, ore.ItemId, (int)ore.Count, (int)ore.Flags));
    }

    [Fact]
    public void New_items_copy_the_fields_of_a_matching_record()
    {
        var pc = Open().Player;                                    // character2, level 7
        var inv = Records(pc, ContainerKind.Inventory);

        // No bow here, so weapon defaults, with durability from the first weapon record (Scorching Wand, max 363).
        var bow = NewItemFields.Create(Item(WolfsSnarlLongbow), 0, 1, null, null, pc, _items);
        Assert.Equal(0x33, bow.Flags);
        Assert.Equal((byte)7, bow.Level);
        Assert.Equal((363u, 363u), bow.Durability);
        Assert.Equal(((byte)Rarity.Common, (byte)0), bow.Rarity);

        // Same category, slot and weapon type: the Torch (flags 03, 55/55).
        var wax = NewItemFields.Create(Item(WaxTorch), 0, 1, null, null, pc, _items);
        Assert.Equal(0x03, wax.Flags);
        Assert.Equal((byte)3, wax.Level);
        Assert.Equal((55u, 55u), wax.Durability);

        // Same id: the Water Pouch's field set, bit-2 bytes included.
        var pouch = NewItemFields.Create(Item(WaterPouch), 0, 1, null, null, pc, _items);
        Assert.Equal(0x04, pouch.Flags);
        Assert.Equal(inv.Single(r => r.ItemId == WaterPouch).Bit2, pouch.Bit2);

        // character1's first Water Pouch has a level field (flags 05, level 0). The default never changes the field
        // set; the pouch's levels are 0–0, so it gets 0. An explicit level still sets it.
        var pc1 = Open("character1.ksd").Player;
        var template1 = Records(pc1, ContainerKind.Inventory).First(r => r.ItemId == WaterPouch);
        Assert.Equal(0x05, template1.Flags);
        var pouch1 = NewItemFields.Create(Item(WaterPouch), 0, 1, null, null, pc1, _items);
        Assert.Equal(0x05, pouch1.Flags);
        Assert.Equal((byte)0, pouch1.Level);
        Assert.Equal(template1.Bit2, pouch1.Bit2);
        Assert.Equal((byte)3, NewItemFields.Create(Item(WaterPouch), 0, 1, 3, null, pc1, _items).Level);

        // With no level given, a ranged item gets the character's level clamped to its range, not a same-id record's
        // own level: the Scorching Wand (1–50) gets 7, not the record's 11 (which may include upgrades).
        Assert.Equal((byte)7, NewItemFields.Create(Item(ScorchingWand), 0, 1, null, null, pc, _items).Level);

        // Same id: full durability and an empty socket, whatever the template holds.
        inv.Single(r => r.ItemId == ScorchingWand).Gem = (0x12345678, 1);
        var wand = NewItemFields.Create(Item(ScorchingWand), 0, 1, 12, Rarity.Rare, pc, _items);
        Assert.Equal(0x33, wand.Flags);
        Assert.Equal((byte)12, wand.Level);
        Assert.Equal((363u, 363u), wand.Durability);
        Assert.Equal(((byte)Rarity.Rare, (byte)0), wand.Rarity);
        Assert.Equal((0u, (byte)0), wand.Gem);
    }

    // The default level never changes the field set; a 0–0 item gets its base level, 0.
    [Fact]
    public void Gear_with_a_0_to_0_level_range_keeps_its_level_field()
    {
        var s = Open(); var e = Ed(s);
        Assert.Equal((0, 0), (Item(ThreadbarePants).MinLevel, Item(ThreadbarePants).MaxLevel));

        // Copied from the Alchemist Apprentice Trousers (same category and slot, flags 11).
        var pants = NewItemFields.Create(Item(ThreadbarePants), 0, 1, null, null, s.Player, _items);
        Assert.Equal(0x11, pants.Flags);
        Assert.Equal((byte)0, pants.Level);

        // Through Add, and read back.
        var at = e.Add(Item(ThreadbarePants), 1, null, null, null);
        var added = Records(Reparsed(s), ContainerKind.Inventory).Single(r => r.Slot == at.Slot);
        Assert.Equal(0x01, added.Flags & 0x01);
        Assert.Equal((byte)0, added.Level);

        // The armour default (no record to copy from) keeps its level field too.
        var bare = Open().Player;
        foreach (var c in bare.Containers) c.Records.Clear();
        var fresh = NewItemFields.Create(Item(ThreadbarePants), 0, 1, null, null, bare, _items);
        Assert.Equal(0x11, fresh.Flags);
        Assert.Equal((byte)0, fresh.Level);
    }

    [Fact]
    public void Edits_survive_save_and_reload()
    {
        var s = Open(); var e = Ed(s);
        e.SetCount(Inv(30), 120);
        var ore = e.Add(Item(CopperOre), 250, null, null, null);
        var bow = e.Add(Item(WolfsSnarlLongbow), 1, 20, Rarity.Epic, Inv(36));
        e.Move(Inv(22), Inv(38));                                  // Health Potion to an empty slot
        e.Remove(Inv(23));                                         // Bandage
        e.SetDurability(Inv(16), 363, 363);
        Assert.Equal(6, s.ChangeCount);
        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(_backupRoot), new FakeProbe()));

        var back = Open(); var b = Ed(back);
        Assert.Equal(InventoryStatus.Full, back.Player.InventoryStatus);
        Assert.Equal(22, Records(back.Player, ContainerKind.Inventory).Count);   // 21 + ore + bow − bandage
        Assert.Equal(120, b.At(Inv(30))!.Count);
        Assert.Equal((CopperOre, 250), (b.At(ore)!.ItemId, (int)b.At(ore)!.Count));
        var bw = b.At(bow)!;
        Assert.Equal((WolfsSnarlLongbow, (byte?)20, (byte)Rarity.Epic), (bw.ItemId, bw.Level, bw.Rarity!.Value.Rarity));
        Assert.Null(b.At(Inv(22)));
        Assert.Equal((HealthPotion, 9), (b.At(Inv(38))!.ItemId, (int)b.At(Inv(38))!.Count));
        Assert.Null(b.At(Inv(23)));
        Assert.Equal((363u, 363u), b.At(Inv(16))!.Durability);
    }
}
