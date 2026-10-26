namespace EnshroudedSaveEditor.GameData.Install;

/// <summary>A found Enshrouded install. The game files are only ever read.</summary>
public sealed class GameInstall
{
    static readonly string[] RequiredFiles = ["enshrouded.exe", "enshrouded.kfc", "enshrouded.kfc_resources"];

    GameInstall(string dir) => Dir = dir;

    public string Dir { get; }

    /// <summary>True when the game's executable and both kfc archives are in <paramref name="dir"/>.</summary>
    public static bool IsGameDir(string dir) =>
        !string.IsNullOrWhiteSpace(dir) && RequiredFiles.All(f => File.Exists(Path.Combine(dir, f)));

    /// <summary>The configured folder if it is a game folder, else the install in a Steam library, else null.</summary>
    public static GameInstall? Find(string? configuredDir) =>
        configuredDir is not null && IsGameDir(configuredDir)
            ? new GameInstall(configuredDir)
            : FindIn(SteamLibraries.Find());

    public static GameInstall? FindIn(IEnumerable<string> libraries)
    {
        foreach (var lib in libraries)
        {
            var dir = Path.Combine(lib, "steamapps", "common", "Enshrouded");
            if (IsGameDir(dir)) return new GameInstall(dir);
        }
        return null;
    }
}
