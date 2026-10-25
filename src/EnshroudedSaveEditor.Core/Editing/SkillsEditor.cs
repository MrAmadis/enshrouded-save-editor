using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Editing;

public enum NodeState { Learned, Available, Locked }

public sealed class SkillsEditor(SaveSession s, SkillGraph tree, IReadOnlyDictionary<uint, SkillReward> rewards)
{
    void EnsureWritable()
    {
        if (s.ReadOnly) throw new EditRejectedException($"This save is read-only: {s.ReadOnlyReason}");
    }

    public IReadOnlySet<uint> Learned =>
        s.Player.Skills.Where(e => e.Rank >= 1).Select(e => e.NodeId).ToHashSet();

    public int RankOf(uint id)
    {
        foreach (var e in s.Player.Skills) if (e.NodeId == id) return e.Rank;
        return 0;
    }

    bool IsConnected(uint id, IReadOnlySet<uint> learned) =>
        tree.Neighbors(id).Any(n => n.Type == SkillNodeType.Spark || learned.Contains(n.Id));

    public NodeState StateOf(uint id)
    {
        var node = tree.Find(id);
        if (node is null) return NodeState.Locked;
        if (node.Type == SkillNodeType.Spark) return NodeState.Learned;
        var learned = Learned;
        if (learned.Contains(id)) return NodeState.Learned;
        return IsConnected(id, learned) ? NodeState.Available : NodeState.Locked;
    }

    public PointsSummary Points() =>
        SkillPoints.Compute(s.Player.Level, s.Player.Skills, tree, s.Knowledge, rewards);

    /// <summary>
    /// Learns the node or ranks it up, under the game's rules: it must link to a learned node or a Spark, and its cost
    /// must fit the available points. There is no unlimited mode: the game resets a tree that spends more than it
    /// earned (session A, T12-R1).
    /// </summary>
    public void Learn(uint id)
    {
        EnsureWritable();
        var node = tree.Find(id) ?? throw new EditRejectedException("Unknown skill.");
        if (node.Type == SkillNodeType.Spark) throw new EditRejectedException("Sparks are always active");
        if (!IsConnected(id, Learned)) throw new EditRejectedException("Connect it to a learned skill first");
        var idx = s.Player.Skills.FindIndex(e => e.NodeId == id);
        var rank = idx >= 0 ? s.Player.Skills[idx].Rank : 0;
        if (rank >= Math.Max(node.MaxRank, 1)) throw new EditRejectedException("Already at max rank");
        if (Points().Available < node.Cost)
            throw new EditRejectedException("Not enough skill points");
        var entry = new SkillEntry(id, (byte)(rank + 1));
        if (idx >= 0) s.Player.Skills[idx] = entry; else s.Player.Skills.Add(entry);
        s.MarkChanged();
    }

    /// <summary>Learned nodes that lose every path to a Spark once <paramref name="id"/> is unlearned.</summary>
    public IReadOnlyList<uint> DependentsOf(uint id)
    {
        var learned = Learned;
        if (!learned.Contains(id)) return [];
        var reached = new HashSet<uint>();
        var queue = new Queue<uint>();
        foreach (var sp in tree.Sparks)
            if (sp.Id != id && reached.Add(sp.Id)) queue.Enqueue(sp.Id);
        while (queue.Count > 0)
            foreach (var n in tree.Neighbors(queue.Dequeue()))
                if (n.Id != id && learned.Contains(n.Id) && reached.Add(n.Id)) queue.Enqueue(n.Id);
        return learned.Where(l => l != id && !reached.Contains(l)).ToList();
    }

    public void Unlearn(uint id)
    {
        EnsureWritable();
        if (tree.Find(id)?.Type == SkillNodeType.Spark) throw new EditRejectedException("Sparks are always active");
        if (!Learned.Contains(id)) throw new EditRejectedException("That skill is not learned.");
        var remove = DependentsOf(id).ToHashSet();
        remove.Add(id);
        s.Player.Skills.RemoveAll(e => e.Rank >= 1 && remove.Contains(e.NodeId));
        s.MarkChanged();
    }

    public void RefundAll()
    {
        EnsureWritable();
        if (s.Player.Skills.RemoveAll(e => e.Rank >= 1) == 0) throw new EditRejectedException("Nothing to refund");
        s.MarkChanged();
    }
}
