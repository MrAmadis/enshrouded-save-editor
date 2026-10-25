using System.Globalization;
using System.Text.Json;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

/// <summary>Game data from the embedded reference files (itemdb2.json, progress.json; see THIRD_PARTY.md).</summary>
public sealed class ReferenceGameData : IGameData
{
    static readonly Lazy<ReferenceGameData> Instance = new(Build);

    public IItemCatalog Items { get; }
    public SkillGraph Tree { get; }
    public IReadOnlyDictionary<uint, SkillReward> Rewards { get; }
    public int LevelCap => 50;
    public IReadOnlyList<uint>? XpPerLevel => null;
    public string Source => "Reference data (Kimikozi, build of 2026-10-15)";

    ReferenceGameData(IItemCatalog items, SkillGraph tree, IReadOnlyDictionary<uint, SkillReward> rewards)
    {
        Items = items; Tree = tree; Rewards = rewards;
    }

    /// <summary>Returns the shared, immutable instance, parsed on first use.</summary>
    public static ReferenceGameData Load() => Instance.Value;

    static ReferenceGameData Build()
    {
        using var items = Open("Reference.itemdb2.json");
        using var progress = Open("Reference.progress.json");
        var (tree, rewards) = ParseProgress(progress.RootElement);
        return new ReferenceGameData(ParseItems(items.RootElement), tree, rewards);
    }

    static JsonDocument Open(string resource)
    {
        using var s = typeof(ReferenceGameData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource {resource} is missing.");
        return JsonDocument.Parse(s);
    }

    static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

    static ItemCatalog ParseItems(JsonElement root)
    {
        var cats = Strings(root.GetProperty("cats"));
        var slots = Strings(root.GetProperty("slots"));
        var wcts = Strings(root.GetProperty("wcts"));
        var list = new List<ItemInfo>();
        foreach (var p in root.GetProperty("items").EnumerateObject())
        {
            var r = p.Value;
            var wt = wcts[r[11].GetInt32()];
            list.Add(new ItemInfo(
                uint.Parse(p.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                r[0].GetString() ?? "",
                Enum.Parse<ItemCategory>(cats[r[1].GetInt32()]),
                (Rarity)(r[2].GetInt32() + 1),
                r[3].GetInt32(), r[4].GetInt32(), r[5].GetInt32(),
                Slot(slots[r[6].GetInt32()]),
                wt.Length == 0 ? null : wt,
                r[7].GetString() ?? "",
                r[12].GetString() ?? "",
                r[8].GetInt32(),
                r[9].GetInt32() != 0));
        }
        return new ItemCatalog(list);
    }

    static EquipSlot Slot(string s) => s.Length == 0 ? EquipSlot.None : Enum.Parse<EquipSlot>(s, ignoreCase: true);

    static SkillClass Class(string? c) => c switch
    {
        "M" => SkillClass.Mage, "R" => SkillClass.Ranger, "W" => SkillClass.Warrior, _ => SkillClass.None,
    };

    static (SkillGraph, IReadOnlyDictionary<uint, SkillReward>) ParseProgress(JsonElement root)
    {
        var nodes = root.GetProperty("nodes").EnumerateArray().Select(n =>
        {
            var max = n.GetProperty("max").GetInt32();
            return new SkillNode(
                n.GetProperty("id").GetUInt32(),
                n.GetProperty("n").GetString() ?? "",
                n.GetProperty("d").GetString() ?? "",
                n.GetProperty("t").GetString() switch
                {
                    "R" => SkillNodeType.Spark, "T" => SkillNodeType.Attribute, "A" => SkillNodeType.Ability,
                    "P" => SkillNodeType.Passive,
                    var t => throw new InvalidOperationException($"Unknown skill node type '{t}'."),
                },
                Class(n.GetProperty("c").GetString()),
                n.GetProperty("cost").GetInt32(),
                max == 0 ? 1 : max,
                n.GetProperty("x").GetDouble(), n.GetProperty("y").GetDouble(),
                n.GetProperty("lv").EnumerateArray().Select(v => v.ToString()).ToArray());
        }).ToList();
        var links = root.GetProperty("links").EnumerateArray()
            .Select(l => (nodes[l[0].GetInt32()].Id, nodes[l[1].GetInt32()].Id)).ToList();
        var labels = root.GetProperty("labels").EnumerateArray().Select(l => new ArchetypeLabel(
            l.GetProperty("n").GetString() ?? "", Class(l.GetProperty("cls").GetString()),
            l.GetProperty("x").GetDouble(), l.GetProperty("y").GetDouble())).ToList();
        var rewards = root.GetProperty("rewards").EnumerateArray().Select(r => new SkillReward(
            r.GetProperty("id").GetUInt32(), r.GetProperty("a").GetInt32(),
            r.GetProperty("g").GetString() ?? "", r.GetProperty("n").GetString() ?? ""))
            .ToDictionary(r => r.Id);
        return (new SkillGraph(nodes, links, labels), rewards);
    }

    sealed class ItemCatalog(IReadOnlyList<ItemInfo> all) : IItemCatalog
    {
        readonly Dictionary<uint, ItemInfo> _byId = all.ToDictionary(i => i.Id);
        public IReadOnlyList<ItemInfo> All { get; } = all;
        public ItemInfo? Find(uint id) => _byId.GetValueOrDefault(id);
    }
}
