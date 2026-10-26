using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

/// <summary>
/// Game data taken piece by piece: each piece from <c>primary</c>, or from <c>fallback</c> when the primary throws
/// <see cref="NotSupportedException"/> for it. A piece neither has throws <see cref="NotSupportedException"/>.
/// </summary>
public sealed class CompositeGameData : IGameData
{
    readonly record struct Piece<T>(string Name, T Value, IGameData? From)
    {
        public T Get() => From is not null ? Value : throw new NotSupportedException(Name);
    }

    readonly Piece<IItemCatalog> _items;
    readonly Piece<SkillGraph> _tree;
    readonly Piece<IReadOnlyDictionary<uint, SkillReward>> _rewards;
    readonly Piece<int> _levelCap;
    readonly Piece<IReadOnlyList<uint>?> _xp;

    public CompositeGameData(IGameData primary, IGameData fallback)
    {
        Piece<T> Take<T>(string name, Func<IGameData, T> get)
        {
            foreach (var g in new[] { primary, fallback })
            {
                try
                {
                    return new Piece<T>(name, get(g), g);
                }
                catch (NotSupportedException) { }
            }
            return new Piece<T>(name, default!, null);
        }

        _items = Take("items", g => g.Items);
        _tree = Take("skill tree", g => g.Tree);
        _rewards = Take("rewards", g => g.Rewards);
        _levelCap = Take("level cap", g => g.LevelCap);
        _xp = Take("XP table", g => g.XpPerLevel);

        var pieces = new (string Name, IGameData? From)[]
        {
            (_items.Name, _items.From), (_tree.Name, _tree.From), (_rewards.Name, _rewards.From),
            (_levelCap.Name, _levelCap.From), (_xp.Name, _xp.From),
        };
        var groups = new[] { primary, fallback }
            .Select(g => (g.Source, Names: pieces.Where(p => p.From == g).Select(p => p.Name).ToList()))
            .Where(x => x.Names.Count > 0)
            .ToList();
        Source = groups is [var only] && only.Names.Count == pieces.Length
            ? only.Source
            : string.Join("; ", groups.Select(x => $"{x.Source}: {string.Join(", ", x.Names)}"));
    }

    public IItemCatalog Items => _items.Get();
    public SkillGraph Tree => _tree.Get();
    public IReadOnlyDictionary<uint, SkillReward> Rewards => _rewards.Get();
    public int LevelCap => _levelCap.Get();
    public IReadOnlyList<uint>? XpPerLevel => _xp.Get();

    /// <summary>The source of every piece, or which pieces came from where ("Your Enshrouded install: items, …; Reference data (…): XP table").</summary>
    public string Source { get; }
}
