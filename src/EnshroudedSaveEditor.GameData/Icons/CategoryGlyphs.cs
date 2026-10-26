using System.Collections.ObjectModel;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

/// <summary>
/// Line icons for what has no icon of the game's own (no install, an unknown item, a skill node other than an ability, an
/// empty Equipped position): one per item category, armour slot, Equipped position kind and skill node type, in the style
/// of the approved mockup (<c>docs/superpowers/specs/mockups/inventory-and-picker.html</c>): a 24×24 view, no fill, 1.6
/// round strokes. The stroke is the mockup's slot colour, because an SVG shown as an image can't inherit the page's colour.
/// </summary>
public static class CategoryGlyphs
{
    public const string Unknown = "unknown";

    const string Head =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"24\" height=\"24\" fill=\"none\" " +
        "stroke=\"#cfd6de\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\">";

    static readonly Dictionary<string, string> Bodies = new(StringComparer.Ordinal)
    {
        // From the mockup.
        ["weapons"] = "<path d=\"M14 4h6v6L9.5 20.5l-6-6z M6.5 12.5l5 5 M3.5 20.5l2-2\"/>",
        ["bow"] = "<path d=\"M6 3c9 3 9 15 0 18 M6 3v18 M3 12h15 M15 9l3 3-3 3\"/>",
        ["staff"] = "<circle cx=\"16.5\" cy=\"6.5\" r=\"3\"/><path d=\"M14.5 8.5L4 21 M19 2l1 2 M21.5 6.5h-1\"/>",
        ["shield"] = "<path d=\"M12 3l7 3v6c0 5-3 8-7 9-4-1-7-4-7-9V6z M12 3v18\"/>",
        ["consumables"] = "<path d=\"M10 3h4 M10.5 3v5L6 15a4 4 0 0 0 3.5 6h5A4 4 0 0 0 18 15l-4.5-7V3 M7.5 14h9\"/>",
        ["animal-food"] = "<path d=\"M4 13h16a8 8 0 0 1-16 0z M8 9c0-2 2-2 2-4 M12 9c0-2 2-2 2-4 M16 9c0-2 2-2 2-4\"/>",
        ["materials"] = "<path d=\"M4 15l4-8 7-2 5 7-3 7-9 1z M8 7l3 6 9-1 M11 13l-4 7\"/>",
        ["ammunition"] = "<path d=\"M4 20L19 5 M19 5h-5 M19 5v5 M4 20l1-4 M4 20l4-1 M7 17l1-3 M7 17l3-1\"/>",
        ["tools"] = "<path d=\"M4 8c5-5 11-5 16 0 M12 5L6 21\"/>",
        ["helmet"] = "<path d=\"M5 15a7 7 0 0 1 14 0v3H5z M12 8v10 M5 15h14\"/>",
        ["chest"] = "<path d=\"M8 4l4 2 4-2 4 4-2 3v9H6v-9L4 8z M12 6v14\"/>",
        ["arms"] = "<path d=\"M7 21v-8L5 9V6h2v4V5h2v5V4h2v6V5h2v8l3-2-1 4-3 4v2z\"/>",
        ["legs"] = "<path d=\"M7 3h10l-1 18h-3l-1-11-1 11H8z\"/>",
        ["boots"] = "<path d=\"M8 3h5v11l6 3v4H6v-4l2-3z\"/>",
        ["ring"] = "<circle cx=\"12\" cy=\"14\" r=\"6\"/><path d=\"M10 6.5L12 4l2 2.5\"/>",

        // Drawn to match.
        ["blueprints"] = "<rect x=\"5\" y=\"3\" width=\"14\" height=\"18\" rx=\"1.5\"/><path d=\"M8 8h8 M8 12h8 M8 16h5\"/>",
        ["build-tools"] = "<path d=\"M12 3l8 4.5v9L12 21l-8-4.5v-9z M4 7.5l8 4.5 8-4.5 M12 12v9\"/>",
        ["collectibles"] = "<path d=\"M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z\"/>",
        ["color-palette"] = "<path d=\"M12 3a9 9 0 1 0 0 18c1.5 0 2-1 2-2s-1-1.5-1-2.5 1-1.5 2-1.5h2a4 4 0 0 0 4-4c0-4.4-4-8-9-8z\"/>" +
                            "<circle cx=\"7.5\" cy=\"11\" r=\"1\"/><circle cx=\"10\" cy=\"7\" r=\"1\"/><circle cx=\"15\" cy=\"7.5\" r=\"1\"/>",
        ["currency"] = "<circle cx=\"12\" cy=\"12\" r=\"8\"/><circle cx=\"12\" cy=\"12\" r=\"4.5\"/>",
        ["customization"] = "<path d=\"M4 7h16v4H4z M6 11v7 M9 11v7 M12 11v7 M15 11v7 M18 11v7\"/>",
        ["equipment"] = "<path d=\"M9 3h6l1 3 4 15H4L8 6z M9 3l3 4 3-4\"/>",
        ["fishing-rod"] = "<path d=\"M4 20L18 4 M18 4v11 M18 15a2 2 0 1 1-2 2\"/>",
        ["instrument"] = "<path d=\"M9 18V5l11-2v13\"/><circle cx=\"6\" cy=\"18\" r=\"3\"/><circle cx=\"17\" cy=\"16\" r=\"3\"/>",
        ["weapon-gem"] = "<path d=\"M7 4h10l4 5-9 11L3 9z M3 9h18 M8 9l4 11 4-11 M10 4l-2 5 M14 4l2 5\"/>",
        ["backpack"] = "<path d=\"M9 7V5a3 3 0 0 1 6 0v2 M6 7h12a1 1 0 0 1 1 1v13H5V8a1 1 0 0 1 1-1z M8 13h8v4H8z\"/>",
        ["glider"] = "<path d=\"M2 11c3-4 7-6 10-6s7 2 10 6c-3-1-6-1-10 0-4-1-7-1-10 0z M12 11v8 M8 20h8\"/>",
        ["hook"] = "<path d=\"M12 21V9 M12 9a6 6 0 0 1-6-6 M12 9a6 6 0 0 0 6-6 M12 9V3 M10 19l2 2 2-2\"/>",
        ["spark"] = "<circle cx=\"12\" cy=\"12\" r=\"2.5\"/><path d=\"M12 2v5 M12 17v5 M2 12h5 M17 12h5 M5 5l3.5 3.5 M15.5 15.5L19 19 M19 5l-3.5 3.5 M8.5 15.5L5 19\"/>",
        ["attribute"] = "<circle cx=\"12\" cy=\"12\" r=\"8\"/><path d=\"M12 8v8 M8 12h8\"/>",
        ["ability"] = "<path d=\"M12 3l9 9-9 9-9-9z M12 8l4 4-4 4-4-4z\"/>",
        ["passive"] = "<circle cx=\"12\" cy=\"12\" r=\"8\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
        [Unknown] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .9-1 1.7v.5 M12 17.5v.01\"/>",
    };

