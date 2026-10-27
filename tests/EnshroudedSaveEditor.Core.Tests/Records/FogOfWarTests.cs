using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Tests;

public class FogOfWarTests
{
    // The 13-byte header every save holds (map spec §5), then the 1280 × 1280 grid.
    static readonly byte[] Header = [0x05, 0x00, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46, 0x47, 0x4F, 0x57, 0x02];
    const int Length = 13 + 1280 * 1280;

    static byte[] Raw(string file) => KsdFile.Parse(Fixtures.Read(file))["fogofwar"].GetData();

    [Theory, InlineData("character0.ksd"), InlineData("character1.ksd"), InlineData("character2.ksd")]
    public void Fog_parses_and_round_trips(string file)
    {
        var raw = Raw(file);
        var fog = FogOfWar.Parse(raw);
        Assert.False(fog.IsCleared);
        Assert.Equal(raw, fog.ToBytes());
    }

    [Fact]
    public void Cleared_keeps_the_header_and_clears_every_cell()
    {
        var raw = Raw("character2.ksd");
        var fog = FogOfWar.Parse(raw);

        var cleared = fog.Cleared();

        var bytes = cleared.ToBytes();
        Assert.Equal(Length, bytes.Length);
        Assert.Equal(Header, bytes[..13]);
        Assert.True(bytes.AsSpan(13).IndexOfAnyExcept((byte)255) < 0);
        Assert.True(cleared.IsCleared);
        Assert.True(FogOfWar.Parse(bytes).IsCleared);
        Assert.False(fog.IsCleared);
        Assert.Equal(raw, fog.ToBytes());
    }

    [Fact]
    public void One_fogged_cell_is_not_cleared()
    {
        var bytes = FogOfWar.Parse(Raw("character2.ksd")).Cleared().ToBytes();
        bytes[^1] = 254;
        Assert.False(FogOfWar.Parse(bytes).IsCleared);
    }

    [Fact]
    public void Fog_rejects_an_unknown_layout()
    {
        var raw = Raw("character2.ksd");
        Assert.Throws<KsdFormatException>(() => FogOfWar.Parse(raw[..^1]));
        Assert.Throws<KsdFormatException>(() => FogOfWar.Parse([.. raw, 0]));
        var noMagic = (byte[])raw.Clone();
        noMagic[8] = (byte)'X';
        Assert.Throws<KsdFormatException>(() => FogOfWar.Parse(noMagic));
    }
}
