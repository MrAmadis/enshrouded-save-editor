using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;

namespace EnshroudedSaveEditor.Tests;

public class MapEditorTests : IDisposable
{
    sealed class NoGame : IProcessProbe
    {
        public bool IsGameRunning() => false;
        public bool IsSteamRunning() => false;
    }

    readonly string _tmp = Fixtures.CopyToTemp();
    readonly string _local = Fixtures.CopyLocalToTemp();
    string P => Path.Combine(_tmp, "character2.ksd");
    static SaveSession Open(string path) => SaveSession.Open(path, Fixtures.ItemIds().Contains);

    public void Dispose()
    {
        foreach (var d in new[] { _tmp, _local })
            try { Directory.Delete(d, true); } catch { }
    }

    [Fact]
    public void ClearFog_clears_the_whole_map()
    {
        var s = Open(P);
        var map = new MapEditor(s);
        Assert.Equal(FogStatus.Fogged, map.Fog);

        map.ClearFog();

        Assert.Equal(FogStatus.Cleared, map.Fog);
        Assert.True(s.Fog!.IsCleared);
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void ClearFog_rejects_a_map_already_cleared()
    {
        var s = Open(P);
        var map = new MapEditor(s);
        map.ClearFog();

        Assert.Equal("Already cleared", Assert.Throws<EditRejectedException>(map.ClearFog).Message);
        Assert.Equal(1, s.ChangeCount);
    }

    // The game-made local "Test" character never went into a world: it has no fogofwar section.
    [Fact]
    public void ClearFog_rejects_a_character_without_a_map()
    {
        var s = Open(Path.Combine(_local, "character0.ksd"));
        var map = new MapEditor(s);
        Assert.Equal(FogStatus.NoMap, map.Fog);

        var e = Assert.Throws<EditRejectedException>(map.ClearFog);

        Assert.Equal(MapEditor.Explain(FogStatus.NoMap), e.Message);
        Assert.Contains("hasn't been in a world", e.Message);
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void ClearFog_rejects_a_fog_in_an_unknown_layout()
    {
        var file = KsdFile.Parse(File.ReadAllBytes(P));
        file["fogofwar"].SetData([1, 2, 3]);
        File.WriteAllBytes(P, file.ToBytes());
        var s = Open(P);
        var map = new MapEditor(s);
        Assert.Equal(FogStatus.Unrecognised, map.Fog);

        var e = Assert.Throws<EditRejectedException>(map.ClearFog);

        Assert.Equal(MapEditor.Explain(FogStatus.Unrecognised), e.Message);
        Assert.Equal(0, s.ChangeCount);
    }

    // Ids character2 knows (value 1) and ids it doesn't.
    const uint Known1 = 0x01165547, Known2 = 0x0E7B9462, New1 = 0xA1A1A1A1, New2 = 0xA2A2A2A2, New3 = 0xA3A3A3A3;

    static readonly IReadOnlyList<MapLocation> Locations =
    [
        new("Known", [Known1]),
        new("Half known", [Known2, New1]),
        new("New", [New2, New3]),
    ];

    [Fact]
    public void DiscoverLocations_learns_every_missing_id()
    {
        var s = Open(P);
        Assert.True(s.Knowledge.Contains(Known1) && s.Knowledge.Contains(Known2));
        var map = new MapEditor(s);
        Assert.Equal((1, 3), map.Discovered(Locations));

        Assert.Equal(2, map.DiscoverLocations(Locations));

        Assert.Equal((3, 3), map.Discovered(Locations));
        Assert.Equal([(New1, 1u), (New2, 1u), (New3, 1u)], s.Knowledge.Entries.Skip(908));
        Assert.Equal(1, s.ChangeCount);
        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(Path.Combine(_tmp, "backups")), new NoGame()));
        Assert.Equal((3, 3), new MapEditor(Open(P)).Discovered(Locations));
    }

    [Fact]
    public void DiscoverLocations_rejects_when_every_location_is_discovered()
    {
        var s = Open(P);
        var map = new MapEditor(s);
        map.DiscoverLocations(Locations);

        Assert.Equal("Already discovered", Assert.Throws<EditRejectedException>(() => map.DiscoverLocations(Locations)).Message);
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void DiscoverLocations_rejects_a_character_without_a_map()
    {
        var s = Open(Path.Combine(_local, "character0.ksd"));
        var map = new MapEditor(s);
        Assert.False(map.HasKnowledge);

        var e = Assert.Throws<EditRejectedException>(() => map.DiscoverLocations(Locations));

        Assert.Equal(MapEditor.Explain(FogStatus.NoMap), e.Message);
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void ClearFog_rejects_a_read_only_save()
    {
        File.WriteAllBytes(P, Fixtures.PatchBlock(File.ReadAllBytes(P), "player_character", b => b[10] = 6));
        var s = Open(P);
        Assert.True(s.ReadOnly);

        Assert.StartsWith("This save is read-only", Assert.Throws<EditRejectedException>(new MapEditor(s).ClearFog).Message);
        Assert.StartsWith("This save is read-only",
            Assert.Throws<EditRejectedException>(() => new MapEditor(s).DiscoverLocations(Locations)).Message);
        Assert.False(s.Fog!.IsCleared);
        Assert.False(s.Knowledge.Contains(New1));
    }
}
