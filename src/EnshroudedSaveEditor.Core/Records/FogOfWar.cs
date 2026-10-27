using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>
/// The <c>fogofwar</c> record: a 13-byte header (<c>FGOW</c> at offset 8), then a 1280 × 1280 grid, one byte a cell, 0
/// fogged to 255 cleared (docs/superpowers/specs/2026-10-26-map-fog-and-locations-design.md §5). Immutable, so a
/// snapshot can keep it without copying 1.6 MB.
/// </summary>
public sealed class FogOfWar
{
    const int HeaderSize = 13;
    const int GridSize = 1280;
    const byte Clear = 255;
    static ReadOnlySpan<byte> Magic => "FGOW"u8;

    readonly byte[] _data;

    FogOfWar(byte[] data)
    {
        _data = data;
        IsCleared = data.AsSpan(HeaderSize).IndexOfAnyExcept(Clear) < 0;
    }

    /// <summary>Every cell is cleared: the whole map is revealed.</summary>
    public bool IsCleared { get; }

    public static FogOfWar Parse(byte[] data)
    {
        if (data.Length != HeaderSize + GridSize * GridSize || !data.AsSpan(8, Magic.Length).SequenceEqual(Magic))
            throw new KsdFormatException("Unexpected fogofwar layout.");
        return new FogOfWar((byte[])data.Clone());
    }

    /// <summary>The same header with every cell cleared.</summary>
    public FogOfWar Cleared()
    {
        var data = new byte[_data.Length];
        _data.AsSpan(0, HeaderSize).CopyTo(data);
        data.AsSpan(HeaderSize).Fill(Clear);
        return new FogOfWar(data);
    }

    public byte[] ToBytes() => (byte[])_data.Clone();
}
