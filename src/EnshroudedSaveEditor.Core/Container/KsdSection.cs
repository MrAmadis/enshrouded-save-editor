using System.Buffers.Binary;
using ZstdSharp;

namespace EnshroudedSaveEditor.Core.Container;

/// <summary>
/// One named block of a .ksd file. Block layout: 4-byte tag, u32 block version,
/// u8 type id, u8 compressed flag, u32 data version, then the body (zstd frame or raw bytes).
/// </summary>
public sealed class KsdSection
{
    internal const int HeaderSize = 14;
    private const int CompressionLevel = 3;

    private byte[] _block;
    private uint _storedChecksum;

    internal KsdSection(string name, byte[] block, uint storedChecksum)
    {
        if (block.Length < HeaderSize)
            throw new KsdFormatException($"Section '{name}' is smaller than its {HeaderSize}-byte header.");
        Name = name;
        _block = block;
        _storedChecksum = storedChecksum;
    }

    public string Name { get; }
    public byte TypeId => _block[8];
    public bool Compressed => _block[9] == 1;
    public uint BlockVersion => BinaryPrimitives.ReadUInt32LittleEndian(_block.AsSpan(4));
    public uint DataVersion => BinaryPrimitives.ReadUInt32LittleEndian(_block.AsSpan(10));
    public bool ChecksumValid => KsdChecksum.Compute(_block) == _storedChecksum;
    public ReadOnlyMemory<byte> Block => _block;

    internal uint StoredChecksum => _storedChecksum;

    /// <summary>The decompressed data, or the raw body when the section is stored.</summary>
    public byte[] GetData()
    {
        var body = _block.AsSpan(HeaderSize);
        if (!Compressed) return body.ToArray();
        try
        {
            using var d = new Decompressor();
            return d.Unwrap(body).ToArray();
        }
        catch (ZstdException e)
        {
            throw new KsdFormatException($"Section '{Name}' has a corrupt zstd body: {e.Message}");
        }
    }

    /// <summary>Replaces the data, keeping the header fields and compression mode, and recomputes the checksum.</summary>
    public void SetData(byte[] data)
    {
        byte[] body;
        if (Compressed)
        {
            using var c = new Compressor(CompressionLevel);
            body = c.Wrap(data).ToArray();
        }
        else
        {
            body = data;
        }

        var block = new byte[HeaderSize + body.Length];
        _block.AsSpan(0, HeaderSize).CopyTo(block);
        body.CopyTo(block, HeaderSize);
        _block = block;
        _storedChecksum = KsdChecksum.Compute(block);
    }
}
