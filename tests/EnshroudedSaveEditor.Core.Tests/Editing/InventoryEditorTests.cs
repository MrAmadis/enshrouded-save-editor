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
    const uint ScorchingWand = 0xe589d7a4;      // character2 Inventory 16, flags 33, durability 352/363

    const string NeedsFull = "Adding and moving items isn't available for this character yet";
    const string EquipLocked = "Equipping and unequipping items isn't available yet";

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
        Assert.Equal(0xe5a9ac59u, e.At(Eq(0))!.ItemId);            // Alchemist Apprentice Hat
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
        Rejected(() => e.SetItemLevel(wand, 0));                   // levels 1–55
        Rejected(() => e.SetItemLevel(wand, 56));
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
        e.SetItemLevel(Eq(0), 5);                                  // Hat: level 10 → 5 (reference levels 5–5)
        e.SetRarity(Eq(0), Rarity.Epic);
        Assert.Equal(2, s.ChangeCount);
        var eq = Records(Reparsed(s), ContainerKind.Equipped);
        Assert.Equal(11, eq.Count);
        Assert.Equal((byte)5, eq[0].Level);
        Assert.Equal(((byte)Rarity.Epic, (byte)5), eq[0].Rarity);
    }

    // T10-R1: the reference level range doesn't bound real records (this chest is level 10 against 5–5).
    [Fact]
    public void Item_levels_run_from_1_to_55_whatever_the_reference_range()
    {
        var s = Open(); var e = Ed(s);
        var chest = Eq(1);                                         // Alchemist Apprentice Chest: level 10, reference 5–5
        Assert.Equal((byte)10, e.At(chest)!.Level);
        e.SetItemLevel(chest, 12);
        Assert.Equal((byte)12, e.At(chest)!.Level);
        e.SetItemLevel(chest, 55);
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.SetItemLevel(chest, 0)).Message);
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.SetItemLevel(chest, 56)).Message);

        var helmet = e.Add(Item(AdventurerHelmet), 1, 55, Rarity.Rare, null);     // reference 10–10
        Assert.Equal("The level must be between 1 and 55.", Rejected(() => e.Add(Item(AdventurerHelmet), 1, 56, null, null)).Message);
        Rejected(() => e.Add(Item(AdventurerHelmet), 1, 0, null, null));
        Assert.Equal(3, s.ChangeCount);

        var re = Reparsed(s);
        Assert.Equal((byte)55, Records(re, ContainerKind.Equipped)[1].Level);
        Assert.Equal((byte)55, Records(re, ContainerKind.Inventory).Single(r => r.Slot == helmet.Slot).Level);
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
        Rejected(() => e.Add(bow, 1, 56, null, null));               // levels 1–55
        Rejected(() => e.Add(bow, 1, 0, null, null));
        Rejected(() => e.Add(bow, 1, 20, (Rarity)0, null));
        Rejected(() => e.Add(ore, 1, 13, null, null));               // a material has no level field
        Rejected(() => e.Add(ore, 1, null, Rarity.Rare, null));      // …or rarity field
        Rejected(() => e.Add(ore, 1, null, null, Inv(30)));          // taken
        Rejected(() => e.Add(ore, 1, null, null, Inv(40)));          // past the backpack
        Rejected(() => e.Add(ore, 1, null, null, Extra(0)));         // only Inventory slots
        Rejected(() => e.Add(ore, 1, null, null, Eq(11)));
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
    }

    [Fact]
    public void Add_rejects_when_backpack_full()
    {
        var s = Open(); var e = Ed(s);
        var free = Enumerable.Range(0, InventoryLayout.InventorySlots).Count(i => e.At(Inv((byte)i)) is null);
        Assert.Equal(24, free);                                    // 40 − 16 real stacks (ghosts count as free)
        for (var i = 0; i < free; i++) e.Add(Item(CopperOre), 1, null, null, null);
        Assert.Equal(free, s.ChangeCount);

        var before = s.Player.ToBytes();
        Assert.Equal("No room in the backpack", Rejected(() => e.Add(Item(CopperOre), 1, null, null, null)).Message);
        Assert.Equal(free, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
        var inv = Records(PlayerCharacter.Parse(before, IsItem), ContainerKind.Inventory);
        Assert.Equal(Enumerable.Range(0, 40), inv.Select(r => (int)r.Slot));
        Assert.DoesNotContain(inv, r => r.IsGhost);
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
        Rejected(() => e.Move(Inv(30), Extra(8)));
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

    [Fact]
    public void Equip_is_refused_until_the_slot_map_is_known()
    {
        var s = Open(); var e = Ed(s);
        var before = s.Player.ToBytes();
        foreach (var edit in new Action[]
        {
            () => e.Move(Inv(16), Eq(0)),                          // wand onto the (occupied) hat slot
            () => e.Move(Eq(5), Inv(35)),                          // ring out to an empty slot
            () => e.Move(Eq(5), Eq(6)),                            // ring ↔ ring
            () => e.Move(Eq(0), Eq(11)),                           // renumber to an unused Equipped slot
            () => e.Remove(Eq(0)),
        })
            Assert.Equal(EquipLocked, Rejected(edit).Message);
        Rejected(() => e.Add(Item(RingOfHealth), 1, null, null, Eq(11)));
        Assert.Empty(InventoryLayout.EquippedSlotsFor);
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, s.Player.ToBytes());
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
        Assert.Equal(2, s.ChangeCount);

        var before = s.Player.ToBytes();
        Assert.Equal(NeedsFull, Rejected(() => e.Add(Item(CopperOre), 1, null, null, null)).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Add(Item(CopperOre), 1, null, null, Inv(35))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Move(Inv(22), Inv(35))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Move(Inv(22), Inv(30))).Message);
        Assert.Equal(NeedsFull, Rejected(() => e.Remove(Inv(22))).Message);
        Assert.Equal(2, s.ChangeCount);
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

        // T10-R1: character1's first Water Pouch has a level field (flags 05, level 0), but the item has no level
        // range (0–0), so a new one gets no level by default; an explicit level still sets it.
        var pc1 = Open("character1.ksd").Player;
        var template1 = Records(pc1, ContainerKind.Inventory).First(r => r.ItemId == WaterPouch);
        Assert.Equal(0x05, template1.Flags);
        var pouch1 = NewItemFields.Create(Item(WaterPouch), 0, 1, null, null, pc1, _items);
        Assert.Equal(0x04, pouch1.Flags);
        Assert.Equal(template1.Bit2, pouch1.Bit2);
        Assert.Equal((byte)3, NewItemFields.Create(Item(WaterPouch), 0, 1, 3, null, pc1, _items).Level);

        // Same id: full durability and an empty socket, whatever the template holds.
        inv.Single(r => r.ItemId == ScorchingWand).Gem = (0x12345678, 1);
        var wand = NewItemFields.Create(Item(ScorchingWand), 0, 1, 12, Rarity.Rare, pc, _items);
        Assert.Equal(0x33, wand.Flags);
        Assert.Equal((byte)12, wand.Level);
        Assert.Equal((363u, 363u), wand.Durability);
        Assert.Equal(((byte)Rarity.Rare, (byte)0), wand.Rarity);
        Assert.Equal((0u, (byte)0), wand.Gem);
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
