using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class CompositeGameDataTests
{
    /// <summary>Game data whose pieces are given, or missing (they throw <see cref="NotSupportedException"/>).</summary>
    sealed class Partial(string source, SkillGraph? tree = null, int? levelCap = null) : IGameData
    {
        public IItemCatalog Items => throw new NotSupportedException("items");
        public SkillGraph Tree => tree ?? throw new NotSupportedException("skill tree");
        public IReadOnlyDictionary<uint, SkillReward> Rewards => throw new NotSupportedException("rewards");
        public int LevelCap => levelCap ?? throw new NotSupportedException("level cap");
        public IReadOnlyList<uint>? XpPerLevel => [100, 200];
        public string Source => source;
    }

    /// <summary>Every piece of <paramref name="data"/>, under another name.</summary>
    sealed class Named(string source, IGameData data) : IGameData
    {
        public IItemCatalog Items => data.Items;
        public SkillGraph Tree => data.Tree;
        public IReadOnlyDictionary<uint, SkillReward> Rewards => data.Rewards;
        public int LevelCap => data.LevelCap;
        public IReadOnlyList<uint>? XpPerLevel => data.XpPerLevel;
        public string Source => source;
    }

    static readonly SkillGraph SmallTree = new([new SkillNode(7, "Spark", "", SkillNodeType.Spark, SkillClass.None, 0, 1, 0, 0, [])], [], []);

    [Fact]
    public void Each_piece_comes_from_the_primary_when_it_has_it()
    {
        var reference = ReferenceGameData.Load();
        var g = new CompositeGameData(new Partial("Install", SmallTree, 50), reference);
        Assert.Same(SmallTree, g.Tree);
        Assert.Equal(50, g.LevelCap);
        Assert.Equal([100u, 200u], g.XpPerLevel);
        Assert.Same(reference.Items, g.Items);
        Assert.Same(reference.Rewards, g.Rewards);
    }

    [Fact]
    public void Source_lists_where_each_piece_came_from()
    {
        var g = new CompositeGameData(new Partial("Install", SmallTree, 50), new Named("Reference", ReferenceGameData.Load()));
        Assert.Equal("Install: skill tree, level cap, XP table; Reference: items, rewards", g.Source);
    }

    [Fact]
    public void A_primary_with_every_piece_is_named_alone()
    {
        var reference = ReferenceGameData.Load();
        Assert.Equal(reference.Source, new CompositeGameData(reference, new Partial("Reference")).Source);
    }

    [Fact]
    public void A_piece_no_source_knows_is_not_named()
    {
        // The install has no XP table and the reference's is null: the install gives everything there is.
        var reference = ReferenceGameData.Load();
        var g = new CompositeGameData(new AllButXp("Install", reference), reference);
        Assert.Null(g.XpPerLevel);
        Assert.Equal("Install", g.Source);
    }

    /// <summary>Every piece of <paramref name="data"/> but the XP table, which it hasn't.</summary>
    sealed class AllButXp(string source, IGameData data) : IGameData
    {
        public IItemCatalog Items => data.Items;
        public SkillGraph Tree => data.Tree;
        public IReadOnlyDictionary<uint, SkillReward> Rewards => data.Rewards;
        public int LevelCap => data.LevelCap;
        public IReadOnlyList<uint>? XpPerLevel => throw new NotSupportedException("XP table");
        public string Source => source;
    }

    [Fact]
    public void A_piece_neither_has_throws()
    {
        var g = new CompositeGameData(new Partial("A"), new Partial("B"));
        Assert.Throws<NotSupportedException>(() => g.Items);
    }
}
