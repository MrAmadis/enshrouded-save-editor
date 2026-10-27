using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>
/// The character's map: <see cref="NoMap"/> when it has no <c>fogofwar</c> section (it hasn't been in a world yet), and
/// <see cref="Unrecognised"/> when the section isn't in the layout the editor knows.
/// </summary>
public enum FogStatus { Fogged, Cleared, NoMap, Unrecognised }

/// <summary>Clears the fog of war (docs/superpowers/specs/2026-10-26-map-fog-and-locations-design.md §7.2).</summary>
public sealed class MapEditor(SaveSession s)
{
    public FogStatus Fog => s.Fog switch
    {
        { IsCleared: true } => FogStatus.Cleared,
        not null => FogStatus.Fogged,
        null when s.File.Find("fogofwar") is null => FogStatus.NoMap,
        null => FogStatus.Unrecognised,
    };

    /// <summary>Why the fog can't be cleared, for the statuses that don't have a map to clear.</summary>
    public static string Explain(FogStatus status) => status switch
    {
        FogStatus.NoMap => "This character hasn't been in a world yet, so it has no map.",
        FogStatus.Unrecognised => "This character's map isn't in a layout the editor knows, so it's left as it is.",
        FogStatus.Cleared => "Already cleared",
        _ => "",
    };

    /// <summary>Reveals the whole map: every cell of the grid is cleared, and its header is kept.</summary>
    public void ClearFog()
    {
        if (s.ReadOnly) throw new EditRejectedException($"This save is read-only: {s.ReadOnlyReason}");
        var status = Fog;
        if (status != FogStatus.Fogged) throw new EditRejectedException(Explain(status));
        s.Fog = s.Fog!.Cleared();
        s.MarkChanged();
    }
}
