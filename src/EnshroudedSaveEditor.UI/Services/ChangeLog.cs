using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>
/// The unsaved edits as the bottom bar lists them: one human line per edit ("Copper Ore 12 → 37", "Added Hunting Bow",
/// "Level 7 → 10"). Undo drops the lines of the edits it reverts; a save or reload clears it.
/// </summary>
public sealed class ChangeLog
{
    public const string RefundedAll = "Refunded all skills";

    public const string ClearedFog = "Cleared fog of war";

    private readonly List<string> _lines = [];

    public int Count => _lines.Count;

    /// <summary>A copy of the lines, oldest first.</summary>
    public IReadOnlyList<string> Lines => _lines.ToArray();

    public void Add(string line) => _lines.Add(line);

    /// <summary>Keeps the first <paramref name="count"/> lines.</summary>
    public void TruncateTo(int count)
    {
        if (count < _lines.Count)
            _lines.RemoveRange(count, _lines.Count - count);
    }

    public void Clear() => _lines.Clear();

    public static string Stack(string item, int from, int to) => $"{item} {from} → {to}";

    public static string ItemLevel(string item, int from, int to) => $"{item} level {from} → {to}";

    public static string ItemRarity(string item, Rarity from, Rarity to) => $"{item} {from} → {to}";

    public static string Durability(string item, (uint Current, uint Max) from, (uint Current, uint Max) to) =>
        $"{item} durability {from.Current}/{from.Max} → {to.Current}/{to.Max}";

    /// <summary>"Added Hunting Bow", or "Added Copper Ore ×10" for more than one (R7).</summary>
    public static string Added(string item, int count) => $"Added {item}{Times(count)}";

    public static string Removed(string item, int count) => $"Removed {item}{Times(count)}";

    public static string Moved(string item) => $"Moved {item}";

    public static string Swapped(string item, string other) => $"Swapped {item} and {other}";

    /// <summary>"Learned Blink", or "Learned Blink (rank 2)" for a rank-up.</summary>
    public static string Learned(string skill, int rank) => rank > 1 ? $"Learned {skill} (rank {rank})" : $"Learned {skill}";

    /// <summary>"Unlearned Blink", or "Unlearned Blink (+2 more)" when its dependents went with it.</summary>
    public static string Unlearned(string skill, int dependents) =>
        dependents > 0 ? $"Unlearned {skill} (+{dependents} more)" : $"Unlearned {skill}";

    public static string Level(uint from, int to) => $"Level {from} → {to}";

    public static string Discovered(int locations) => locations == 1 ? "Discovered 1 location" : $"Discovered {locations} locations";

    public static string Xp(uint from, long to) => $"XP {from} → {to}";

    public static string Name(string from, string to) => $"Name {from} → {to}";

    private static string Times(int count) => count > 1 ? $" ×{count}" : "";
}
