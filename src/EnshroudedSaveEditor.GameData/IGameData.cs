using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.GameData;

public interface IGameData
{
    IItemCatalog Items { get; }
    SkillGraph Tree { get; }
    IReadOnlyDictionary<uint, SkillReward> Rewards { get; }
    int LevelCap { get; }

    /// <summary><c>XpPerLevel[i]</c> is the XP needed to go from level i+1 to i+2; null when unknown.</summary>
    IReadOnlyList<uint>? XpPerLevel { get; }

    /// <summary>The places the map marks and the knowledge that shows them; only the install has them.</summary>
    IReadOnlyList<MapLocation> Locations => throw new NotSupportedException("map locations");

    string Source { get; }
}
