using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// Reads the item catalog from every <c>keen::ItemInfo</c> resource, filled the way the reference data fills it (see
/// docs/research/kfc-format.md, "Items").
/// </summary>
internal static class KfcItems
{
    const int VanityFlag = 1 << 18; // keen::ItemFlag.IsVanityItem

    /// <param name="levelCap">The player level cap: a level range that ends at <paramref name="itemLevelCap"/> ends here.</param>
    public static List<ItemInfo> Read(KfcResources r, KfcText text, int levelCap, int itemLevelCap)
    {
        var weaponTypes = new Dictionary<Guid, string>();
        foreach (var w in r.First("keen::ItemRegistryResource")["weaponCategories"].Elements)
            weaponTypes[w["guid"].Guid] = w["weaponCategory"]["categoryType"].EnumName;

        var items = new List<ItemInfo>();
        var seen = new HashSet<uint>();
        foreach (var item in r.All("keen::ItemInfo"))
        {
            var id = item["itemId"].UInt;
            if (!seen.Add(id)) continue;
            var internalName = item["debugName"].String;
            var name = text[item["name"]];
            var levels = item["itemLevelRange"];
            var max = (int)levels["maxLevel"].Int;
            var vanity = (item["flags"].Int & VanityFlag) != 0;
            items.Add(new ItemInfo(
                id,
                name.Length > 0 ? name : internalName,
                Enum.TryParse<ItemCategory>(item["category"].EnumName, out var category) ? category : ItemCategory.Materials,
                (Rarity)(item["rarity"].Int + 1),
                (int)item["maxStackSize"].Int,
                (int)levels["minLevel"].Int,
                max == itemLevelCap ? levelCap : max,
                vanity ? EquipSlot.None : Slot(item["equipment"]["slot"].EnumName),
                weaponTypes.GetValueOrDefault(item["weaponCategoryReference"].Guid),
                internalName,
                text[item["description"]],
                item["perkIds"].Elements.Any(p => p.UInt != 0) ? 1 : 0,
                item["disableRarityGeneration"].Int != 0));
        }
        return items;
    }

    /// <summary>The editor's slot for an <c>keen::EquipmentSlotType</c>; vanity pieces have none (they go by name).</summary>
    static EquipSlot Slot(string slot) => slot switch
    {
        "Armour_Head" => EquipSlot.Helmet,
        "Armour_UpperBody" => EquipSlot.Chest,
        "Armour_Arms" => EquipSlot.Arms,
        "Armour_LowerBody" => EquipSlot.Legs,
        "Armour_Feet" => EquipSlot.Boots,
        "Armour_Ring" => EquipSlot.Ring,
        "Shield" => EquipSlot.Shield,
        "MeleeWeapon" or "MidRangeWeapon" or "RangeWeapon" => EquipSlot.Weapon,
        _ => EquipSlot.None,
    };
}
