using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>
/// The game data the editor runs on, loaded once on first use: the embedded reference data now, a composite with the
/// install reader later (Task 19).
/// </summary>
public sealed class GameDataProvider(Func<IGameData> load)
{
    private readonly Lazy<IGameData> _data = new(load ?? throw new ArgumentNullException(nameof(load)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public IGameData Data => _data.Value;

    /// <summary>Where the data comes from, for the user ("Reference data (…)").</summary>
    public string Source => Data.Source;
}
