namespace EnshroudedSaveEditor.Core.Model;

/// <summary>
/// A place the map marks, and the player-knowledge ids that show it: its hint and visible states, never its completed one
/// (docs/superpowers/specs/2026-10-26-map-fog-and-locations-design.md §6.2). It counts as discovered when the character
/// knows every id.
/// </summary>
public sealed record MapLocation(string Name, IReadOnlyList<uint> Knowledge);
