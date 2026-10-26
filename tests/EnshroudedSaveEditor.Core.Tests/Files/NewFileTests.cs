using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Tests;

/// <summary>
/// The write behind backups and Lab copies: only a file that already exists counts as a name collision. A write that
/// fails after the file was created (a full disk) used to count as one too, so the partial file stayed behind.
/// </summary>
public sealed class NewFileTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("enshrouded-newfile-").FullName;
    string Target => Path.Combine(_dir, "character3.ksd");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Writes_a_new_file()
    {
        Assert.True(NewFile.TryWrite(Target, s => s.Write([1, 2, 3])));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Target));
    }

    [Fact]
    public void An_existing_file_is_a_collision_and_is_left_alone()
    {
        File.WriteAllBytes(Target, [9, 9]);
        var called = false;
        Assert.False(NewFile.TryWrite(Target, _ => called = true));
        Assert.False(called);
        Assert.Equal([9, 9], File.ReadAllBytes(Target));
    }

    [Fact]
    public void A_failed_write_removes_the_partial_file_and_rethrows()
    {
        var full = new IOException("There is not enough space on the disk.");
        var thrown = Assert.Throws<IOException>(() => NewFile.TryWrite(Target, s =>
        {
            s.Write(new byte[100_000]);                            // past the stream's buffer: on disk already
            throw full;
        }));
        Assert.Same(full, thrown);
        Assert.False(File.Exists(Target));
        Assert.True(NewFile.TryWrite(Target, s => s.Write([1])));  // the name is free again
    }
}
