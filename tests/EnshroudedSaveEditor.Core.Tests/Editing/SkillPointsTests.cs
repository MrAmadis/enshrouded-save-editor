using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class SkillPointsTests : IDisposable
{
    readonly string _tmp = Fixtures.CopyToTemp();
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Theory, InlineData("character0.ksd", 22, 18, 0, 40), InlineData("character1.ksd", 34, 35, 0, 69), InlineData("character2.ksd", 12, 10, 22, 0)]
    public void Points_match_the_saves(string f, int lv, int rw, int sp, int av)
    {
        var game = ReferenceGameData.Load();
        var s = SaveSession.Open(Path.Combine(_tmp, f), Fixtures.ItemIds().Contains);
        var p = SkillPoints.Compute(s.Player.Level, s.Player.Skills, game.Tree, s.Knowledge, game.Rewards);
        Assert.Equal(new PointsSummary(lv, rw, sp, av), p);
    }

    [Fact]
    public void Unknown_nodes_cost_nothing()
    {
        var game = ReferenceGameData.Load();
        var s = SaveSession.Open(Path.Combine(_tmp, "character0.ksd"), Fixtures.ItemIds().Contains);
        var p = SkillPoints.Compute(1, [new(0xDEADBEEF, 5)], game.Tree, s.Knowledge, new Dictionary<uint, EnshroudedSaveEditor.Core.Model.SkillReward>());
        Assert.Equal(new PointsSummary(0, 0, 0, 0), p);
    }
}
