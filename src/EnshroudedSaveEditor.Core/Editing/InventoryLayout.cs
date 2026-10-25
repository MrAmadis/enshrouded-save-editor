using System.Collections.ObjectModel;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>
/// Slot capacities and the Equipped slot map. Provisional until the in-game checks of Task 12; each value
/// says whether it has been confirmed in-game (docs/research/player-character.md §4).
/// </summary>
public static class InventoryLayout
{
    /// <summary>Inventory slots 0 to 39: the highest slot seen in any save is 39.</summary>
    public const int InventorySlots = 40;       // confirmed in-game: no

    public const int ExtraSlots = 8;            // confirmed in-game: no

    /// <summary>Backpack grid width.</summary>
    public const int Columns = 8;               // confirmed in-game: no

    /// <summary>
    /// The highest item level an edit may set (the lowest is 1), for any record with a level field. The reference
    /// level ranges don't bound real records (e.g. Alchemist Apprentice pieces at level 10 against 5–5), so they
    /// only give the default level of an added item.
    /// </summary>
    public const int MaxItemLevel = 55;         // confirmed in-game: no — 1.0 patch notes: equipment max level 55

    /// <summary>
    /// Whether the Equipped slot numbers are known to mean fixed positions. Until then nothing moves into,
    /// out of or within Equipped, and no Equipped record is added or removed.
    /// </summary>
    public const bool EquipMapKnown = false;    // confirmed in-game: no

    /// <summary>The Equipped slots each kind of gear may go in; empty until the map is known.</summary>
    public static IReadOnlyDictionary<EquipSlot, byte[]> EquippedSlotsFor { get; } =
        ReadOnlyDictionary<EquipSlot, byte[]>.Empty;   // confirmed in-game: no
}
