using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Records;

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
    public void Snapshot_restores_the_three_records_and_the_change_count()
    {
        var originalPlayer = KsdFile.Parse(File.ReadAllBytes(P))["player_character"].GetData();
        var s = Open();
        var before = s.Snapshot();

        s.Character = s.Character.With(name: "Other");
        s.Player.Level = 9;
        s.Player.Skills.Clear();
        s.Player.Container(ContainerKind.Inventory)!.Records.RemoveAt(0);
        s.MarkChanged();
        var edited = s.Snapshot();
        Assert.Equal(1, edited.ChangeCount);

        s.Restore(before);
        Assert.Equal("Amadis", s.Character.Name);
        Assert.Equal(7u, s.Player.Level);
        Assert.Equal(30, s.Player.Skills.Count);
        Assert.Equal(InventoryStatus.Full, s.Player.InventoryStatus);
        Assert.Equal(originalPlayer, s.Player.ToBytes());
        Assert.Equal(0, s.ChangeCount);

        s.Restore(edited);
        Assert.Equal(("Other", 9u, 1), (s.Character.Name, s.Player.Level, s.ChangeCount));
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));
        var reopened = Open();
        Assert.Equal(("Other", 9u), (reopened.Character.Name, reopened.Player.Level));
        Assert.Empty(reopened.Player.Skills);
    }

    [Fact]
    public void Open_reads_the_fog_when_the_character_has_one()
    {
        Assert.False(Open().Fog!.IsCleared);

        var local = Fixtures.CopyLocalToTemp();
        try
        {
            Assert.Null(SaveSession.Open(Path.Combine(local, "character0.ksd"), Fixtures.ItemIds().Contains).Fog);
        }
        finally
        {
            try { Directory.Delete(local, true); } catch { }
        }
    }

    [Fact]
    public void A_fog_in_an_unknown_layout_is_left_alone_and_the_save_stays_writable()
    {
        var file = KsdFile.Parse(File.ReadAllBytes(P));
        file["fogofwar"].SetData([1, 2, 3]);
        File.WriteAllBytes(P, file.ToBytes());
        var block = KsdFile.Parse(File.ReadAllBytes(P))["fogofwar"].Block.ToArray();

        var s = Open();
        Assert.False(s.ReadOnly);
        Assert.Null(s.Fog);
        s.Player.Level = 8; s.MarkChanged();
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));

        Assert.Equal(block, KsdFile.Parse(File.ReadAllBytes(P))["fogofwar"].Block.ToArray());
    }

    [Fact]
    public void Save_writes_a_cleared_fog_and_keeps_the_other_sections()
    {
        var orig = KsdFile.Parse(File.ReadAllBytes(P));
        var s = Open();
        s.Fog = s.Fog!.Cleared(); s.MarkChanged();

        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));

        var after = KsdFile.Parse(File.ReadAllBytes(P));
        var fog = after["fogofwar"].GetData();
        Assert.Equal(orig["fogofwar"].GetData()[..13], fog[..13]);
        Assert.True(fog.AsSpan(13).IndexOfAnyExcept((byte)255) < 0);
        Assert.True(after["fogofwar"].ChecksumValid);
        foreach (var o in orig.Sections.Where(x => x.Name != "fogofwar"))
            Assert.True(o.Block.Span.SequenceEqual(after[o.Name].Block.Span), o.Name);
        Assert.True(Open().Fog!.IsCleared);
    }

    [Fact]
    public void Snapshot_restores_the_fog()
    {
        var orig = KsdFile.Parse(File.ReadAllBytes(P))["fogofwar"].Block.ToArray();
        var s = Open();
        var before = s.Snapshot();
        s.Fog = s.Fog!.Cleared(); s.MarkChanged();

        s.Restore(before);
        Assert.False(s.Fog!.IsCleared);
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));

        Assert.Equal(orig, KsdFile.Parse(File.ReadAllBytes(P))["fogofwar"].Block.ToArray());
    }

    [Fact]
    public void Restore_refuses_a_snapshot_that_does_not_read_back()
    {
        var s = Open();
        var good = s.Snapshot();
        var bad = good with { Player = good.Player[..20] };
        Assert.ThrowsAny<KsdFormatException>(() => s.Restore(bad));
        Assert.Equal(7u, s.Player.Level);
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

    static (List<byte[]> Blocks, uint Revision, DateTimeOffset SavedAt) FileState(SaveSession s) =>
        (s.File.Sections.Select(x => x.Block.ToArray()).ToList(), s.File.Revision, s.File.SavedAt);

    static void AssertSameState((List<byte[]> Blocks, uint Revision, DateTimeOffset SavedAt) expected, SaveSession s)
    {
        var actual = FileState(s);
        Assert.Equal((expected.Revision, expected.SavedAt), (actual.Revision, actual.SavedAt));
        Assert.Equal(expected.Blocks.Count, actual.Blocks.Count);
        for (var i = 0; i < expected.Blocks.Count; i++)
            Assert.True(expected.Blocks[i].AsSpan().SequenceEqual(actual.Blocks[i]), s.File.Sections[i].Name);
        Assert.All(s.File.Sections, x => Assert.True(x.ChecksumValid, x.Name));
    }

    // Review fix 1: a failed save must leave the in-memory container equal to the file on disk, or a later save that
    // finds the records unchanged would write the failed attempt's sections.
    [Fact]
    public void VerifyFailed_leaves_the_container_as_it_was_before_the_save()
    {
        var disk = File.ReadAllBytes(P);
        var s = Open();
        var before = FileState(s);
        s.Player.Level = 10; s.MarkChanged();
        s.VerifyOverride = () => false;

        Assert.Equal(SaveResult.VerifyFailed, s.Save(Backups, new FakeProbe()));

        Assert.Equal(disk, File.ReadAllBytes(P));
        AssertSameState(before, s);
        Assert.Equal(1, s.ChangeCount);

        // Undo the edit by hand and save: the file must hold level 7, not the failed attempt's 10.
        s.VerifyOverride = null;
        s.Player.Level = 7;
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));
        var reopened = Open();
        Assert.Equal(7u, reopened.Player.Level);
        Assert.Equal(before.Revision + 1, reopened.File.Revision);
    }

    [Fact]
    public void A_failed_write_leaves_the_container_as_it_was_before_the_save()
    {
        var disk = File.ReadAllBytes(P);
        var s = Open();
        var before = FileState(s);
        s.Player.Level = 10; s.MarkChanged();
        File.SetAttributes(P, FileAttributes.ReadOnly);
        try
        {
            var e = Record.Exception(() => s.Save(Backups, new FakeProbe()));
            Assert.True(e is IOException or UnauthorizedAccessException, e?.ToString() ?? "no exception");
        }
        finally
        {
            File.SetAttributes(P, FileAttributes.Normal);
        }

        Assert.Equal(disk, File.ReadAllBytes(P));
        AssertSameState(before, s);
        Assert.Empty(Directory.GetFiles(_tmp, "*.tmp"));
        Assert.Equal(SaveResult.Saved, s.Save(Backups, new FakeProbe()));
        Assert.Equal(10u, Open().Player.Level);
    }
}
