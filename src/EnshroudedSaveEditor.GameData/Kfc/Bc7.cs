using System.Buffers.Binary;
using System.Numerics;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// Decodes BC7 blocks (16 bytes, 4×4 RGBA texels) as the BC7 format specification describes them (Khronos Data Format
/// Specification, "BC7"; Direct3D 11 "BC7 Format"). Written from the specification; checked against Pillow's decoder.
/// </summary>
internal static class Bc7
{
    /// <summary>
    /// One mode's layout: subsets, partition, rotation and index-selection bits, colour and alpha endpoint bits, a P-bit
    /// per endpoint or per subset, and the bits of the first and second index.
    /// </summary>
    readonly record struct Mode(int Subsets, int PartitionBits, int RotationBits, int SelectorBits, int ColorBits, int AlphaBits,
        bool EndpointPBits, bool SharedPBits, int IndexBits, int IndexBits2);

    static readonly Mode[] Modes =
    [
        new(3, 4, 0, 0, 4, 0, true, false, 3, 0),
        new(2, 6, 0, 0, 6, 0, false, true, 3, 0),
        new(3, 6, 0, 0, 5, 0, false, false, 2, 0),
        new(2, 6, 0, 0, 7, 0, true, false, 2, 0),
        new(1, 0, 2, 1, 5, 6, false, false, 2, 3),
        new(1, 0, 2, 0, 7, 8, false, false, 2, 2),
        new(1, 0, 0, 0, 7, 7, true, false, 4, 0),
        new(2, 6, 0, 0, 5, 5, true, false, 2, 0),
    ];

    static readonly byte[] Weights2 = [0, 21, 43, 64];
    static readonly byte[] Weights3 = [0, 9, 18, 27, 37, 46, 55, 64];
    static readonly byte[] Weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    /// <summary>The two-subset partitions: bit <c>i</c> set puts texel <c>i</c> in subset 1.</summary>
    static readonly ushort[] Partitions2 =
    [
        0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80, 0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
        0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE, 0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
        0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A, 0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
        0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C, 0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22,
    ];

    /// <summary>The three-subset partitions: each texel's subset, row by row.</summary>
    static readonly string[] Partitions3 =
    [
        "0011001102212222", "0001001122112221", "0000200122112211", "0222002200110111",
        "0000000011221122", "0011001100220022", "0022002211111111", "0011001122112211",
        "0000000011112222", "0000111111112222", "0000111122222222", "0012001200120012",
        "0112011201120112", "0122012201220122", "0011011211221222", "0011200122002220",
        "0001001101121122", "0111001120012200", "0000112211221122", "0022002200221111",
        "0111011102220222", "0001000122212221", "0000001101220122", "0000110022102210",
        "0122012200110000", "0012001211222222", "0110122112210110", "0000011012211221",
        "0022110211020022", "0110011020022222", "0011012201220011", "0000200022112221",
        "0000000211221222", "0222002200120011", "0011001200220222", "0120012001200120",
        "0000111122220000", "0120120120120120", "0120201212010120", "0011220011220011",
        "0011112222000011", "0101010122222222", "0000000021212121", "0022112200221122",
        "0022001100220011", "0220122102201221", "0101222222220101", "0000212121212121",
        "0101010101012222", "0222011102220111", "0002111200021112", "0000211221122112",
        "0222011101110222", "0002111211120002", "0110011001102222", "0000000021122112",
        "0110011022222222", "0022001100110022", "0022112211220022", "0000000000002112",
        "0002000100020001", "0222122202221222", "0101222222222222", "0111201122012220",
    ];

    /// <summary>The anchor texel of subset 1 in the two-subset partitions (subset 0's is texel 0).</summary>
    static readonly byte[] Anchors2 =
    [
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
        6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15,
    ];

    /// <summary>The anchor texels of subsets 1 and 2 in the three-subset partitions.</summary>
    static readonly byte[] Anchors3A =
    [
        3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3,
        3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15,
        8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15,
        3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3,
    ];

    static readonly byte[] Anchors3B =
    [
        15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8,
        15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8,
        15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8,
        15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8,
    ];

    /// <summary>
    /// Decodes one block into <paramref name="rgba"/> (4 bytes per texel): its top-left texel at <paramref name="at"/>, its
    /// rows <paramref name="stride"/> bytes apart. A block without a mode (first byte 0) is transparent black.
    /// </summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> rgba, int at, int stride)
    {
        if (block[0] == 0)
        {
            for (var y = 0; y < 4; y++) rgba.Slice(at + y * stride, 16).Clear();
            return;
        }
        var mode = BitOperations.TrailingZeroCount(block[0]);
        var m = Modes[mode];
        var bits = new BitReader(BinaryPrimitives.ReadUInt64LittleEndian(block), BinaryPrimitives.ReadUInt64LittleEndian(block[8..]),
            mode + 1);
        var partition = bits.Read(m.PartitionBits);
        var rotation = bits.Read(m.RotationBits);
        var selector = bits.Read(m.SelectorBits);

        // Endpoint e of subset s is e[(s * 2 + e) * 4 + channel]: all reds first, then greens, blues and alphas.
        var endpoints = m.Subsets * 2;
        Span<int> e = stackalloc int[6 * 4];
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < endpoints; i++)
                e[i * 4 + c] = bits.Read(m.ColorBits);
        for (var i = 0; i < endpoints; i++)
            e[i * 4 + 3] = m.AlphaBits > 0 ? bits.Read(m.AlphaBits) : 255;

