using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>
/// The character's map: <see cref="NoMap"/> when it has no <c>fogofwar</c> section (it hasn't been in a world yet), and
/// <see cref="Unrecognised"/> when the section isn't in the layout the editor knows.
/// </summary>
public enum FogStatus { Fogged, Cleared, NoMap, Unrecognised }

/// <summary>
/// Clears the fog of war and discovers the map's locations
/// (docs/superpowers/specs/2026-10-26-map-fog-and-locations-design.md §7.2, §7.3).
/// </summary>
public sealed class MapEditor(SaveSession s)
{
    public FogStatus Fog => s.Fog switch
    {
        { IsCleared: true } => FogStatus.Cleared,
        not null => FogStatus.Fogged,
        null when s.File.Find("fogofwar") is null => FogStatus.NoMap,
        null => FogStatus.Unrecognised,
    };

    /// <summary>The character has a <c>player_knowledge</c> section to learn locations into; one that has never been in a world has none.</summary>
    public bool HasKnowledge => s.File.Find("player_knowledge") is not null;

    /// <summary>Why the fog can't be cleared, for the statuses that don't have a map to clear.</summary>
    public static string Explain(FogStatus status) => status switch
    {
        FogStatus.NoMap => "This character hasn't been in a world yet, so it has no map.",
        FogStatus.Unrecognised => "This character's map isn't in a layout the editor knows, so it's left as it is.",
        FogStatus.Cleared => "Already cleared",
        _ => "",
    };

    void EnsureWritable()
    {
        if (s.ReadOnly) throw new EditRejectedException($"This save is read-only: {s.ReadOnlyReason}");
    }

    /// <summary>Reveals the whole map: every cell of the grid is cleared, and its header is kept.</summary>
    public void ClearFog()
    {
        EnsureWritable();
        var status = Fog;
        if (status != FogStatus.Fogged) throw new EditRejectedException(Explain(status));
        s.Fog = s.Fog!.Cleared();
        s.MarkChanged();
    }

    /// <summary>How many of <paramref name="locations"/> the character has discovered (knows every id of), of how many.</summary>
    public (int Discovered, int Total) Discovered(IReadOnlyList<MapLocation> locations) =>
        (locations.Count(l => l.Knowledge.All(s.Knowledge.Contains)), locations.Count);

    /// <summary>Learns every id of <paramref name="locations"/> the character doesn't know; returns how many locations that discovered.</summary>
    public int DiscoverLocations(IReadOnlyList<MapLocation> locations)
    {
        EnsureWritable();
        if (!HasKnowledge) throw new EditRejectedException(Explain(FogStatus.NoMap));
        var (discovered, total) = Discovered(locations);
        if (discovered == total) throw new EditRejectedException("Already discovered");
        s.Knowledge = s.Knowledge.Learn(locations.SelectMany(l => l.Knowledge));
        s.MarkChanged();
        return total - discovered;
    }
}
