using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

/// <summary>The slot map confirmed in session A (docs/research/in-game-findings.md).</summary>
public class InventoryLayoutTests
{
    readonly IItemCatalog _items = ReferenceGameData.Load().Items;
    ItemInfo Item(uint id) => _items.Find(id)!;

    static IEnumerable<int> Ints(IEnumerable<byte> b) => b.Select(x => (int)x);

    [Fact]
    public void Backpack_and_action_bar_slots()
    {
        Assert.Equal(40, InventoryLayout.InventorySlots);
        Assert.Equal(8, InventoryLayout.Columns);
        Assert.Equal(Enumerable.Range(0, 16).Concat(Enumerable.Range(32, 8)), Ints(InventoryLayout.BackpackSlots));
        Assert.Equal(2, InventoryLayout.HotbarRows.Count);
        Assert.Equal(Enumerable.Range(16, 8), Ints(InventoryLayout.HotbarRows[0]));
        Assert.Equal(Enumerable.Range(24, 8), Ints(InventoryLayout.HotbarRows[1]));
        Assert.Equal(Enumerable.Range(16, 16), Ints(InventoryLayout.HotbarSlots));
        Assert.Empty(InventoryLayout.BackpackSlots.Intersect(InventoryLayout.HotbarSlots));
        Assert.Equal(Enumerable.Range(0, 40), Ints(InventoryLayout.BackpackSlots.Concat(InventoryLayout.HotbarSlots).Order()));
    }

    [Fact]
    public void Backpack_grid_is_the_base_rows_then_the_extension_rows()
    {
        var grid = InventoryLayout.BackpackGrid(16);
        Assert.Equal(40, grid.Count);
        Assert.Equal(InventoryLayout.BackpackSlots.Select(s => new ItemRef(ContainerKind.Inventory, s)), grid.Take(24));
        Assert.Equal(Enumerable.Range(0, 16).Select(s => new ItemRef(ContainerKind.Extra, (byte)s)), grid.Skip(24));
        Assert.Equal(24, InventoryLayout.BackpackGrid(0).Count);
    }

    // T12-R2: 8 × rows of the equipped backpack, by internal name; "Backpack Extension 16" in-game for Backpack_T1_NEW.
    [Fact]
    public void Extra_capacity_follows_the_backpack()
    {
        Assert.Equal(new Dictionary<string, int>
        {
            ["Backpack_T0"] = 1, ["Backpack_T1"] = 1, ["Backpack_T1_NEW"] = 2, ["Backpack_T2"] = 2,
            ["Backpack_T2_NEW"] = 3, ["Backpack_T3"] = 3, ["Backpack_T3_NEW"] = 4,
        }, InventoryLayout.BackpackRows);
        Assert.Equal(8, InventoryLayout.ExtraCapacity(Item(0x1fa74ed7)));     // Tiny Backpack
        Assert.Equal(8, InventoryLayout.ExtraCapacity(Item(0xfc3d5c4c)));     // Small Backpack (Backpack_T1)
        Assert.Equal(16, InventoryLayout.ExtraCapacity(Item(0xde38058e)));    // Small Backpack (Backpack_T1_NEW)
        Assert.Equal(32, InventoryLayout.ExtraCapacity(Item(0x3b624853)));    // Large Backpack (Backpack_T3_NEW)
        Assert.Equal(0, InventoryLayout.ExtraCapacity(null));                  // no backpack equipped
        Assert.Equal(0, InventoryLayout.ExtraCapacity(Item(0x2b9d5b6f)));     // Copper Ore
        // Every backpack in the reference data is in the table.
        Assert.All(_items.All.Where(i => i.InternalName.StartsWith("Backpack_")), i => Assert.True(InventoryLayout.ExtraCapacity(i) > 0, i.InternalName));
    }

    [Fact]
    public void Equip_positions_are_the_confirmed_map()
    {
        Assert.True(InventoryLayout.EquipMapKnown);
        var p = InventoryLayout.EquipPositions;
        Assert.Equal(Enumerable.Range(0, 16), p.Select(x => (int)x.Index));
        Assert.Equal(
        [
            EquipKind.HelmetAppearance, EquipKind.ChestAppearance, EquipKind.ArmsAppearance, EquipKind.LegsAppearance,
            EquipKind.BootsAppearance, EquipKind.Helmet, EquipKind.Chest, EquipKind.Arms, EquipKind.Legs, EquipKind.Boots,
            EquipKind.Ring, EquipKind.Ring, EquipKind.Shield, EquipKind.GrapplingHook, EquipKind.Glider, EquipKind.Backpack,
        ], p.Select(x => x.Kind));
        Assert.All(p.Take(5), x => Assert.True(x.IsAppearance));
        Assert.All(p.Skip(5), x => Assert.False(x.IsAppearance));
        Assert.Equal(["Helmet appearance", "Helmet", "Ring", "Ring", "Shield", "Grappling hook", "Glider", "Backpack"],
            new[] { 0, 5, 10, 11, 12, 13, 14, 15 }.Select(i => p[i].Label));
        Assert.Equal([5], InventoryLayout.EquippedSlotsFor[EquipSlot.Helmet]);
        Assert.Equal([6], InventoryLayout.EquippedSlotsFor[EquipSlot.Chest]);
        Assert.Equal([7], InventoryLayout.EquippedSlotsFor[EquipSlot.Arms]);
        Assert.Equal([8], InventoryLayout.EquippedSlotsFor[EquipSlot.Legs]);
        Assert.Equal([9], InventoryLayout.EquippedSlotsFor[EquipSlot.Boots]);
        Assert.Equal([10, 11], InventoryLayout.EquippedSlotsFor[EquipSlot.Ring]);
        Assert.Equal([12], InventoryLayout.EquippedSlotsFor[EquipSlot.Shield]);
        Assert.False(InventoryLayout.EquippedSlotsFor.ContainsKey(EquipSlot.Weapon));
    }

