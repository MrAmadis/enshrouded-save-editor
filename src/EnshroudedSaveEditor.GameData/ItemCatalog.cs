using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

/// <summary>An item list with lookup by id.</summary>
internal sealed class ItemCatalog(IReadOnlyList<ItemInfo> all) : IItemCatalog
{
    readonly Dictionary<uint, ItemInfo> _byId = all.ToDictionary(i => i.Id);
    public IReadOnlyList<ItemInfo> All { get; } = all;
    public ItemInfo? Find(uint id) => _byId.GetValueOrDefault(id);
}
