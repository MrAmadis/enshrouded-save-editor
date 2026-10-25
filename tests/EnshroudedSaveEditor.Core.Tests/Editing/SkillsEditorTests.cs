using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class SkillsEditorTests : IDisposable
{
    sealed class FakeProbe : IProcessProbe
    {
        public bool IsGameRunning() => false;
        public bool IsSteamRunning() => false;
    }

    readonly string _tmp = Fixtures.CopyToTemp();
    readonly string _backupRoot = Path.Combine(Path.GetTempPath(), "enshrouded-backups-" + Guid.NewGuid().ToString("N"));
    readonly ReferenceGameData _game = ReferenceGameData.Load();
    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
        try { Directory.Delete(_backupRoot, true); } catch { }
    }

    SaveSession Open(string f) => SaveSession.Open(Path.Combine(_tmp, f), Fixtures.ItemIds().Contains);
    SkillsEditor Ed(SaveSession s) => new(s, _game.Tree, _game.Rewards);

    [Fact]
    public void Character2_has_15_learned_and_no_points()
    {
        var e = Ed(Open("character2.ksd"));
        Assert.Equal(15, e.Learned.Count);
        Assert.Equal(0, e.Points().Available);
    }

    [Fact]
    public void Learn_requires_a_connection_in_both_modes()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        var locked = _game.Tree.Nodes.First(n => n.Type != SkillNodeType.Spark && e.StateOf(n.Id) == NodeState.Locked);
        foreach (var mode in new[] { PointsMode.GameRules, PointsMode.Unlimited })
        {
            var ex = Assert.Throws<EditRejectedException>(() => e.Learn(locked.Id, mode));
            Assert.Contains("Connect it to a learned skill first", ex.Message);
        }
        Assert.Equal(0, s.ChangeCount);
        Assert.Empty(s.Player.Skills);
    }

    [Fact]
    public void Sparks_cannot_be_learned_and_are_always_learned()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        var spark = _game.Tree.Sparks.First();
        Assert.Equal(NodeState.Learned, e.StateOf(spark.Id));
        var ex = Assert.Throws<EditRejectedException>(() => e.Learn(spark.Id, PointsMode.Unlimited));
        Assert.Contains("Sparks are always active", ex.Message);
        Assert.Equal(NodeState.Locked, e.StateOf(0xDEADBEEF));
    }

    [Fact]
    public void GameRules_blocks_overspend_Unlimited_allows_it()
    {
        var s = Open("character2.ksd"); var e = Ed(s);
        var avail = _game.Tree.Nodes.First(n => n.Type != SkillNodeType.Spark && n.Cost >= 1 && e.StateOf(n.Id) == NodeState.Available);
        var ex = Assert.Throws<EditRejectedException>(() => e.Learn(avail.Id, PointsMode.GameRules));
        Assert.Contains("Not enough skill points", ex.Message);
        Assert.Equal(0, s.ChangeCount);
        e.Learn(avail.Id, PointsMode.Unlimited);
        Assert.True(e.Points().Available < 0);
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void GameRules_learn_spends_cost()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        var avail = _game.Tree.Nodes.First(n => n.Type != SkillNodeType.Spark && e.StateOf(n.Id) == NodeState.Available);
        e.Learn(avail.Id, PointsMode.GameRules);
        Assert.Equal(69 - avail.Cost, e.Points().Available);
        Assert.Equal(1, e.RankOf(avail.Id));
    }

    [Fact]
    public void Learning_a_rank0_entry_sets_rank_in_place()
    {
        var s = Open("character2.ksd"); var e = Ed(s);
        var idx = s.Player.Skills.FindIndex(x => x.Rank == 0 && e.StateOf(x.NodeId) == NodeState.Available);
        Assert.True(idx >= 0);
        var id = s.Player.Skills[idx].NodeId;
        e.Learn(id, PointsMode.Unlimited);
        Assert.Equal(30, s.Player.Skills.Count);
        Assert.Equal(new SkillEntry(id, 1), s.Player.Skills[idx]);
    }

    [Fact]
    public void Rank_up_until_max()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        var n = _game.Tree.Nodes.First(x => x.MaxRank == 3 && e.StateOf(x.Id) == NodeState.Available);
        for (var i = 0; i < 3; i++) e.Learn(n.Id, PointsMode.Unlimited);
        Assert.Equal(3, e.RankOf(n.Id));
        var ex = Assert.Throws<EditRejectedException>(() => e.Learn(n.Id, PointsMode.Unlimited));
        Assert.Contains("Already at max rank", ex.Message);
        Assert.Equal(3, s.ChangeCount);
    }

    (SaveSession S, SkillsEditor E, uint A, uint B, uint C) Chain()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        var t = _game.Tree;
        bool Plain(SkillNode n) => n.Type != SkillNodeType.Spark;
        foreach (var spark in t.Sparks)
            foreach (var a in t.Neighbors(spark.Id).Where(Plain))
                foreach (var b in t.Neighbors(a.Id).Where(x => Plain(x) && t.Neighbors(x.Id).All(Plain)))
                    foreach (var c in t.Neighbors(b.Id).Where(x => Plain(x) && x.Id != a.Id && t.Neighbors(x.Id).All(y => Plain(y) && y.Id != a.Id)))
                        return (s, e, a.Id, b.Id, c.Id);
        throw new InvalidOperationException("no chain found");
    }

    [Fact]
    public void Unlearn_cascades_to_disconnected_nodes()
    {
        var (s, e, a, b, c) = Chain();
        e.Learn(a, PointsMode.Unlimited); e.Learn(b, PointsMode.Unlimited); e.Learn(c, PointsMode.Unlimited);
        Assert.Equal(new HashSet<uint> { b, c }, e.DependentsOf(a).ToHashSet());
        var before = s.ChangeCount;
        e.Unlearn(a);
        Assert.Empty(e.Learned);
        Assert.Empty(s.Player.Skills);
        Assert.Equal(before + 1, s.ChangeCount);
    }

    [Fact]
    public void Unlearn_of_unlearned_node_is_rejected()
    {
        var s = Open("character1.ksd"); var e = Ed(s);
        Assert.Throws<EditRejectedException>(() => e.Unlearn(_game.Tree.Nodes.First(n => n.Type != SkillNodeType.Spark).Id));
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void RefundAll_keeps_only_rank0_entries()
    {
        var s = Open("character2.ksd"); var e = Ed(s);
        e.RefundAll();
        Assert.Empty(e.Learned);
        Assert.Equal(15, s.Player.Skills.Count);
        Assert.All(s.Player.Skills, x => Assert.Equal(0, x.Rank));
        Assert.Equal(22, e.Points().Available);
        Assert.Equal(1, s.ChangeCount);
        Assert.Throws<EditRejectedException>(() => e.RefundAll());
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void Edited_skills_survive_save_and_reload()
    {
        var s = Open("character2.ksd"); var e = Ed(s);
        e.RefundAll();
        var expected = s.Player.Skills.ToList();
        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(_backupRoot), new FakeProbe()));
        Assert.Equal(expected, Open("character2.ksd").Player.Skills);
    }
}
