using System.Collections.Concurrent;
using System.Text;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

/// <summary>
/// Icon URLs for items, skills and Equipped positions, served by the window from <c>https://icons.ese/</c>, which it
/// maps to <paramref name="iconCacheRoot"/>.
/// <para>
/// With a texture source (the install's icons, <paramref name="textures"/>), an item or skill node with a texture gets
/// <c>https://icons.ese/item/&lt;id:x8&gt;.png</c> or <c>https://icons.ese/skill/&lt;id:x8&gt;.png</c>: the PNG is
/// written to that path under the cache on the id's first request (never all up front), then reused, also by later
/// sessions until the source's <see cref="IIconTextureSource.Version"/> changes. Without a source, or when a texture can't
/// be found or decoded (remembered for the session), the icon is a <see cref="CategoryGlyphs"/> glyph:
/// <c>https://icons.ese/glyph/&lt;name&gt;.svg</c>, written to <c>&lt;iconCacheRoot&gt;/glyph/&lt;name&gt;.svg</c> on first
/// use (rewritten when the cached file differs).
/// </para>
/// A cache that can't be written gives a URL with nothing behind it, never an exception.
/// </summary>
public sealed class IconService(string iconCacheRoot, IIconTextureSource? textures = null)
{
    public const string Host = "icons.ese";
    const string GlyphFolder = "glyph", ItemFolder = "item", SkillFolder = "skill", VersionFile = "textures.version";

    readonly ConcurrentDictionary<string, bool> _written = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Lazy<bool>> _extracted = new(StringComparer.Ordinal);
    readonly Lock _versionLock = new();
    bool? _cacheIsCurrent;

    /// <summary>The folder the window maps to <see cref="Host"/>.</summary>
    public string CacheRoot { get; } = !string.IsNullOrWhiteSpace(iconCacheRoot)
        ? iconCacheRoot
        : throw new ArgumentException("The icon cache folder is required.", nameof(iconCacheRoot));

    /// <param name="id">The item id; the install's icon is looked up by it.</param>
    /// <param name="info">Null for an item missing from the game data; it chooses the glyph.</param>
    public string ItemIcon(uint id, ItemInfo? info) =>
        Texture(ItemFolder, id, t => t.ItemTexture(id)) ?? Glyph(CategoryGlyphs.ForItem(info));

    public string SkillIcon(SkillNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Texture(SkillFolder, node.Id, t => t.SkillTexture(node.Id)) ?? Glyph(CategoryGlyphs.ForSkill(node.Type));
    }

    /// <summary>What an empty Equipped position shows.</summary>
    public string PositionIcon(EquipPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return Glyph(CategoryGlyphs.ForPosition(position.Kind));
    }

    /// <summary>The URL of the id's PNG, extracted on its first request; null when there is no texture to show.</summary>
    string? Texture(string folder, uint id, Func<IIconTextureSource, IconTexture?> read)
    {
        if (textures is not { } source) return null;
        var name = $"{folder}/{id:x8}.png";
        var extracted = _extracted.GetOrAdd(name, n => new Lazy<bool>(() => Extract(source, n, read))).Value;
        return extracted ? $"https://{Host}/{name}" : null;
    }

    bool Extract(IIconTextureSource source, string name, Func<IIconTextureSource, IconTexture?> read)
    {
        var path = Path.Combine(CacheRoot, name);
        bool current;
        lock (_versionLock) current = _cacheIsCurrent ??= DropStaleTextures(source);
        if (current && File.Exists(path)) return true;
        if (read(source) is not { } t || t.Width <= 0 || t.Height <= 0 || t.Rgba.Length != t.Width * t.Height * 4) return false;
        return Save(path, Png.Encode(t));
    }

    /// <summary>
    /// Deletes the item and skill icons cached for another version of the textures, and records this one. False when that
    /// failed: cached icons are then extracted again rather than trusted.
    /// </summary>
    bool DropStaleTextures(IIconTextureSource source)
    {
        var stamp = Path.Combine(CacheRoot, VersionFile);
        try
        {
            if (File.Exists(stamp) && File.ReadAllText(stamp) == source.Version) return true;
            foreach (var folder in new[] { ItemFolder, SkillFolder }.Select(f => Path.Combine(CacheRoot, f)))
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return Save(stamp, Encoding.UTF8.GetBytes(source.Version));
    }

    string Glyph(string name)
    {
        _written.GetOrAdd(name, WriteGlyph);
        return $"https://{Host}/{GlyphFolder}/{name}.svg";
    }

    bool WriteGlyph(string name)
    {
        var svg = CategoryGlyphs.Svg[name];
        var path = Path.Combine(CacheRoot, GlyphFolder, name + ".svg");
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == svg) return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return Save(path, Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>Writes the file through a temporary one beside it; false when the cache can't be written.</summary>
    static bool Save(string path, byte[] data)
    {
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(tmp, data);
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
