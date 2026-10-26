using EnshroudedSaveEditor.GameData.Install;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// The game's own icons, read from the install: an item's icon is the <c>keen::UiTextureResource</c> with the item's GUID
/// (its <c>objectId</c>; pre-rendered by the game's tools); an ability node's is the texture of its <c>iconActive</c>
/// image (other skill nodes have none). A fully transparent texture counts as no icon. The archive is opened on the first
/// request and read under a lock; when it can't be opened, every icon is missing. Install files are only read. See
/// docs/research/kfc-format.md, "Icons".
/// </summary>
public sealed class KfcIconTextures(GameInstall install) : IIconTextureSource, IDisposable
{
    /// <summary>
    /// The largest icon side: a bigger texture gives its first mip level that fits. The editor draws icons at 22–36 px;
    /// 128 leaves room for high-DPI screens and decodes four times faster than the 256 px originals.
    /// </summary>
    public const int MaxSize = 128;

    /// <summary>Raised whenever how icons are extracted changes, so cached ones are extracted again.</summary>
    const int Format = 1;

    const string TextureType = "keen::UiTextureResource";

    sealed record Index(KfcResources Resources, Dictionary<uint, Guid> Items, Dictionary<uint, Guid> Skills);

    readonly Lock _lock = new();
    readonly Lazy<string> _version = new(() =>
    {
        try
        {
            return $"{Format}-{KfcCache.Key(install)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{Format}";
        }
    });
    Index? _index;
    bool _failed, _disposed;

    /// <summary>The extraction format and the size and timestamp of the game's archive and executable.</summary>
    public string Version => _version.Value;

    public IconTexture? ItemTexture(uint id) => Read(index => index.Items.GetValueOrDefault(id));

    public IconTexture? SkillTexture(uint nodeId) => Read(index => index.Skills.GetValueOrDefault(nodeId));

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _index?.Resources.Dispose();
            _index = null;
        }
    }

    IconTexture? Read(Func<Index, Guid> textureOf)
    {
        lock (_lock)
        {
            if (Open() is not { } index) return null;
            var guid = textureOf(index);
            if (guid == Guid.Empty) return null;
            try
            {
                if (index.Resources.Find(guid, TextureType) is not { } t) return null;
                var content = index.Resources.ContentData(t["data"]["hash"])
                    ?? throw new InvalidDataException($"The content of texture {guid} is missing.");
                var size = t["size"];
                var image = KfcTexture.Decode(t["format"].EnumName, (int)size["x"].Int, (int)size["y"].Int,
                    (int)t["levelCount"].Int, content, MaxSize);
                return Blank(image) ? null : image;
            }
            catch (Exception e) when (Unreadable(e))
            {
                return null;
            }
        }
    }

    /// <summary>The archive and which texture each item and skill node has; null when the install can't be read.</summary>
    Index? Open()
    {
        if (_index is not null || _failed || _disposed) return _index;
        try
        {
            var r = KfcResources.Open(install.Dir);
            var items = Map(() => r.All("keen::ItemInfo").Select(i => (i["itemId"].UInt, i["objectId"].Guid)));
            var skills = Map(() => r.First("keen::SkillTreeResource")["nodes"].Elements
                .Select(n => (n["id"].UInt, n["iconActive"]["image"].Guid)));
            return _index = new Index(r, items, skills);
        }
        catch (Exception e) when (Unreadable(e))
        {
            _failed = true;
            return null;
        }
    }

    /// <summary>Fully transparent: the install's icons of Customization items and Bare Fists are; they get their glyph.</summary>
    static bool Blank(IconTexture image)
    {
        for (var i = 3; i < image.Rgba.Length; i += 4)
            if (image.Rgba[i] != 0) return false;
        return true;
    }

    /// <summary>Id → texture GUID (an id's first); empty when the install's data isn't what this reader expects.</summary>
    static Dictionary<uint, Guid> Map(Func<IEnumerable<(uint Id, Guid Texture)>> read)
    {
        var map = new Dictionary<uint, Guid>();
        try
        {
            foreach (var (id, texture) in read()) map.TryAdd(id, texture);
        }
        catch (Exception e) when (Unreadable(e))
        {
            map.Clear();
        }
        return map;
    }

    static bool Unreadable(Exception e) =>
        e is InvalidDataException or IOException or UnauthorizedAccessException or KeyNotFoundException or NotSupportedException
            or ArgumentException or IndexOutOfRangeException or InvalidOperationException or OverflowException;
}
