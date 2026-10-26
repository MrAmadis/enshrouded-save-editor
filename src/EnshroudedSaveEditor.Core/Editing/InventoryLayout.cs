using System.Collections.ObjectModel;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>What an Equipped position holds, in position order (<see cref="Ring"/> covers positions 10 and 11).</summary>
public enum EquipKind
{
    HelmetAppearance, ChestAppearance, ArmsAppearance, LegsAppearance, BootsAppearance,
    Helmet, Chest, Arms, Legs, Boots, Ring, Shield, GrapplingHook, Glider, Backpack,
}

/// <summary>
/// One of the 16 Equipped positions: bit <see cref="Index"/> of the Equipped mask
/// (<see cref="PlayerCharacter.EquippedMask"/>). <see cref="Slot"/> is the armour slot it is for (the piece an
/// appearance position dresses), or <see cref="EquipSlot.None"/> for the grappling hook, glider and backpack.
/// </summary>
public sealed record EquipPosition(byte Index, EquipKind Kind, EquipSlot Slot, string Label)
{
    public bool IsAppearance => Kind <= EquipKind.BootsAppearance;
}

/// <summary>
/// The slot map, capacities and Equipped positions. Each value says whether it was confirmed in-game
/// (docs/research/in-game-findings.md, session A) or where it comes from.
/// </summary>
public static class InventoryLayout
{
    /// <summary>Inventory slots 0 to 39: the backpack's base rows and the action bar.</summary>
    public const int InventorySlots = 40;       // confirmed in-game: yes (session A, "Test Slots": all 40 shown)

    /// <summary>Backpack grid width; also the slots per backpack-extension row.</summary>
    public const int Columns = 8;               // confirmed in-game: yes (session A)

    /// <summary>
    /// The Inventory slots of the base backpack, in grid order: row 1 is 0–7, row 2 is 8–15, row 3 is 32–39.
    /// </summary>
    public static IReadOnlyList<byte> BackpackSlots { get; } =   // confirmed in-game: yes (session A)
        Array.AsReadOnly([.. Range(0, 16), .. Range(32, 8)]);

    /// <summary>The action bar: row 1 is Inventory 16–23 (keys 1–8), row 2 is 24–31.</summary>
    public static IReadOnlyList<IReadOnlyList<byte>> HotbarRows { get; } =   // confirmed in-game: yes (session A)
        Array.AsReadOnly<IReadOnlyList<byte>>([Array.AsReadOnly(Range(16, 8)), Array.AsReadOnly(Range(24, 8))]);

    /// <summary>All action bar slots, Inventory 16–31.</summary>
    public static IReadOnlyList<byte> HotbarSlots { get; } = Array.AsReadOnly(Range(16, 16));   // confirmed in-game: yes (session A)

