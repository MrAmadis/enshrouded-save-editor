namespace EnshroudedSaveEditor.GameData;

/// <summary>An icon's texels: 8-bit RGBA, row by row from the top left.</summary>
public sealed record IconTexture(int Width, int Height, byte[] Rgba);

/// <summary>
/// Where <see cref="IconService"/> gets the game's own icons. Each method returns null when the id has no icon or its
/// texture can't be read; it doesn't throw for that.
/// </summary>
public interface IIconTextureSource
{
    /// <summary>
    /// Changes whenever the textures may have changed (another game build); icons cached for another version are dropped.
    /// </summary>
    string Version { get; }

    IconTexture? ItemTexture(uint id);

    IconTexture? SkillTexture(uint nodeId);
}
