using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// The map's locations (docs/superpowers/specs/2026-10-26-map-fog-and-locations-design.md §6.2): every marker template
/// (<c>keen::ecs::MapMarker</c>) whose unlock rules show it through one Bool of player knowledge (a hint or visible state
/// of type <c>SimpleBool</c>, equals 1), with those ids. A marker that also uses an id some marker completes with (a Gem
/// Forge's hint is a Night Temple's completed state), or a skill-point reward id, is left out: discovering never
/// completes a place or grants points.
/// </summary>
internal static class KfcMapLocations
{
    public static IReadOnlyList<MapLocation> Read(KfcResources r, KfcText text)
    {
        var knowledge = r.First("keen::GameKnowledgeResource");
        var bools = knowledge["playerKnowledge"]["values"].Elements
            .Where(v => v["type"].EnumName == "Bool")
            .Select(v => v["id"]["id"].UInt)
            .ToHashSet();
        var completed = knowledge["skillPoints"].Elements.Select(p => p["knowledgeId"]["id"].UInt).ToHashSet();

        var shown = new List<MapLocation>();
        foreach (var t in r.AllInFileOrder("keen::ecs::TemplateResource"))
        {
            KfcValue? marker = null, rules = null, loca = null;
            foreach (var c in t["components"].Elements)
                if (c.Value is { } v)
                    switch (v.Type.QualifiedName)
                    {
                        case "keen::ecs::MapMarker": marker = v; break;
                        case "keen::ecs::RuntimeMapMarkerUnlockRequirements": rules = v; break;
                        case "keen::ecs::LocaTagComponent": loca = v; break;
                    }
            if (marker is not { } m || rules is not { } u) continue;

            if (u["completedStateQuery"]["knowledgeOrQueryId"].UInt is var done and not 0) completed.Add(done);
            var ids = new[] { u["hintStateQuery"], u["visibleStateQuery"] }
                .Where(q => q["type"].EnumName == "SimpleBool" && q["compareOperator"].EnumName == "Equals" && q["compareValue"].UInt == 1)
                .Select(q => q["knowledgeOrQueryId"].UInt)
                .Where(bools.Contains)
                .Distinct()
                .ToArray();
            if (ids.Length == 0) continue;

            var name = text[m["markerId"]];
            if (name.Length == 0 && loca is { } l) name = text[l["locaTag"]];
            shown.Add(new MapLocation(name.Length > 0 ? name : t["name"].String, ids));
        }
        return shown.Where(l => !l.Knowledge.Any(completed.Contains)).ToList();
    }
}
