using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// Reads the skill tree (<c>keen::SkillTreeResource</c>: nodes, positions, links) and its archetype labels (the skill tree
/// screen's class hints).
/// </summary>
internal static class KfcSkillTree
{
    public static SkillGraph Read(KfcResources r, KfcText text)
    {
        var tree = r.First("keen::SkillTreeResource");
        var skillText = new KfcSkillText(r, text);
        var nodes = new List<SkillNode>();
        foreach (var n in tree["nodes"].Elements)
        {
            var position = n["uiPosition"];
            var max = (int)n["maxLevel"].Int;
            var (description, rankValues) = skillText.Describe(n);
            nodes.Add(new SkillNode(
                n["id"].UInt,
                text[n["name"].UInt],
                description,
                n["type"].EnumName switch
                {
                    "Root" => SkillNodeType.Spark, "Attribute" => SkillNodeType.Attribute,
                    "ActiveSkill" => SkillNodeType.Ability, "PassiveSkill" => SkillNodeType.Passive,
                    var t => throw new InvalidDataException($"Unknown skill node type {t}."),
                },
                n["classAlignment"].EnumName switch
                {
                    "Mage" => SkillClass.Mage, "Ranger" => SkillClass.Ranger, "Warrior" => SkillClass.Warrior, _ => SkillClass.None,
                },
                (int)n["costs"].Int,
                max == 0 ? 1 : max,
                Math.Round(position["x"].Float, 1),
                Math.Round(position["y"].Float, 1),
                rankValues));
        }
        var links = tree["links"].Elements.Select(l => (l["sourceNode"].UInt, l["targetNode"].UInt)).ToList();
        return new SkillGraph(nodes, links, Labels(r, text, nodes));
    }

    /// <summary>
    /// Each class hint is drawn at <c>centerRingRadius + distanceFromRing</c> from the tree's centre (the middle of the
    /// Sparks) towards <c>directionDegrees</c>; its class is that of the nearest class node.
    /// </summary>
    static List<ArchetypeLabel> Labels(KfcResources r, KfcText text, List<SkillNode> nodes)
    {
        var screen = r.First("keen::FbUiBundle")["menu"]["skilltree"];
        var sparks = nodes.Where(n => n.Type == SkillNodeType.Spark).ToList();
        if (sparks.Count == 0) return [];
        double cx = sparks.Average(s => s.X), cy = sparks.Average(s => s.Y), ring = screen["centerRingRadius"].Float;
        var classed = nodes.Where(n => n.Class != SkillClass.None).ToList();
        var labels = new List<ArchetypeLabel>();
        foreach (var hint in screen["classHints"].Elements)
        {
            var angle = hint["directionDegrees"].Int * Math.PI / 180;
            var distance = ring + hint["distanceFromRing"].Float;
            double x = Math.Round(cx + distance * Math.Cos(angle)), y = Math.Round(cy + distance * Math.Sin(angle));
            var nearest = classed.MinBy(n => (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y));
            labels.Add(new ArchetypeLabel(text[hint["label"]].ToUpperInvariant(), nearest?.Class ?? SkillClass.None, x, y));
        }
        return labels;
    }
}
