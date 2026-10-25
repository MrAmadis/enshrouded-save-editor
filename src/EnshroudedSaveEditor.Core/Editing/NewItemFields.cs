using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>
/// The optional fields of a newly added item record. These rules are provisional (my calls, checked in-game
/// by Task 11's "Test Items" copy).
/// </summary>
public static class NewItemFields
{
    const byte LevelBit = 0x01, DurabilityBit = 0x02, RarityBit = 0x10, GemBit = 0x20;

    /// <summary>
    /// Builds the record. The field set (flags, plus the bit 2 and bit 3 bytes) is copied from a record in
    /// <paramref name="pc"/>:
    /// <list type="number">
    /// <item>one of the same item id; else</item>
    /// <item>for an unstackable item, one of the same category, equipment slot and weapon type (needs
    /// <paramref name="items"/>; the weapon type keeps throwables and torches apart from other weapons).</item>
    /// </list>
    /// Ghost records have no fields, so they are never copied. With no such record, stackable items get no fields
    /// (no stackable record in any save has any); otherwise armour and rings get level and rarity (<c>0x11</c>),
    /// shields level, durability and rarity (<c>0x13</c>, as every shield seen has durability), weapons level,
    /// durability, rarity and a gem socket (<c>0x33</c>), and everything else no fields.
    /// <para>
    /// Where the field set has them: level is <paramref name="level"/>, or else the character's level clamped to
    /// the item's reference range, or no level at all when that range is 0–0 (T10-R1); rarity is <paramref name="rarity"/>, or else the base rarity, with the extra byte 0;
    /// durability is full, at the copied record's maximum, or for the defaults that of the first weapon record in
    /// <paramref name="pc"/> (needs <paramref name="items"/>), or else 100; the gem socket is empty. A level or
    /// rarity the field set has no room for is not added: the caller checks the returned record.
    /// </para>
    /// </summary>
    public static ItemRecord Create(ItemInfo info, byte slot, int count, int? level, Rarity? rarity,
        PlayerCharacter pc, IItemCatalog? items = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, ushort.MaxValue);
        if (level is < 0 or > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(level));

        var records = pc.Containers.SelectMany(c => c.Records).Where(r => !r.IsGhost).ToList();
        var template = records.FirstOrDefault(r => r.ItemId == info.Id)
            ?? (info.MaxStack == 1 && items is not null
                ? records.FirstOrDefault(r => items.Find(r.ItemId) is { MaxStack: 1 } other
                    && other.Category == info.Category && other.Slot == info.Slot && other.WeaponType == info.WeaponType)
                : null);

        var rec = new ItemRecord(slot, info.Id, (ushort)count);
        byte flags;
        uint durability;
        if (template is not null)
        {
            flags = template.Flags;
            rec.Bit2 = template.Bit2;
            rec.Transmog = template.Transmog;
            durability = template.Durability?.Max ?? 100;
        }
        else
        {
            flags = DefaultFlags(info);
            durability = records.FirstOrDefault(r => r.Durability is not null
                && items?.Find(r.ItemId)?.Category == ItemCategory.Weapons)?.Durability?.Max ?? 100;
        }

        if ((flags & LevelBit) != 0) rec.Level = level is { } l ? (byte)l : DefaultLevel(info, pc.Level);
        if ((flags & DurabilityBit) != 0) rec.Durability = (durability, durability);
        if ((flags & RarityBit) != 0) rec.Rarity = ((byte)(rarity ?? info.BaseRarity), 0);
        if ((flags & GemBit) != 0) rec.Gem = (0, 0);
        return rec;
    }

    static byte DefaultFlags(ItemInfo info) => info switch
    {
        { MaxStack: > 1 } => 0x00,
        { Category: ItemCategory.Weapons } => 0x33,
        { Category: ItemCategory.Equipment, Slot: EquipSlot.Shield } => 0x13,
        { Category: ItemCategory.Equipment, Slot: EquipSlot.Helmet or EquipSlot.Chest or EquipSlot.Arms
            or EquipSlot.Legs or EquipSlot.Boots or EquipSlot.Ring } => 0x11,
        _ => 0x00,
    };

    /// <summary>
    /// T10-R1: the character's level clamped to the item's reference range, or no level (no field) when the item
    /// has no level range (max level 0). The range is used only here, not to bound edits.
    /// </summary>
    static byte? DefaultLevel(ItemInfo info, uint characterLevel)
    {
        if (info.MaxLevel <= 0) return null;
        var max = Math.Min(info.MaxLevel, byte.MaxValue);
        var min = Math.Clamp(info.MinLevel, 0, max);
        return (byte)Math.Clamp(characterLevel, (uint)min, (uint)max);
    }
}
