using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>A character in the picker.</summary>
/// <param name="UnspentPoints">Skill points left to spend; null for a file that isn't <see cref="CharacterStatus.Ok"/>.</param>
public sealed record CharacterView(
    string Path, string FileName, string Name, int Level, DateTime SavedAtLocal, CharacterStatus Status, string? StatusMessage,
    int? UnspentPoints);

/// <summary>One item record as the inventory shows it.</summary>
/// <param name="Name">"Unknown item {id:x8}" for an item missing from the game data (<paramref name="Known"/> false).</param>
/// <param name="Rarity">The record's rarity, or else the item's base rarity (for the tint); null when neither is known.</param>
/// <param name="MaxStack">The most a stack may hold: 1 to 65535 (65535 for an unknown item), as SetCount allows.</param>
/// <param name="Level">The record's item level; null when it has no level field (then it can't be set).</param>
/// <param name="MinLevel">The levels SetItemLevel allows (<see cref="InventoryLayout.LevelRange"/>: the item's range, up to
/// 55); null for an unknown item (which allows 1 to <see cref="InventoryLayout.MaxItemLevel"/>).</param>
/// <param name="FixedLevel">The game sets the level (<see cref="ItemInfo.FixedLevel"/>): <paramref name="MinLevel"/> plus
/// upgrades. It can't be set.</param>
/// <param name="Durability">Null when the record has no durability field.</param>
/// <param name="HasRarity">Whether the record has a rarity field, so its rarity can be set.</param>
public sealed record ItemView(
    uint Id, string Name, ItemCategory? Category, Rarity? Rarity, int Count, int MaxStack, int? Level, int? MinLevel, int? MaxLevel,
    bool FixedLevel, uint? Durability, uint? MaxDurability, bool Known, string IconUrl, bool HasRarity);

/// <summary>
/// One slot: an Inventory or Extra slot, or an Equipped position (<see cref="ItemRef"/>). <paramref name="Item"/> is null
/// when the slot is empty, holds a ghost (an empty remembered stack) or holds a hidden record (Customization).
/// </summary>
/// <param name="Label">The Equipped position's label ("Helmet", "Ring"…), the key of the first action-bar row ("1"–"8"), else null.</param>
public sealed record SlotView(ItemRef Ref, string? Label, ItemView? Item);

/// <param name="Rank">0 when not learned (rank-0 entries count as not learned).</param>
/// <param name="State">Sparks are always <see cref="NodeState.Learned"/>.</param>
/// <param name="Attribute">For an attribute node, the attribute it raises: "STR", "DEX", "INT", "CON", "SPI" or "END".</param>
/// <param name="Picture">The node's own picture from the install (abilities have one); null when it has none.</param>
/// <param name="Title">The name the tree shows above a named skill; null for Sparks (all "Spark") and for attribute nodes
/// named after their attribute ("Strength", which <paramref name="Attribute"/> already says).</param>
public sealed record SkillNodeView(
    uint Id, string Name, string Description, SkillNodeType Type, SkillClass Class, int Cost, int Rank, int MaxRank, NodeState State,
    double X, double Y, string? Attribute, string? Picture, string? Title);

/// <summary>The open character.</summary>
/// <param name="Equipped">The 16 Equipped positions in order (<see cref="InventoryLayout.EquipPositions"/>).</param>
/// <param name="Backpack">The backpack grid, <see cref="InventoryLayout.Columns"/> per row: the base rows, then the equipped
/// backpack's extension rows (<see cref="InventoryLayout.BackpackGrid"/>).</param>
/// <param name="Hotbar">The two action-bar rows (<see cref="InventoryLayout.HotbarRows"/>), 16 slots.</param>
/// <param name="Pending">The unsaved edits, one line each, oldest first.</param>
/// <param name="CanUndo">There is an edit to undo (up to the last <see cref="EditorService.UndoDepth"/>).</param>
/// <param name="XpToNextLevel">The XP from this level to the next (R6); null when the XP table is unknown or at the cap.</param>
/// <param name="Overflow">Records outside the grid the UI shows: Extra slots past the equipped backpack's extension, and
/// Inventory slots past 39. They are kept as they are; the UI lists them so they can be moved or removed.</param>
public sealed record SessionView(
    string Path,
    string FileName,
    string Name,
    int Level,
    uint Xp,
    int LevelCap,
    bool ReadOnly,
    string? ReadOnlyReason,
    InventoryStatus Inventory,
    IReadOnlyList<SlotView> Equipped,
    IReadOnlyList<SlotView> Backpack,
    IReadOnlyList<SlotView> Hotbar,
    PointsSummary Points,
    IReadOnlyList<SkillNodeView> Nodes,
    IReadOnlyList<(uint A, uint B)> Links,
    IReadOnlyList<ArchetypeLabel> Labels,
    IReadOnlyList<string> Pending,
    bool CanUndo,
    uint? XpToNextLevel,
    IReadOnlyList<SlotView> Overflow);

/// <summary>An item the Add item browser offers.</summary>
/// <param name="SubCategory">The weapon type for Weapons ("RangedBow"; "" when it has none), the <see cref="EquipSlot"/>
/// name for Equipment ("Helmet"; "None" for backpacks, wards and appearance pieces), and "" otherwise.</param>
/// <param name="MaxStack">1 to 65535, as AddItem allows.</param>
/// <param name="FixedLevel">The game sets the level (<see cref="ItemInfo.FixedLevel"/>), so a new one is added at
/// <paramref name="MinLevel"/>; otherwise AddItem takes a level from <paramref name="MinLevel"/> to <paramref name="MaxLevel"/>
/// (<see cref="InventoryLayout.LevelRange"/>: the item's range, up to 55).</param>
/// <param name="Icon">Gives the icon's URL; asked only when the item is shown, because the first ask may extract the
/// install's icon (the catalog holds every item).</param>
/// <param name="InternalName">The game's own name ("Weapon_T1_2H_BowEpic_02"), which the browser's search also covers.</param>
public sealed record CatalogEntryView(
    uint Id, string Name, ItemCategory Category, string SubCategory, Rarity BaseRarity, int MaxStack, int MinLevel, int MaxLevel,
    bool FixedLevel, Func<string> Icon, string InternalName)
{
    public string IconUrl => Icon();
}
