using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>
/// URLs for icons the window serves itself, so components need no WebView in tests. Views already carry their items'
/// <c>IconUrl</c>; this covers what a component only knows by id or position.
/// </summary>
public interface IAssetUrls
{
    string ItemIcon(uint id);

    string SkillIcon(uint nodeId);

    /// <summary>The glyph an empty Equipped position shows.</summary>
    string PositionIcon(EquipPosition position);
}

/// <summary>Icons from <see cref="IconService"/>, served from <see cref="IconsHost"/> (mapped to <see cref="IconService.CacheRoot"/>).</summary>
public sealed class IconAssets(IconService icons, GameDataProvider game) : IAssetUrls
{
    public const string IconsHost = IconService.Host;

    public string ItemIcon(uint id) => icons.ItemIcon(id, game.Data.Items.Find(id));

    /// <summary>An id missing from the tree gets the unknown glyph.</summary>
    public string SkillIcon(uint nodeId) =>
        game.Data.Tree.Find(nodeId) is { } node ? icons.SkillIcon(node) : icons.ItemIcon(nodeId, null);

    public string PositionIcon(EquipPosition position) => icons.PositionIcon(position);
}
