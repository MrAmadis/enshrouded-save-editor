namespace EnshroudedSaveEditor.Core.Model;

/// <summary>The item categories, in the order of the reference data's <c>cats</c> list.</summary>
public enum ItemCategory
{
    Ammunition, AnimalFood, Blueprints, BuildTools, Collectibles, ColorPalette, Consumables, Currency,
    Customization, Equipment, FishingRod, Instrument, Materials, Tools, WeaponGem, Weapons,
}

/// <summary>Item rarity, with the values the save file uses.</summary>
public enum Rarity : byte { Common = 1, Uncommon, Rare, Epic, Legendary }

/// <summary>Equipment slots, in the order of the reference data's <c>slots</c> list.</summary>
public enum EquipSlot { None, Arms, Boots, Chest, Helmet, Legs, Ring, Shield, Weapon }

public sealed record ItemInfo(
    uint Id, string Name, ItemCategory Category, Rarity BaseRarity, int MaxStack, int MinLevel, int MaxLevel,
    EquipSlot Slot, string? WeaponType, string InternalName, string Description, int Perks, bool NoRarity);

public interface IItemCatalog
{
    ItemInfo? Find(uint id);
    IReadOnlyList<ItemInfo> All { get; }
}