        int colorBits = m.ColorBits, alphaBits = m.AlphaBits;
        if (m.EndpointPBits || m.SharedPBits)
        {
            var pbit = 0;
            for (var i = 0; i < endpoints; i++)
            {
                if (m.EndpointPBits || i % 2 == 0) pbit = bits.Read(1);
                for (var c = 0; c < (alphaBits > 0 ? 4 : 3); c++) e[i * 4 + c] = e[i * 4 + c] << 1 | pbit;
            }
            colorBits++;
            if (alphaBits > 0) alphaBits++;
        }
        for (var i = 0; i < endpoints; i++)
        {
            for (var c = 0; c < 3; c++) e[i * 4 + c] = Expand(e[i * 4 + c], colorBits);
            if (alphaBits > 0) e[i * 4 + 3] = Expand(e[i * 4 + 3], alphaBits);
        }

        Span<int> index = stackalloc int[16];
        for (var t = 0; t < 16; t++) index[t] = bits.Read(m.IndexBits - (IsAnchor(m.Subsets, partition, t) ? 1 : 0));
        Span<int> index2 = stackalloc int[16];
        for (var t = 0; t < 16 && m.IndexBits2 > 0; t++) index2[t] = bits.Read(m.IndexBits2 - (t == 0 ? 1 : 0));

        // Modes 4 and 5 have a second index: alpha's (colour's when mode 4's selector bit is set).
        var colorIndex = selector == 0 ? index : index2;
        var alphaIndex = m.IndexBits2 == 0 || selector == 1 ? index : index2;
        var colorWeights = Weights(selector == 0 ? m.IndexBits : m.IndexBits2);
        var alphaWeights = Weights(m.IndexBits2 == 0 || selector == 1 ? m.IndexBits : m.IndexBits2);
        for (var t = 0; t < 16; t++)
        {
            var s = SubsetOf(m.Subsets, partition, t) * 2 * 4;
            int cw = colorWeights[colorIndex[t]], aw = alphaWeights[alphaIndex[t]];
            int r = Lerp(e[s], e[s + 4], cw), g = Lerp(e[s + 1], e[s + 5], cw), b = Lerp(e[s + 2], e[s + 6], cw);
            var a = Lerp(e[s + 3], e[s + 7], aw);
            switch (rotation)
            {
                case 1: (r, a) = (a, r); break;
                case 2: (g, a) = (a, g); break;
                case 3: (b, a) = (a, b); break;
            }
            var o = at + (t >> 2) * stride + (t & 3) * 4;
            rgba[o] = (byte)r;
            rgba[o + 1] = (byte)g;
            rgba[o + 2] = (byte)b;
            rgba[o + 3] = (byte)a;
        }
    }

    /// <summary>An endpoint of <paramref name="bits"/> bits widened to 8 by repeating its top bits.</summary>
    static int Expand(int v, int bits) => bits >= 8 ? v : v << (8 - bits) | v >> (2 * bits - 8);

    static int Lerp(int a, int b, int weight) => ((64 - weight) * a + weight * b + 32) >> 6;

    static byte[] Weights(int bits) => bits switch { 2 => Weights2, 3 => Weights3, _ => Weights4 };

    static int SubsetOf(int subsets, int partition, int texel) => subsets switch
    {
        1 => 0,
        2 => Partitions2[partition] >> texel & 1,
        _ => Partitions3[partition][texel] - '0',
    };

    /// <summary>An anchor texel's index has one bit less (its top bit is 0).</summary>
    static bool IsAnchor(int subsets, int partition, int texel) => texel == 0 || subsets switch
    {
        2 => texel == Anchors2[partition],
        3 => texel == Anchors3A[partition] || texel == Anchors3B[partition],
        _ => false,
    };

    /// <summary>Reads a block's bits (its low and high 64) from the lowest up, at most 8 at a time.</summary>
    ref struct BitReader(ulong low, ulong high, int at)
    {
        int _at = at;

        public int Read(int n)
        {
            var v = _at switch
            {
                0 => low,
                < 64 => low >> _at | high << (64 - _at),
                _ => high >> (_at - 64),
            };
            _at += n;
            return (int)(v & ((1UL << n) - 1));
        }
    }
}
