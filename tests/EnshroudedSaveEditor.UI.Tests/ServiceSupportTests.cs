using System.Text.Json;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.GameData.Install;
using EnshroudedSaveEditor.UI.Services;

namespace EnshroudedSaveEditor.Tests;

public sealed class ServiceSupportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "enshrouded-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Settings_round_trip_as_camel_case_json()
    {
        var path = Path.Combine(_dir, "sub", "settings.json");
        new Settings { GameDir = @"D:\Games\Enshrouded", SaveRoot = @"D:\Saves" }.Save(path);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(@"D:\Games\Enshrouded", json.RootElement.GetProperty("gameDir").GetString());
        Assert.Equal(@"D:\Saves", json.RootElement.GetProperty("saveRoot").GetString());
        var back = Settings.Load(path);
        Assert.Equal((@"D:\Games\Enshrouded", @"D:\Saves"), (back.GameDir, back.SaveRoot));
    }

    [Fact]
    public void Missing_or_broken_settings_load_as_defaults()
    {
        Assert.Null(Settings.Load(Path.Combine(_dir, "none.json")).SaveRoot);
        Directory.CreateDirectory(_dir);
        var broken = Path.Combine(_dir, "broken.json");
        File.WriteAllText(broken, "{ not json");
        var s = Settings.Load(broken);
        Assert.Null(s.GameDir);
        Assert.Null(s.SaveRoot);
    }

    [Fact]
    public void Game_data_loads_once_on_first_use()
    {
        var loads = 0;
        var provider = new GameDataProvider(() => { loads++; return ReferenceGameData.Load(); });
        Assert.Equal(0, loads);

        Assert.Same(provider.Data, provider.Data);
        Assert.Equal(ReferenceGameData.Load().Source, provider.Source);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void Options_place_backups_cache_and_settings_under_the_app_data_root()
    {
        var o = new EditorOptions([], @"C:\AppData\Ese", new EditorServiceTests.FakeProbe(), null);
        Assert.Equal(@"C:\AppData\Ese\backups", o.BackupRoot);
        Assert.Equal(@"C:\AppData\Ese\cache\icons", o.IconCacheRoot);
        Assert.Equal(@"C:\AppData\Ese\cache\gamedata", o.GameDataCacheRoot);
        Assert.Equal(@"C:\AppData\Ese\settings.json", o.SettingsPath);
        Assert.EndsWith(@"\EnshroudedSaveEditor", EditorOptions.DefaultAppDataRoot);
    }

    [Fact]
    public void Without_an_install_the_reference_data_is_used()
    {
        Assert.Same(ReferenceGameData.Load(), GameDataProvider.Load(null, Path.Combine(_dir, "cache")));
    }

    [Fact]
    public void An_install_that_cannot_be_read_falls_back_to_the_reference_data()
    {
        var game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(game);
        foreach (var f in new[] { "enshrouded.exe", "enshrouded.kfc", "enshrouded.kfc_resources" })
            File.WriteAllText(Path.Combine(game, f), "not game data");
        Assert.Same(ReferenceGameData.Load(), GameDataProvider.Load(GameInstall.Find(game), Path.Combine(_dir, "cache")));
    }

    [Fact]
    public void A_found_install_is_preferred_with_the_reference_data_as_fallback()
    {
        var install = GameInstall.Find(null);
        Assert.SkipWhen(install is null, "Enshrouded is not installed");
        var data = GameDataProvider.Load(install, Path.Combine(_dir, "cache"));
        Assert.IsType<CompositeGameData>(data);
        Assert.StartsWith("Your Enshrouded install", data.Source);
    }
}
