using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace EnshroudedSaveEditor.GameData;

/// <summary>
/// Writes an <see cref="IconTexture"/> as a PNG file: 8-bit RGBA, unfiltered rows in one zlib-compressed IDAT chunk
/// (unfiltered rows came out smallest for the game's icons, and fastest).
/// </summary>
internal static class Png
{
    static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(IconTexture image)
    {
        var stride = image.Width * 4;
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < image.Height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(image.Rgba, y * stride, stride);
            }
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA; compression, filter method and interlace stay 0
        Chunk(png, "IHDR"u8, header);
        Chunk(png, "IDAT"u8, idat.GetBuffer().AsSpan(0, (int)idat.Length));
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    /// <summary>A chunk: its length, type, data, and the CRC-32 of type and data.</summary>
    static void Chunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);
        png.Write(type);
        png.Write(data);
        var crc = new Crc32();
        crc.Append(type);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc.GetCurrentHashAsUInt32());
        png.Write(word);
    }
}
