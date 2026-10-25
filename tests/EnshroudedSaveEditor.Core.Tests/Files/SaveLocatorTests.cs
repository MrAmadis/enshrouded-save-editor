using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Tests;

public class SaveLocatorTests : IDisposable
{
    readonly string _tmp = Fixtures.CopyToTemp();

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void Scan_lists_three_amadis_newest_first()
    {
        var list = new SaveLocator().Scan(_tmp);
        Assert.Equal(new[] { "character2.ksd", "character1.ksd", "character0.ksd" }, list.Select(c => c.FileName));
        Assert.All(list, c => { Assert.Equal("Amadis", c.Name); Assert.Equal(CharacterStatus.Ok, c.Status); });
        Assert.Equal(new[] { 7, 18, 12 }, list.Select(c => c.Level));
    }

    [Fact]
    public void Damaged_file_is_listed_not_thrown()
    {
        File.WriteAllBytes(Path.Combine(_tmp, "character0.ksd"), new byte[] { 1, 2, 3, 4, 5 });
        var list = new SaveLocator().Scan(_tmp);
        Assert.Equal(3, list.Count);
        var bad = list.Single(c => c.FileName == "character0.ksd");
        Assert.Equal(CharacterStatus.Damaged, bad.Status);
        Assert.False(string.IsNullOrWhiteSpace(bad.StatusMessage));
        Assert.All(list.Where(c => c != bad), c => Assert.Equal(CharacterStatus.Ok, c.Status));
    }

    [Fact]
    public void Unknown_data_version_is_unsupported()
    {
        var p = Path.Combine(_tmp, "character1.ksd");
        File.WriteAllBytes(p, Fixtures.PatchBlock(File.ReadAllBytes(p), "player_character", b => b[10] = 6));
        var c = new SaveLocator().Scan(_tmp).Single(x => x.FileName == "character1.ksd");
        Assert.Equal(CharacterStatus.Unsupported, c.Status);
    }

    [Fact]
    public void Checksum_mismatch_is_damaged()
    {
        var p = Path.Combine(_tmp, "character1.ksd");
        var bytes = File.ReadAllBytes(p);
        var block = KsdFile.Parse(bytes)["fogofwar"].Block.ToArray();
        var at = bytes.AsSpan().IndexOf(block);
        Assert.True(at >= 0);
        bytes[at + block.Length - 1] ^= 0xFF;
        File.WriteAllBytes(p, bytes);
        var c = new SaveLocator().Scan(_tmp).Single(x => x.FileName == "character1.ksd");
        Assert.Equal(CharacterStatus.Damaged, c.Status);
        Assert.Contains("checksum", c.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ignores_world_and_global_files()
    {
        var list = new SaveLocator().Scan(_tmp);
        Assert.Equal(3, list.Count);
        Assert.All(list, c => Assert.StartsWith("character", c.FileName));
    }

    [Fact]
    public void Missing_root_returns_empty() => Assert.Empty(new SaveLocator().Scan(@"C:\does\not\exist"));

    [Fact]
    public void DefaultRoots_does_not_throw() => Assert.NotNull(SaveLocator.DefaultRoots());

    [Theory]
    [InlineData("character_data")]
    [InlineData("player_character")]
    public void Truncated_record_is_damaged_while_others_stay_ok(string section)
    {
        var p = Path.Combine(_tmp, "character0.ksd");
        var f = KsdFile.Parse(File.ReadAllBytes(p));
        f[section].SetData(new byte[] { 1, 2, 3 });
        File.WriteAllBytes(p, f.ToBytes());
        var list = new SaveLocator().Scan(_tmp);
        Assert.Equal(3, list.Count);
        var bad = list.Single(c => c.FileName == "character0.ksd");
        Assert.NotEqual(CharacterStatus.Ok, bad.Status);
        Assert.False(string.IsNullOrWhiteSpace(bad.StatusMessage));
        Assert.All(list.Where(c => c != bad), c => Assert.Equal(CharacterStatus.Ok, c.Status));
    }
}
