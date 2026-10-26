using EnshroudedSaveEditor.GameData.Install;

namespace EnshroudedSaveEditor.Tests;

public sealed class InstallTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "ese-install-" + Guid.NewGuid().ToString("N"));

    public InstallTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() => Directory.Delete(_tmp, true);

    string MakeGameDir(string name, params string[] files)
    {
        var dir = Path.Combine(_tmp, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "x");
        return dir;
    }

    static readonly string[] AllFiles = ["enshrouded.exe", "enshrouded.kfc", "enshrouded.kfc_resources"];

    const string Vdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"228980"		"391461943"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"apps"
        		{
        			"1203620"		"1"
        		}
        	}
        }
        """;

    [Fact]
    public void Parses_library_paths_and_unescapes_backslashes()
    {
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLibraries.Parse(Vdf));
    }

    [Fact]
    public void Parse_of_garbage_is_empty()
    {
        Assert.Empty(SteamLibraries.Parse("not a vdf file"));
    }

    [Fact]
    public void Find_reads_libraryfolders_under_the_steam_root_and_includes_the_root()
    {
        var root = Path.Combine(_tmp, "Steam");
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\"0\"\n{\n\"path\"\t\"" + root.Replace(@"\", @"\\") + "\"\n}\n\"1\"\n{\n\"path\"\t\"Z:\\\\Games\"\n}\n}\n");
        Assert.Equal([root, @"Z:\Games"], SteamLibraries.Find(root));
    }

    [Fact]
    public void Find_without_a_vdf_returns_just_the_root()
    {
        Assert.Equal([_tmp], SteamLibraries.Find(_tmp));
    }

    [Fact]
    public void IsGameDir_needs_all_three_files()
    {
        Assert.True(GameInstall.IsGameDir(MakeGameDir("full", AllFiles)));
        Assert.False(GameInstall.IsGameDir(MakeGameDir("partial", "enshrouded.exe", "enshrouded.kfc")));
        Assert.False(GameInstall.IsGameDir(Path.Combine(_tmp, "missing")));
    }

    [Fact]
    public void A_valid_configured_dir_wins()
    {
        var dir = MakeGameDir("custom", AllFiles);
        Assert.Equal(dir, GameInstall.Find(dir)!.Dir);
    }

    [Fact]
    public void Finds_install_in_a_steam_library()
    {
        var lib = Path.Combine(_tmp, "lib");
        var game = Path.Combine(lib, "steamapps", "common", "Enshrouded");
        Directory.CreateDirectory(game);
        foreach (var f in AllFiles) File.WriteAllText(Path.Combine(game, f), "x");
        Assert.Equal(game, GameInstall.FindIn([Path.Combine(_tmp, "empty"), lib])!.Dir);
        Assert.Null(GameInstall.FindIn([Path.Combine(_tmp, "empty")]));
    }

    [Fact]
    public void Finds_install_through_steam_libraries()
    {
        const string expected = @"D:\SteamLibrary\steamapps\common\Enshrouded";
        Assert.SkipUnless(GameInstall.IsGameDir(expected), "Enshrouded is not installed at " + expected);
        Assert.Equal(expected, GameInstall.Find(null)!.Dir);
    }
}
