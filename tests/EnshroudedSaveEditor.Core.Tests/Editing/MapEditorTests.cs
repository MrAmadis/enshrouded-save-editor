using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Tests;

public class MapEditorTests : IDisposable
{
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

    [Fact]
    public void ClearFog_rejects_a_read_only_save()
    {
        File.WriteAllBytes(P, Fixtures.PatchBlock(File.ReadAllBytes(P), "player_character", b => b[10] = 6));
        var s = Open(P);
        Assert.True(s.ReadOnly);

        Assert.StartsWith("This save is read-only", Assert.Throws<EditRejectedException>(new MapEditor(s).ClearFog).Message);
        Assert.False(s.Fog!.IsCleared);
    }
}
