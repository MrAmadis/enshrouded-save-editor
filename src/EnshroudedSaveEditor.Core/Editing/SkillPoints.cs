using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Editing;

public sealed record PointsSummary(int FromLevels, int FromRewards, int Spent, int Available);

public static class SkillPoints
{
    /// <summary>Two points per level after the first, plus collected rewards, minus Σ cost × rank. Unknown nodes cost 0.</summary>
    public static PointsSummary Compute(uint level, IEnumerable<SkillEntry> skills, SkillGraph tree,
        PlayerKnowledge knowledge, IReadOnlyDictionary<uint, SkillReward> rewards)
    {
        var fromLevels = 2 * ((int)Math.Max(level, 1u) - 1);
        var fromRewards = 0;
        foreach (var (id, reward) in rewards)
            if (knowledge.Contains(id)) fromRewards += reward.Points;
        var spent = 0;
        foreach (var e in skills)
            spent += (tree.Find(e.NodeId)?.Cost ?? 0) * e.Rank;
        return new PointsSummary(fromLevels, fromRewards, spent, fromLevels + fromRewards - spent);
    }
}
