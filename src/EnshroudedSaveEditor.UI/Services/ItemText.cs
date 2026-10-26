using System.Globalization;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>How the Inventory tab and the Add item browser name categories, rarities and places.</summary>
public static class ItemText
{
    /// <summary>The order the Add item browser lists the categories in (it leaves out the ones with no items).</summary>
    public static IReadOnlyList<ItemCategory> CategoryOrder { get; } =
    [
        ItemCategory.Weapons, ItemCategory.Equipment, ItemCategory.Ammunition, ItemCategory.WeaponGem, ItemCategory.Consumables,
        ItemCategory.AnimalFood, ItemCategory.Materials, ItemCategory.Tools, ItemCategory.FishingRod, ItemCategory.Instrument,
        ItemCategory.BuildTools, ItemCategory.Blueprints, ItemCategory.ColorPalette, ItemCategory.Collectibles,
        ItemCategory.Currency, ItemCategory.Customization,
    ];

    /// <summary>The label for a slotless Equipment item and for a weapon without a type.</summary>
    public const string Other = "Other";

    public static string Category(ItemCategory? category) => category switch
    {
        null => "Unknown",
        ItemCategory.AnimalFood => "Animal food",
        ItemCategory.BuildTools => "Building",
        ItemCategory.ColorPalette => "Colour palettes",
        ItemCategory.FishingRod => "Fishing rods",
        ItemCategory.Instrument => "Instruments",
        ItemCategory.WeaponGem => "Weapon gems",
        { } c => c.ToString(),
    };

    static readonly Dictionary<string, string> WeaponTypes = new(StringComparer.Ordinal)
    {
        ["Dagger"] = "Daggers", ["GreatSword"] = "Greatswords", ["OneHandAxe"] = "Axes", ["OneHandClub"] = "Clubs",
        ["OneHandSword"] = "Swords", ["OneHandWeapon"] = "Other one-handed", ["RangedBow"] = "Bows", ["RangedStaff"] = "Staffs",
        ["RangedWand"] = "Wands", ["Throwable"] = "Throwables", ["TwoHandAxe"] = "Two-handed axes",
        ["TwoHandClub"] = "Two-handed clubs", ["TwoHandHammer"] = "Hammers", ["UnarmedFists"] = "Fists",
    };

    /// <summary>
    /// A <see cref="CatalogEntryView.SubCategory"/> as the browser shows it: weapon types by name ("RangedBow" is
    /// "Bows"), Equipment slots as they are, and both "" and the slotless "None" as <see cref="Other"/>.
    /// </summary>
    public static string SubCategory(ItemCategory category, string sub) => category switch
    {
        _ when sub.Length == 0 => Other,
        ItemCategory.Weapons => WeaponTypes.GetValueOrDefault(sub, sub),
        ItemCategory.Equipment when sub == nameof(EquipSlot.None) => Other,
        _ => sub,
    };

    /// <summary>
    /// A stackable item, or more than one of anything: it gets a count badge and the Stack stepper. One of an unknown
    /// item isn't, as its limit (65535) is only a guess.
    /// </summary>
    public static bool IsStack(ItemView item) => item.Count > 1 || (item.MaxStack > 1 && item.Known);

    /// <summary>The slot tint class: <c>r1</c> (Common) to <c>r5</c> (Legendary), or <c>r0</c> (none).</summary>
    public static string RarityClass(Rarity? rarity) => rarity is { } r ? $"r{(int)r}" : "r0";

    /// <summary>The text colour class for a rarity name.</summary>
    public static string RarityColor(Rarity? rarity) => rarity is { } r ? $"c-{r.ToString().ToLowerInvariant()}" : "";

    /// <summary>"Lv 1–50", "Lv 13", or "" when the item has no level range.</summary>
    public static string LevelRange(int min, int max) =>
        max <= 0 ? "" : min >= max ? $"Lv {max}" : $"Lv {min}–{max}";

    /// <summary>Beside a level stepper: "levels 1–50".</summary>
    public static string Levels(int min, int max) => $"levels {min}–{max}";

    /// <summary>
    /// Beside a fixed level (<see cref="ItemView.FixedLevel"/>): "set by the game", and when upgrades raised it past
    /// its base, "set by the game: base 5 plus upgrades".
    /// </summary>
    public static string SetByGame(int baseLevel, int level) =>
        level == baseLevel ? "set by the game" : $"set by the game: base {baseLevel} plus upgrades";

    /// <summary>Thousands separated, as the category counts show.</summary>
    public static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The <c>data-ref</c> of a slot: "Inventory:30", "Equipped:5", "Extra:20".</summary>
    public static string Ref(ItemRef r) => $"{r.Kind}:{r.Slot}";

    /// <summary>
    /// Where a slot is, as the user sees it: "Equipped · Helmet", "Backpack · slot 3" and "Action bar · slot 15" (by
    /// their place in <see cref="SessionView.Backpack"/> and <see cref="SessionView.Hotbar"/>), or else "Outside the backpack".
    /// </summary>
    public static string Where(SessionView view, ItemRef r)
    {
        if (r.Kind == ContainerKind.Equipped)
            return $"Equipped · {view.Equipped.FirstOrDefault(s => s.Ref == r)?.Label ?? $"position {r.Slot}"}";
        if (IndexOf(view.Backpack, r) is var i and >= 0)
            return $"Backpack · slot {i + 1}";
        if (IndexOf(view.Hotbar, r) is var j and >= 0)
            return $"Action bar · slot {j + 1}";
        return "Outside the backpack";
    }

    /// <summary>The first free slot of the backpack grid, where an add without a target goes; -1 when it is full.</summary>
    public static int FirstFree(SessionView view)
    {
        for (var i = 0; i < view.Backpack.Count; i++)
            if (view.Backpack[i].Item is null)
                return i;
        return -1;
    }

    static int IndexOf(IReadOnlyList<SlotView> slots, ItemRef r)
    {
        for (var i = 0; i < slots.Count; i++)
            if (slots[i].Ref == r)
                return i;
        return -1;
    }
}