    [Theory]
    [InlineData(0xff1b25e5u, new[] { 5 })]          // Adventurer Helmet
    [InlineData(0xb8da18f5u, new[] { 6 })]          // Adventurer Chest
    [InlineData(0x420f94adu, new[] { 7 })]          // Adventurer Gloves (arms)
    [InlineData(0xc4003fc4u, new[] { 8 })]          // Adventurer Trousers
    [InlineData(0x2a5ea1cau, new[] { 9 })]          // Adventurer Boots
    [InlineData(0xf29b7aa9u, new[] { 10, 11 })]     // Ring of Health
    [InlineData(0x0262764au, new[] { 12 })]         // Hero's Shield
    [InlineData(0x53bf5455u, new[] { 12 })]         // Spectral Ward (Ward_T1_Loot, no slot in the reference data)
    [InlineData(0x6aa6a1d4u, new[] { 13 })]         // Improved Grappling Hook
    [InlineData(0x534ba901u, new[] { 14 })]         // Advanced Glider
    [InlineData(0xde38058eu, new[] { 15 })]         // Small Backpack (_NEW)
    [InlineData(0xba08f81du, new[] { 0 })]          // Exclusive Hollow Helmet (Vanity_…_Helmet)
    [InlineData(0x4ba1d245u, new[] { 1 })]          // … Chestplate (_Chest)
    [InlineData(0x20496878u, new[] { 2 })]          // … Gloves
    [InlineData(0x5cb01a35u, new[] { 3 })]          // … Pants (_Legs)
    [InlineData(0x26324e2cu, new[] { 4 })]          // … Boots
    [InlineData(0xe589d7a4u, new int[0])]           // Scorching Wand: weapons live in the action bar
    [InlineData(0x2b9d5b6fu, new int[0])]           // Copper Ore
    [InlineData(0xb13154aeu, new int[0])]           // Riptide (Craft_Shield_…, no slot): not evidenced, so no position
    public void Items_fit_only_their_positions(uint id, int[] positions)
    {
        var info = Item(id);
        Assert.Equal(positions, Enumerable.Range(0, 16).Where(p => InventoryLayout.Fits(info, p)));
        Assert.False(InventoryLayout.Fits(info, -1));
        Assert.False(InventoryLayout.Fits(info, 16));
    }

    static IEnumerable<PlayerCharacter> GameWritten(string name) => name.Contains('/')
        ? [PlayerCharacter.Parse(KsdFile.Parse(Fixtures.ReadCapture(name))["player_character"].GetData(), Fixtures.ItemIds().Contains)]
        : [PlayerCharacter.Parse(KsdFile.Parse(name.Contains(".backup-")
            ? File.ReadAllBytes(Path.Combine(Fixtures.RealBackup, name)) : Fixtures.Read(name))["player_character"].GetData(), Fixtures.ItemIds().Contains)];

    public static TheoryData<string> SavesWithEquippedRecords()
    {
        var data = new TheoryData<string>("character0.ksd", "character1.ksd", "character2.ksd");
        foreach (var i in Enumerable.Range(0, 10)) data.Add($"character2.ksd.backup-{i}");
        foreach (var c in Fixtures.ExperimentCaptures()) data.Add(c.Data);
        return data;
    }

    // The map checked against every save: each Equipped record whose id is in the reference data fits its position.
    [Theory, MemberData(nameof(SavesWithEquippedRecords))]
    public void Every_equipped_item_fits_its_position(string save)
    {
        foreach (var pc in GameWritten(save))
        {
            var eq = pc.Container(ContainerKind.Equipped)!.Records;
            for (var i = 0; i < eq.Count; i++)
                if (_items.Find(eq[i].ItemId) is { } info)
                    Assert.True(InventoryLayout.Fits(info, pc.EquippedPositions![i]), $"{info.Name} at position {pc.EquippedPositions![i]}");
        }
    }
}
