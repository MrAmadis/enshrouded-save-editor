using System.Globalization;
using System.Text.Json;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData.Install;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>The skill tree as cached: nodes, links as [a, b] pairs, and the archetype labels.</summary>
internal sealed record KfcTreeSnapshot(IReadOnlyList<SkillNode> Nodes, IReadOnlyList<uint[]> Links, IReadOnlyList<ArchetypeLabel> Labels)
{
    public static KfcTreeSnapshot Of(SkillGraph g) => new(g.Nodes, g.Links.Select(l => new[] { l.A, l.B }).ToList(), g.Labels);

    /// <summary>The graph; throws <see cref="ArgumentException"/> or <see cref="InvalidDataException"/> when it is malformed.</summary>
    public SkillGraph ToGraph() => new(Nodes, Links.Select(l => l is [var a, var b] ? (a, b) : throw new InvalidDataException("A link is not a pair.")).ToList(), Labels);
}

/// <summary>What <see cref="KfcGameData"/> read from the install. A null piece couldn't be read.</summary>
internal sealed record KfcSnapshot(
    IReadOnlyList<ItemInfo>? Items, KfcTreeSnapshot? Tree, IReadOnlyList<SkillReward>? Rewards, int? LevelCap,
    IReadOnlyList<MapLocation>? Locations);

/// <summary>
/// The extracted game data as JSON in the cache folder, keyed to the size and timestamp of <c>enshrouded.kfc</c> and
/// <c>enshrouded.exe</c> (and the cache format). A missing, stale or malformed cache reads as null.
/// </summary>
internal static class KfcCache
{
    /// <summary>Raised whenever what is extracted, or how it is stored, changes.</summary>
    const int Format = 2;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    sealed record Stored(string Key, int Format, KfcSnapshot? Data);

    public static string Key(GameInstall g) =>
        string.Join("-", new[] { "enshrouded.kfc", "enshrouded.exe" }.Select(name =>
        {
            var f = new FileInfo(Path.Combine(g.Dir, name));
            return string.Create(CultureInfo.InvariantCulture, $"{f.Length}-{f.LastWriteTimeUtc.Ticks}");
        }));

    public static KfcSnapshot? Read(string path, string key)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json);
            if (stored is not { Format: Format, Data: { } data } || stored.Key != key) return null;
            _ = data.Tree?.ToGraph();
            if (data.Items is { } items && items.Select(i => i.Id).Distinct().Count() != items.Count) return null;
            return data;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException
            or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the cache (to a temporary file, then moved over the old one); failures are ignored.</summary>
    public static void Write(string path, string key, KfcSnapshot data)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(new Stored(key, Format, data), Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
        }
    }
}
