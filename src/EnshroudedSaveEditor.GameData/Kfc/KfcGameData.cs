using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData.Install;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// Game data read from the install's resource database (docs/research/kfc-format.md), cached per game build in
/// <c>cacheDir</c>. A piece that couldn't be read throws <see cref="NotSupportedException"/> (its name) from its
/// property; the XP-per-level table isn't in the install.
/// </summary>
public sealed class KfcGameData : IGameData
{
    const string CacheFile = "install.json";

    readonly IItemCatalog? _items;
    readonly SkillGraph? _tree;
    readonly IReadOnlyDictionary<uint, SkillReward>? _rewards;
    readonly int? _levelCap;
    readonly IReadOnlyList<MapLocation>? _locations;

    KfcGameData(KfcSnapshot s)
    {
        _items = s.Items is { } items ? new ItemCatalog(items) : null;
        _tree = s.Tree?.ToGraph();
        _rewards = s.Rewards?.ToDictionary(r => r.Id);
        _levelCap = s.LevelCap;
        _locations = s.Locations;
    }

    public IItemCatalog Items => _items ?? throw new NotSupportedException("items");
    public SkillGraph Tree => _tree ?? throw new NotSupportedException("skill tree");
    public IReadOnlyDictionary<uint, SkillReward> Rewards => _rewards ?? throw new NotSupportedException("rewards");
    public int LevelCap => _levelCap ?? throw new NotSupportedException("level cap");
    public IReadOnlyList<uint>? XpPerLevel => throw new NotSupportedException("XP table");
    public IReadOnlyList<MapLocation> Locations => _locations ?? throw new NotSupportedException("map locations");
    public string Source => "Your Enshrouded install";

    /// <summary>
    /// The install's data: from the cache when it was made from these game files, else read from the install (and cached
    /// when every piece could be read). Throws when the install's archive or type metadata can't be opened at all.
    /// </summary>
    public static KfcGameData Load(GameInstall g, string cacheDir) => Load(g, cacheDir, Extract);

    /// <summary>Tests: <see cref="Load(GameInstall, string)"/> with another reader of the install.</summary>
    internal static KfcGameData Load(GameInstall g, string cacheDir, Func<GameInstall, KfcSnapshot> extract)
    {
        var path = Path.Combine(cacheDir, CacheFile);
        var key = KfcCache.Key(g);
        if (KfcCache.Read(path, key) is { } cached && Complete(cached)) return new KfcGameData(cached);
        var snapshot = extract(g);
        // A piece that failed may fail only this time (a file Steam is writing): it is read again on the next start.
        if (Complete(snapshot)) KfcCache.Write(path, key, snapshot);
        return new KfcGameData(snapshot);
    }

    static bool Complete(KfcSnapshot s) =>
        s is { Items: not null, Tree: not null, Rewards: not null, LevelCap: not null, Locations: not null };

    sealed record LevelCaps(int Player, int Item);

    static KfcSnapshot Extract(GameInstall g)
    {
        using var r = KfcResources.Open(g.Dir);
        var text = Piece(() => KfcText.Load(r));
        var caps = Piece(() =>
        {
            var balancing = r.First("keen::BalancingTable");
            return new LevelCaps((int)balancing["playerLevelCap"].Int, (int)balancing["itemLevelCap"].Int);
        });
        var items = text is null || caps is null ? null : Piece(() => KfcItems.Read(r, text, caps.Player, caps.Item));
        var tree = text is null ? null : Piece(() => KfcTreeSnapshot.Of(KfcSkillTree.Read(r, text)));
        // Rewards: the knowledge ids that unlock skill points (Elixir Wells, Shroud Roots); the install has no names for them.
        var rewards = Piece(() => r.First("keen::GameKnowledgeResource")["skillPoints"].Elements
            .Select(p => new SkillReward(p["knowledgeId"]["id"].UInt, (int)p["amount"].Int, "", "")).ToList());
        var locations = text is null ? null : Piece(() => KfcMapLocations.Read(r, text));
        return new KfcSnapshot(items, tree, rewards, caps?.Player, locations);
    }

    /// <summary>One piece of the data, or null when the install's data for it isn't what this reader expects.</summary>
    static T? Piece<T>(Func<T> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or ArgumentException or IOException
            or IndexOutOfRangeException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }
}
