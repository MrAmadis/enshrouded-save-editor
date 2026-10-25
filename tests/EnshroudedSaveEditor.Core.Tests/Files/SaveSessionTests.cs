using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Tests;

public class SaveSessionTests : IDisposable
{
    sealed class FakeProbe(bool game = false, bool steam = false) : IProcessProbe
    {
        public bool IsGameRunning() => game;
        public bool IsSteamRunning() => steam;
    }

    readonly string _tmp = Fixtures.CopyToTemp();
    readonly string _backupRoot = Path.Combine(Path.GetTempPath(), "enshrouded-backups-" + Guid.NewGuid().ToString("N"));
    string P => Path.Combine(_tmp, "character2.ksd");
    BackupService Backups => new(_backupRoot);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
        try { Directory.Delete(_backupRoot, true); } catch { }
    }

    SaveSession Open() => SaveSession.Open(P, Fixtures.ItemIds().Contains);

    [Fact]
    public void Open_reads_records_and_starts_clean()
    {
        var s = Open();
        Assert.Equal("Amadis", s.Character.Name);
        Assert.Equal(7u, s.Player.Level);
        Assert.Equal(0, s.ChangeCount);
        Assert.False(s.ReadOnly);
        Assert.NotNull(s.Knowledge);
    }

    [Fact]
    public void Save_writes_valid_file_with_backup_and_new_revision()
    {
        var original = File.ReadAllBytes(P);
        var s = Open();
        s.Player.Level = 8; s.MarkChanged();
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));

        var f = KsdFile.Parse(File.ReadAllBytes(P));
        Assert.All(f.Sections, x => Assert.True(x.ChecksumValid));
        Assert.Equal(8u, Open().Player.Level);
        Assert.Equal(992u, f.Revision);
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - f.SavedAt).TotalSeconds) < 5);
        var b = Backups.List(P);
        Assert.Single(b);
        Assert.Equal(original, File.ReadAllBytes(b[0].Path));
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void Save_refused_while_game_running()
    {
        var before = File.ReadAllBytes(P);
        var s = Open();
        s.Player.Level = 8; s.MarkChanged();
        Assert.Equal(SaveResult.GameRunning, s.Save(Backups, new FakeProbe(game: true)));
        Assert.Equal(before, File.ReadAllBytes(P));
        Assert.Empty(Backups.List(P));
    }

    [Fact]
    public void Save_refused_when_file_changed_on_disk()
    {
        var s = Open();
        s.MarkChanged();
        var other = File.ReadAllBytes(Path.Combine(_tmp, "character1.ksd"));
        File.WriteAllBytes(P, other);
        Assert.Equal(SaveResult.ChangedOnDisk, s.Save(Backups, new FakeProbe()));
        Assert.Equal(other, File.ReadAllBytes(P));
        Assert.Empty(Backups.List(P));
    }

    [Fact]
    public void ReadOnly_session_never_writes()
    {
        var bytes = File.ReadAllBytes(P);
        var block = KsdFile.Parse(bytes)["fogofwar"].Block.ToArray();
        var at = bytes.AsSpan().IndexOf(block);
        Assert.True(at >= 0);
        bytes[at + block.Length - 1] ^= 0xFF;
        File.WriteAllBytes(P, bytes);

        var s = Open();
        Assert.True(s.ReadOnly);
        Assert.False(string.IsNullOrWhiteSpace(s.ReadOnlyReason));
        s.MarkChanged();
        Assert.Equal(SaveResult.ReadOnly, s.Save(Backups, new FakeProbe()));
        Assert.Equal(bytes, File.ReadAllBytes(P));
        Assert.Empty(Backups.List(P));
    }

    [Fact]
    public void Unsupported_data_version_opens_read_only_or_throws_format_error()
    {
        // Version byte 6 on player_character: either still parseable (read-only) or a clear KsdFormatException.
        File.WriteAllBytes(P, Fixtures.PatchBlock(File.ReadAllBytes(P), "player_character", b => b[10] = 6));
        var before = File.ReadAllBytes(P);
        try
        {
            var s = Open();
            Assert.True(s.ReadOnly);
            s.MarkChanged();
            Assert.Equal(SaveResult.ReadOnly, s.Save(Backups, new FakeProbe()));
        }
        catch (KsdFormatException) { }
        Assert.Equal(before, File.ReadAllBytes(P));
    }

    [Fact]
    public void Unchanged_save_keeps_every_section_block()
    {
        var orig = KsdFile.Parse(File.ReadAllBytes(P));
        var s = Open();
        s.MarkChanged();
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));
        var after = KsdFile.Parse(File.ReadAllBytes(P));
        Assert.Equal(orig.Sections.Count, after.Sections.Count);
        foreach (var o in orig.Sections)
            Assert.True(o.Block.Span.SequenceEqual(after[o.Name].Block.Span), o.Name);
        Assert.Equal(orig.Revision + 1, after.Revision);
        Assert.Equal(orig.Guid, after.Guid);
    }

    [Fact]
    public void Restore_brings_back_backup_and_backs_up_current_first()
    {
        var original = File.ReadAllBytes(P);
        var s = Open();
        s.Player.Level = 8; s.MarkChanged();
        s.Save(Backups, new FakeProbe());
        var edited = File.ReadAllBytes(P);
        var b = Backups.List(P).Single();

        Backups.Restore(b, P);
        Assert.Equal(original, File.ReadAllBytes(P));
        var all = Backups.List(P);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => File.ReadAllBytes(x.Path).AsSpan().SequenceEqual(edited));
    }
}
