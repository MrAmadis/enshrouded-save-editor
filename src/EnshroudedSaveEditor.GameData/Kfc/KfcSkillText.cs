using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// Fills a skill description's <c>%k</c> placeholders, in order, from the tag's arguments: a config value of the node
/// (per rank "10% / 20% / 30% (by level)" when it scales), a balancing value, or the name of an input action. A
/// placeholder that can't be filled shows "?". See docs/research/kfc-format.md, "Skill text".
/// </summary>
internal sealed partial class KfcSkillText
{
    [GeneratedRegex(@"\G [A-Za-z]+")]
    private static partial Regex NextWord();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordStart();

    readonly KfcText _text;
    readonly KfcValue _balancing;
    readonly Dictionary<long, string> _balancingFields = new();
    readonly Dictionary<long, string> _inputs = new();

    public KfcSkillText(KfcResources r, KfcText text)
    {
        _text = text;
        _balancing = r.First("keen::BalancingTable");
        foreach (var (name, value) in r.Types["keen::BalancingLocaIds"].EnumValues)
            if (_balancing.Type.Field("player" + name) is { } field) _balancingFields[(long)value] = field.Name;

        // Input actions are named by the UI's gameplay action labels: the field named like the action, else one ending
        // with its name, else the action's name split into words.
        var labels = r.First("keen::FbUiBundle")["locatags"]["gameplayActions"];
        var fields = labels.Type.Fields;
        foreach (var (name, value) in r.Types["keen::input_mapping::runtime::GameplayAction"].EnumValues)
        {
            var field = fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? fields.FirstOrDefault(f => f.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            var label = field is null ? "" : text[labels[field.Name]];
            _inputs.TryAdd((long)value, label.Length > 0 ? label : WordStart().Replace(name.Replace('_', ' '), " "));
        }
    }

    /// <summary>The node's description with its values, and the values per rank of its first scaling placeholder.</summary>
    public (string Description, IReadOnlyList<string> RankValues) Describe(KfcValue node)
    {
        var id = node["description"].UInt;
        var text = _text[id];
        var arguments = _text.Arguments(id);
        var ranks = Math.Max(1, (int)node["maxLevel"].Int);
        var perLevelUnit = PerLevelUnit(_text[node["perLevelEffectDescription"].UInt]);
        var configs = new Dictionary<uint, KfcValue>();
        foreach (var list in new[] { "simple", "scaled" })
            foreach (var c in node["configValues"][list].Elements)
                if (c.Value is { } config && config.Get("configId") is { } configId) configs.TryAdd(configId.UInt, config);

        var result = new StringBuilder();
        IReadOnlyList<string> rankValues = [];
        var next = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var marker = c == '%' && i + 1 < text.Length ? text[i + 1] : '\0';
            if (marker == '%') { result.Append('%'); i++; continue; }
            if (marker != 'k') { result.Append(c); continue; }
            i++;
            if ((next < arguments.Count ? Value(arguments[next++], configs, ranks) : null) is not { } value)
            {
                result.Append('?');
                continue;
            }
            var (numbers, unit) = value;
            if (numbers.Count == 1) { result.Append(numbers[0]).Append(unit); continue; }
            // "(by level)" follows a percentage, and the unit of another number: the per-level text's, else the next word.
            if (unit.Length == 0 && !numbers[0].EndsWith('%'))
            {
                unit = perLevelUnit.Length > 0 && text.AsSpan(i + 1).StartsWith(perLevelUnit) ? perLevelUnit : NextWord().Match(text, i + 1).Value;
                i += unit.Length;
            }
            if (rankValues.Count == 0) rankValues = numbers.Select(n => n + unit).ToList();
            result.Append(string.Join(" / ", numbers)).Append(unit).Append(" (by level)");
        }
        return (result.ToString(), rankValues);
    }

    /// <summary>The words a node's per-level text puts after its value: " minutes" for "%k minutes per level".</summary>
    static string PerLevelUnit(string perLevel)
    {
        var at = perLevel.IndexOf("%k", StringComparison.Ordinal);
        var end = perLevel.LastIndexOf(" per level", StringComparison.Ordinal);
        return at >= 0 && end > at + 2 ? perLevel[(at + 2)..end] : "";
    }

    /// <summary>A placeholder's numbers (one, or one per rank) and the unit after them; null when unknown.</summary>
    (List<string> Numbers, string Unit)? Value(KfcTextArgument argument, Dictionary<uint, KfcValue> configs, int ranks)
    {
        switch (argument.Kind)
        {
            case "Input":
                return _inputs.TryGetValue(argument.Id, out var input) ? ([input], "") : null;
            case "Balancing":
                return _balancingFields.TryGetValue(argument.Id, out var field) ? ([Number(_balancing[field].Int)], "") : null;
            case "Config" when configs.TryGetValue(argument.Id, out var c):
                var format = c.Get("valueFormat")?.EnumName ?? "Normal";
                var value = Amount(c["value"]);
                List<double> values;
                if (c.Get("scaleFactor") is not { } scale) values = [value];
                else if (c["function"].EnumName == "Linear") values = Enumerable.Range(1, ranks).Select(r => value + scale.Float * r).ToList();
                else return null;
                return Format(values, format);
            default:
                return null;
        }
    }

    /// <summary>A config's number; a <c>keen::Time</c> (nanoseconds) in seconds.</summary>
    static double Amount(KfcValue v) => v.Type.Kind switch
    {
        KfcKind.Float32 or KfcKind.Float64 => v.Float,
        KfcKind.Struct => v.Int / 1e9,
        _ => v.Int,
    };

    /// <summary>Numbers in the config's format; durations in seconds, or minutes when they are all whole minutes.</summary>
    static (List<string>, string) Format(List<double> values, string format)
    {
        switch (format)
        {
            case "Percentage":
                return (values.Select(v => Number(v * 100) + "%").ToList(), "");
            case "Duration" or "DurationMilliseconds":
                var seconds = format == "Duration" ? values : values.Select(v => v / 1000).ToList();
                var minutes = seconds.All(s => s >= 60 && s % 60 == 0);
                var shown = seconds.Select(s => minutes ? s / 60 : s).ToList();
                var unit = (minutes ? " minute" : " second") + (shown is [1] ? "" : "s");
                return (shown.Select(Number).ToList(), unit);
            default:
                return (values.Select(Number).ToList(), "");
        }
    }

    static string Number(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
