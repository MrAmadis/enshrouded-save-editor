namespace EnshroudedSaveEditor.Core.Model;

public enum SkillNodeType { Spark, Attribute, Ability, Passive }

public enum SkillClass { None, Mage, Ranger, Warrior }

/// <summary><paramref name="MaxRank"/> is at least 1.</summary>
public sealed record SkillNode(
    uint Id, string Name, string Description, SkillNodeType Type, SkillClass Class, int Cost, int MaxRank,
    double X, double Y, IReadOnlyList<string> RankValues);

public sealed record ArchetypeLabel(string Name, SkillClass Class, double X, double Y);

public sealed record SkillReward(uint Id, int Points, string Region, string Name);

/// <summary>The skill tree: nodes, undirected links between node ids, and the archetype labels.</summary>
public sealed class SkillGraph
{
    readonly Dictionary<uint, SkillNode> _byId;
    readonly Dictionary<uint, List<SkillNode>> _neighbors = new();

    public IReadOnlyList<SkillNode> Nodes { get; }
    public IReadOnlyList<(uint A, uint B)> Links { get; }
    public IReadOnlyList<ArchetypeLabel> Labels { get; }

    public SkillGraph(IReadOnlyList<SkillNode> nodes, IReadOnlyList<(uint A, uint B)> links, IReadOnlyList<ArchetypeLabel> labels)
    {
        Nodes = nodes; Links = links; Labels = labels;
        _byId = nodes.ToDictionary(n => n.Id);
        foreach (var (a, b) in links)
        {
            if (!_byId.TryGetValue(a, out var na) || !_byId.TryGetValue(b, out var nb))
                throw new ArgumentException($"Link {a}-{b} refers to an unknown node.", nameof(links));
            Add(a, nb); Add(b, na);
        }
    }

    void Add(uint id, SkillNode other)
    {
        if (!_neighbors.TryGetValue(id, out var list)) _neighbors[id] = list = new List<SkillNode>();
        list.Add(other);
    }

    public SkillNode? Find(uint id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<SkillNode> Neighbors(uint id) =>
        _neighbors.TryGetValue(id, out var list) ? list : Array.Empty<SkillNode>();

    public IEnumerable<SkillNode> Sparks => Nodes.Where(n => n.Type == SkillNodeType.Spark);
}