    /// <summary>Every glyph by name: a complete SVG document.</summary>
    public static IReadOnlyDictionary<string, string> Svg { get; } =
        new ReadOnlyDictionary<string, string>(Bodies.ToDictionary(kv => kv.Key, kv => Head + kv.Value + "</svg>", StringComparer.Ordinal));

    public static string ForCategory(ItemCategory category) => category switch
    {
        ItemCategory.Ammunition => "ammunition",
        ItemCategory.AnimalFood => "animal-food",
        ItemCategory.Blueprints => "blueprints",
        ItemCategory.BuildTools => "build-tools",
        ItemCategory.Collectibles => "collectibles",
        ItemCategory.ColorPalette => "color-palette",
        ItemCategory.Consumables => "consumables",
        ItemCategory.Currency => "currency",
        ItemCategory.Customization => "customization",
        ItemCategory.Equipment => "equipment",
        ItemCategory.FishingRod => "fishing-rod",
        ItemCategory.Instrument => "instrument",
        ItemCategory.Materials => "materials",
        ItemCategory.Tools => "tools",
        ItemCategory.WeaponGem => "weapon-gem",
        ItemCategory.Weapons => "weapons",
        _ => Unknown,
    };

    /// <summary>The glyph of an armour, ring or shield slot; null for the others.</summary>
    public static string? ForSlot(EquipSlot slot) => slot switch
    {
        EquipSlot.Helmet => "helmet",
        EquipSlot.Chest => "chest",
        EquipSlot.Arms => "arms",
        EquipSlot.Legs => "legs",
        EquipSlot.Boots => "boots",
        EquipSlot.Ring => "ring",
        EquipSlot.Shield => "shield",
        _ => null,
    };

    /// <summary>What an Equipped position of <paramref name="kind"/> holds; appearance positions show the piece they dress.</summary>
    public static string ForPosition(EquipKind kind) => kind switch
    {
        EquipKind.HelmetAppearance or EquipKind.Helmet => "helmet",
        EquipKind.ChestAppearance or EquipKind.Chest => "chest",
        EquipKind.ArmsAppearance or EquipKind.Arms => "arms",
        EquipKind.LegsAppearance or EquipKind.Legs => "legs",
        EquipKind.BootsAppearance or EquipKind.Boots => "boots",
        EquipKind.Ring => "ring",
        EquipKind.Shield => "shield",
        EquipKind.GrapplingHook => "hook",
        EquipKind.Glider => "glider",
        EquipKind.Backpack => "backpack",
        _ => Unknown,
    };

    /// <summary>
    /// An item's glyph: its armour slot or Equipped position kind when it has one, bows and staffs (wands too) by
    /// weapon type, otherwise its category; <see cref="Unknown"/> for an item missing from the game data.
    /// </summary>
    public static string ForItem(ItemInfo? info)
    {
        if (info is null) return Unknown;
        if (info.Category == ItemCategory.Weapons)
            return info.WeaponType switch
            {
                "RangedBow" => "bow",
                "RangedStaff" or "RangedWand" => "staff",
                _ => "weapons",
            };
        if (info.Category == ItemCategory.Equipment && ForSlot(info.Slot) is { } slot) return slot;
        if (info.Category is ItemCategory.Equipment or ItemCategory.Tools && InventoryLayout.KindOf(info) is { } kind)
            return ForPosition(kind);
        return ForCategory(info.Category);
    }

    public static string ForSkill(SkillNodeType type) => type switch
    {
        SkillNodeType.Spark => "spark",
        SkillNodeType.Attribute => "attribute",
        SkillNodeType.Ability => "ability",
        SkillNodeType.Passive => "passive",
        _ => Unknown,
    };
}