    /// <summary>
    /// Backpack-extension rows by the equipped backpack's internal name (T12-R2). The Extra container holds
    /// <see cref="Columns"/> × rows slots, from 0. Confirmed in-game for <c>Backpack_T1_NEW</c> (2 rows: "Backpack
    /// Extension 16", session A); the others follow the item descriptions ("enlarges your inventory by one row …").
    /// </summary>
    public static IReadOnlyDictionary<string, int> BackpackRows { get; } = new ReadOnlyDictionary<string, int>(
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Backpack_T0"] = 1, ["Backpack_T1"] = 1, ["Backpack_T1_NEW"] = 2, ["Backpack_T2"] = 2,
            ["Backpack_T2_NEW"] = 3, ["Backpack_T3"] = 3, ["Backpack_T3_NEW"] = 4,
        });

    /// <summary>The Extra slots the equipped backpack gives: 0 with no backpack or one that isn't in <see cref="BackpackRows"/>.</summary>
    public static int ExtraCapacity(ItemInfo? backpack) =>
        backpack is { Category: ItemCategory.Equipment } && BackpackRows.TryGetValue(backpack.InternalName, out var rows)
            ? Columns * rows
            : 0;

    /// <summary>
    /// The backpack grid in display order, <see cref="Columns"/> per row: the base rows (<see cref="BackpackSlots"/>),
    /// then the extension rows, Extra 0 to <paramref name="extraCapacity"/> − 1. Add's default placement follows it.
    /// </summary>
    public static IReadOnlyList<ItemRef> BackpackGrid(int extraCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(extraCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(extraCapacity, byte.MaxValue + 1);
        return Array.AsReadOnly(
        [
            .. BackpackSlots.Select(s => new ItemRef(ContainerKind.Inventory, s)),
            .. Enumerable.Range(0, extraCapacity).Select(s => new ItemRef(ContainerKind.Extra, (byte)s)),
        ]);
    }

    /// <summary>
    /// The highest level an edit may set on an item missing from the game data (the lowest is 1). A known item takes
    /// levels within its own range, and a fixed-level one only its base level (session B).
    /// </summary>
    public const int MaxItemLevel = 55;         // confirmed in-game: no — 1.0 patch notes: equipment max level 55

    /// <summary>
    /// The Equipped positions: bits 0–15 of the Equipped mask. A record sits at the position of its mask bit, and
    /// records are stored in bit order with slot bytes 0…n−1 (docs/research/player-character.md).
    /// </summary>
    public static IReadOnlyList<EquipPosition> EquipPositions { get; } = Array.AsReadOnly(   // confirmed in-game: yes (session A)
    [
        new EquipPosition(0, EquipKind.HelmetAppearance, EquipSlot.Helmet, "Helmet appearance"),
        new EquipPosition(1, EquipKind.ChestAppearance, EquipSlot.Chest, "Chest appearance"),
        new EquipPosition(2, EquipKind.ArmsAppearance, EquipSlot.Arms, "Arms appearance"),
        new EquipPosition(3, EquipKind.LegsAppearance, EquipSlot.Legs, "Legs appearance"),
        new EquipPosition(4, EquipKind.BootsAppearance, EquipSlot.Boots, "Boots appearance"),
        new EquipPosition(5, EquipKind.Helmet, EquipSlot.Helmet, "Helmet"),
        new EquipPosition(6, EquipKind.Chest, EquipSlot.Chest, "Chest"),
        new EquipPosition(7, EquipKind.Arms, EquipSlot.Arms, "Arms"),
        new EquipPosition(8, EquipKind.Legs, EquipSlot.Legs, "Legs"),
        new EquipPosition(9, EquipKind.Boots, EquipSlot.Boots, "Boots"),
        new EquipPosition(10, EquipKind.Ring, EquipSlot.Ring, "Ring"),
        new EquipPosition(11, EquipKind.Ring, EquipSlot.Ring, "Ring"),
        new EquipPosition(12, EquipKind.Shield, EquipSlot.Shield, "Shield"),
        new EquipPosition(13, EquipKind.GrapplingHook, EquipSlot.None, "Grappling hook"),
        new EquipPosition(14, EquipKind.Glider, EquipSlot.None, "Glider"),
        new EquipPosition(15, EquipKind.Backpack, EquipSlot.None, "Backpack"),
    ]);

    /// <summary>
    /// Whether the Equipped positions are known (<see cref="EquipPositions"/>). Swaps between an Equipped item and
    /// one that fits its position are allowed; equipping into an empty position and unequipping are not, because
    /// they change the mask, and when the game changes header byte 2 isn't known.
    /// </summary>
    public const bool EquipMapKnown = true;     // confirmed in-game: yes (session A)

    static readonly Dictionary<EquipSlot, EquipKind> GearBySlot = new()
    {
        [EquipSlot.Helmet] = EquipKind.Helmet, [EquipSlot.Chest] = EquipKind.Chest, [EquipSlot.Arms] = EquipKind.Arms,
        [EquipSlot.Legs] = EquipKind.Legs, [EquipSlot.Boots] = EquipKind.Boots, [EquipSlot.Ring] = EquipKind.Ring,
        [EquipSlot.Shield] = EquipKind.Shield,
    };

    static readonly (string Suffix, EquipKind Kind)[] AppearanceSuffixes =
    [
        ("_Helmet", EquipKind.HelmetAppearance), ("_Chest", EquipKind.ChestAppearance), ("_Gloves", EquipKind.ArmsAppearance),
        ("_Legs", EquipKind.LegsAppearance), ("_Boots", EquipKind.BootsAppearance),
    ];

    /// <summary>
    /// The kind of Equipped position <paramref name="info"/> goes in, or null when that isn't known. Each rule is
    /// backed by game-written saves (docs/research/player-character.md):
    /// <list type="bullet">
    /// <item>Equipment with an armour, ring or shield slot: that slot;</item>
    /// <item>Equipment with no slot: a backpack (<see cref="BackpackRows"/>); a ward (<c>Ward_*</c>, at position 12
    /// like a shield); an appearance piece (<c>Vanity_*_Helmet</c>, <c>_Chest</c>, <c>_Gloves</c>, <c>_Legs</c>,
    /// <c>_Boots</c>);</item>
    /// <item>Tools: a grappling hook (<c>Tool_Grappling_Hook*</c>) or a glider (<c>Tool_Glider_*</c>).</item>
    /// </list>
    /// Anything else (weapons, other no-slot equipment such as <c>Craft_Shield_*</c>) has no Equipped position.
    /// </summary>
    public static EquipKind? KindOf(ItemInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var name = info.InternalName;
        switch (info.Category)
        {
            case ItemCategory.Equipment when GearBySlot.TryGetValue(info.Slot, out var gear):
                return gear;
            case ItemCategory.Equipment when info.Slot == EquipSlot.None:
                if (BackpackRows.ContainsKey(name)) return EquipKind.Backpack;
                if (name.StartsWith("Ward_", StringComparison.Ordinal)) return EquipKind.Shield;
                if (name.StartsWith("Vanity_", StringComparison.Ordinal))
                    foreach (var (suffix, kind) in AppearanceSuffixes)
                        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return kind;
                return null;
            case ItemCategory.Tools:
                if (name.StartsWith("Tool_Grappling_Hook", StringComparison.Ordinal)) return EquipKind.GrapplingHook;
                if (name.StartsWith("Tool_Glider_", StringComparison.Ordinal)) return EquipKind.Glider;
                return null;
            default:
                return null;
        }
    }

    /// <summary>Whether <paramref name="info"/> may sit in Equipped <paramref name="position"/> (0–15).</summary>
    public static bool Fits(ItemInfo info, int position) =>
        position is >= 0 and < 16 && KindOf(info) == EquipPositions[position].Kind;

    static byte[] Range(int start, int count) => Enumerable.Range(start, count).Select(i => (byte)i).ToArray();
}
