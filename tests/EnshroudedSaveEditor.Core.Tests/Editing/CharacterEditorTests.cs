using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Tests;

public class CharacterEditorTests : IDisposable
{
    sealed class FakeProbe : IProcessProbe
    {
        public bool IsGameRunning() => false;
        public bool IsSteamRunning() => false;
    }

    readonly string _tmp = Fixtures.CopyToTemp();
    readonly string _backupRoot = Path.Combine(Path.GetTempPath(), "enshrouded-backups-" + Guid.NewGuid().ToString("N"));
    string P => Path.Combine(_tmp, "character2.ksd");
    SaveSession Open() => SaveSession.Open(P, Fixtures.ItemIds().Contains);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
        try { Directory.Delete(_backupRoot, true); } catch { }
    }

    [Fact]
    public void SetName_rejects_invalid()
    {
        var s = Open(); var e = new CharacterEditor(s);
        foreach (var bad in new[] { "", "   ", new string('x', 16), "a\u0001b", "Amadis", "  Amadis  " })
            Assert.Throws<EditRejectedException>(() => e.SetName(bad));
        Assert.Equal("The name can be at most 15 characters.", Assert.Throws<EditRejectedException>(() => e.SetName(new string('x', 16))).Message);
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal("Amadis", s.Character.Name);
    }

    // Session A: the in-game character creator accepts up to 15 characters (the game still shows a longer name
    // written directly, but the editor matches the creator).
    [Fact]
    public void SetName_accepts_15_chars_and_trims()
    {
        var s = Open(); var e = new CharacterEditor(s);
        Assert.Equal(15, CharacterEditor.MaxNameLength);
        e.SetName("  " + new string('x', 15) + " ");
        Assert.Equal(new string('x', 15), s.Character.Name);
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void SetName_writes_new_name()
    {
        var s = Open();
        new CharacterEditor(s).SetName("Amadis the Bold");
        Assert.Equal("Amadis the Bold", s.Character.Name);
        Assert.Equal(1, s.ChangeCount);
        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(_backupRoot), new FakeProbe()));
        Assert.Equal("Amadis the Bold", Open().Character.Name);
    }

    [Fact]
    public void SetLevel_rejects_out_of_range_and_noop()
    {
        var s = Open(); var e = new CharacterEditor(s);
        Assert.Throws<EditRejectedException>(() => e.SetLevel(0, 50));
        Assert.Throws<EditRejectedException>(() => e.SetLevel(51, 50));
        var ex = Assert.Throws<EditRejectedException>(() => e.SetLevel(7, 50));
        Assert.Equal("Already 7", ex.Message);
        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(7u, s.Player.Level);
    }

    [Fact]
    public void SetXp_range_and_write()
    {
        var s = Open(); var e = new CharacterEditor(s);
        Assert.Throws<EditRejectedException>(() => e.SetXp(-1));
        Assert.Throws<EditRejectedException>(() => e.SetXp((long)uint.MaxValue + 1));
        Assert.Throws<EditRejectedException>(() => e.SetXp(s.Player.Xp));
        Assert.Equal(0, s.ChangeCount);
        e.SetXp(uint.MaxValue);
        Assert.Equal(uint.MaxValue, s.Player.Xp);
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void Raising_level_raises_available_points()
    {
        var game = ReferenceGameData.Load();
        var s = Open();
        PointsSummary Pts() => SkillPoints.Compute(s.Player.Level, s.Player.Skills, game.Tree, s.Knowledge, game.Rewards);
        Assert.Equal(0, Pts().Available);
        new CharacterEditor(s).SetLevel(10, game.LevelCap);
        Assert.Equal(6, Pts().Available);
        Assert.Equal(1, s.ChangeCount);
    }

    [Fact]
    public void Read_only_session_rejects_edits()
    {
        // Break the container version byte (offset 4): unsupported version -> opened read-only.
        var bytes = File.ReadAllBytes(P);
        bytes[4] ^= 0x7F;
        File.WriteAllBytes(P, bytes);
        var s = Open();
        Assert.True(s.ReadOnly);
        var e = new CharacterEditor(s);
        var expected = "This save is read-only: " + s.ReadOnlyReason;
        Assert.Equal(expected, Assert.Throws<EditRejectedException>(() => e.SetLevel(10, 50)).Message);
        Assert.Equal(expected, Assert.Throws<EditRejectedException>(() => e.SetName("Bob")).Message);
        Assert.Equal(expected, Assert.Throws<EditRejectedException>(() => e.SetXp(5)).Message);
        Assert.Equal(0, s.ChangeCount);
    }
}
