using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.GameData.Install;
using EnshroudedSaveEditor.GameData.Kfc;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>
/// The game data the editor runs on, loaded once on first use: in the app, <see cref="Load"/> (the install's data piece by
/// piece, with the embedded reference data as fallback).
/// </summary>
public sealed class GameDataProvider(Func<IGameData> load)
{
    private readonly Lazy<IGameData> _data = new(load ?? throw new ArgumentNullException(nameof(load)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public IGameData Data => _data.Value;

    /// <summary>Where the data comes from, for the user ("Reference data (…)").</summary>
    public string Source => Data.Source;

    /// <summary>
    /// The install's data over the reference data (<see cref="CompositeGameData"/>), cached in <paramref name="cacheRoot"/>;
    /// the reference data alone when there is no install or its data can't be read at all. Never throws for the install.
    /// </summary>
    public static IGameData Load(GameInstall? install, string cacheRoot)
    {
        if (install is null) return ReferenceGameData.Load();
        try
        {
            return new CompositeGameData(KfcGameData.Load(install, cacheRoot), ReferenceGameData.Load());
        }
        catch (Exception)
        {
            // The install is optional: whatever goes wrong reading it, the editor runs on the reference data.
            return ReferenceGameData.Load();
        }
    }
}
