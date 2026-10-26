using System.Text.RegularExpressions;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>What fills a <c>%k</c> placeholder: a config value, an input action, a balancing value or a generic one.</summary>
internal readonly record struct KfcTextArgument(uint Id, string Kind);

/// <summary>
/// The game's US English text by loca tag id, as plain text: markup such as <c>&lt;lorename&gt;</c> removed, spaces
/// collapsed, one line break between non-empty lines. A tag reference (a GUID) has the id FNV-1a of its 16 bytes; the skill
/// tree stores the ids directly. Placeholders (<c>%k</c>, one per argument) are left in.
/// </summary>
internal sealed partial class KfcText
{
    [GeneratedRegex("<[^<>]*>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Space();

    readonly Dictionary<uint, (string Text, KfcTextArgument[] Arguments)> _tags;

    KfcText(Dictionary<uint, (string, KfcTextArgument[])> tags) => _tags = tags;

    public static KfcText Load(KfcResources r)
    {
        var collection = r.First("keen::LocaTagCollectionResource");
        var hash = collection["keenglishData"]["hash"];
        foreach (var language in collection["languages"].Elements)
            if (language["language"].EnumName == "En_Us") hash = language["data"]["hash"];
        var data = r.Content(hash, "keen::LocaTagCollectionResourceData")
            ?? throw new InvalidDataException("The install's English text is missing.");
        var tags = new Dictionary<uint, (string, KfcTextArgument[])>();
        foreach (var tag in data["tags"].Elements)
            tags[tag["id"].UInt] = (Plain(tag["text"].String),
                tag["arguments"].Elements.Select(a => new KfcTextArgument(a["id"].UInt, a["type"].EnumName)).ToArray());
        return new KfcText(tags);
    }

    static string Plain(string s) =>
        string.Join("\n", Markup().Replace(s, "").Split('\n').Select(l => Space().Replace(l, " ").Trim()).Where(l => l.Length > 0));

    /// <summary>The text with loca tag id <paramref name="id"/>; empty when there is none.</summary>
    public string this[uint id] => _tags.TryGetValue(id, out var t) ? t.Text : "";

    /// <summary>The text a loca tag reference names, on one line; empty when there is none.</summary>
    public string this[KfcValue reference]
    {
        get
        {
            var guid = reference.Bytes16;
            return guid.All(b => b == 0) ? "" : this[KfcTypes.Fnv1a(guid)].Replace('\n', ' ');
        }
    }

    /// <summary>The arguments of the tag's placeholders, in order.</summary>
    public IReadOnlyList<KfcTextArgument> Arguments(uint id) => _tags.TryGetValue(id, out var t) ? t.Arguments : [];
}
