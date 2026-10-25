using System.Collections.Concurrent;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

/// <summary>
/// Icon URLs for items, skills and Equipped positions, served by the window from <c>https://icons.ese/</c>, which it
/// maps to <paramref name="iconCacheRoot"/>. Until the install's icons can be read (Task 20) every icon is a
/// <see cref="CategoryGlyphs"/> glyph: <c>https://icons.ese/glyph/&lt;name&gt;.svg</c>, written to
/// <c>&lt;iconCacheRoot&gt;/glyph/&lt;name&gt;.svg</c> on first use (rewritten when the cached file differs). A cache
/// that can't be written gives a URL with nothing behind it, never an exception.
/// </summary>
public sealed class IconService(string iconCacheRoot)
{
    public const string Host = "icons.ese";
    const string GlyphFolder = "glyph";

    readonly ConcurrentDictionary<string, bool> _written = new(StringComparer.Ordinal);

    /// <summary>The folder the window maps to <see cref="Host"/>.</summary>
    public string CacheRoot { get; } = !string.IsNullOrWhiteSpace(iconCacheRoot)
        ? iconCacheRoot
        : throw new ArgumentException("The icon cache folder is required.", nameof(iconCacheRoot));

    /// <param name="id">The item id; the install's icon is looked up by it from Task 20.</param>
    /// <param name="info">Null for an item missing from the game data.</param>
    public string ItemIcon(uint id, ItemInfo? info) => Glyph(CategoryGlyphs.ForItem(info));

    public string SkillIcon(SkillNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Glyph(CategoryGlyphs.ForSkill(node.Type));
    }

    /// <summary>What an empty Equipped position shows.</summary>
    public string PositionIcon(EquipPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return Glyph(CategoryGlyphs.ForPosition(position.Kind));
    }

    string Glyph(string name)
    {
        _written.GetOrAdd(name, Write);
        return $"https://{Host}/{GlyphFolder}/{name}.svg";
    }

    bool Write(string name)
    {
        var svg = CategoryGlyphs.Svg[name];
        var path = Path.Combine(CacheRoot, GlyphFolder, name + ".svg");
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == svg) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tmp, svg);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
