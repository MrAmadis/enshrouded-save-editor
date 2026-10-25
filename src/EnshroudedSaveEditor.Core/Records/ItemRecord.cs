using System.Buffers.Binary;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>
/// One item record inside <c>player_character</c>: <c>[u8 slot][u32 item id][u16 count][u8 field flags]</c>,
/// then the optional fields in bit order (layout from Kimikozi's lib/pc.js, MIT; see THIRD_PARTY.md):
/// bit 0 u8 level; bit 1 u32 durability, u32 max; bit 2 8 unknown bytes; bit 3 4-byte transmog reference;
/// bit 4 u8 rarity, u8 extra; bit 5 gem socket <c>[u32 gem id][u8 marker]</c>. Bits 6-7 have no known size.
/// <see cref="Flags"/> is derived from which optional fields are set, so the two can't disagree.
/// </summary>
public sealed class ItemRecord
{
    public const int HeaderLength = 8;

    static readonly int[] FieldSizes = { 1, 8, 8, 4, 2, 5 };

    byte[]? _bit2;
    byte[]? _transmog;

    public ItemRecord(byte slot, uint itemId, ushort count)
    {
        Slot = slot; ItemId = itemId; Count = count;
    }

    public byte Slot { get; set; }
    public uint ItemId { get; set; }
    public ushort Count { get; set; }

    /// <summary>Bit 0.</summary>
    public byte? Level { get; set; }

    /// <summary>Bit 1.</summary>
    public (uint Current, uint Max)? Durability { get; set; }

    /// <summary>Bit 2: 8 bytes of unknown meaning (seen on Water Pouches). Copied on set.</summary>
    public byte[]? Bit2 { get => _bit2; set => _bit2 = Sized(value, 8); }

    /// <summary>Bit 3: 4-byte appearance (transmog) reference. Copied on set.</summary>
    public byte[]? Transmog { get => _transmog; set => _transmog = Sized(value, 4); }

    /// <summary>Bit 4.</summary>
    public (byte Rarity, byte Extra)? Rarity { get; set; }

    /// <summary>Bit 5: gem socket; all zero when empty.</summary>
    public (uint GemId, byte Marker)? Gem { get; set; }

    public byte Flags => (byte)(
        (Level is null ? 0 : 0x01) | (Durability is null ? 0 : 0x02) | (_bit2 is null ? 0 : 0x04) |
        (_transmog is null ? 0 : 0x08) | (Rarity is null ? 0 : 0x10) | (Gem is null ? 0 : 0x20));

    /// <summary>A remembered but empty stack: count 0 and no fields.</summary>
    public bool IsGhost => Count == 0 && Flags == 0;

    public int EncodedLength
    {
        get
        {
            var n = HeaderLength;
            var f = Flags;
            for (var bit = 0; bit < FieldSizes.Length; bit++)
                if ((f >> bit & 1) != 0) n += FieldSizes[bit];
            return n;
        }
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
            throw new ArgumentException("Destination is too small for this record.", nameof(destination));
        destination[0] = Slot;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[1..], ItemId);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[5..], Count);
        destination[7] = Flags;
        var p = HeaderLength;
        if (Level is { } level) destination[p++] = level;
        if (Durability is { } d)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], d.Current);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[(p + 4)..], d.Max);
            p += 8;
        }
        if (_bit2 is not null) { _bit2.CopyTo(destination[p..]); p += 8; }
        if (_transmog is not null) { _transmog.CopyTo(destination[p..]); p += 4; }
        if (Rarity is { } r) { destination[p] = r.Rarity; destination[p + 1] = r.Extra; p += 2; }
        if (Gem is { } g) { BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], g.GemId); destination[p + 4] = g.Marker; }
    }

    /// <summary>
    /// Reads the record at <paramref name="at"/>, or returns null when the bytes there aren't one:
    /// the id fails <paramref name="isItem"/>, a flag bit has no known field size, a field runs past
    /// the end, or the count is 0 while fields are present (only plain records can be ghosts).
    /// </summary>
    public static ItemRecord? TryRead(ReadOnlySpan<byte> data, int at, Func<uint, bool> isItem)
    {
        ArgumentNullException.ThrowIfNull(isItem);
        if (at < 0 || at > data.Length - HeaderLength) return null;
        var id = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 1)..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 5)..]);
        var flags = data[at + 7];
        if ((flags & 0xC0) != 0 || (count == 0 && flags != 0) || !isItem(id)) return null;

        var r = new ItemRecord(data[at], id, count);
        var p = at + HeaderLength;
        for (var bit = 0; bit < FieldSizes.Length; bit++)
        {
            if ((flags >> bit & 1) == 0) continue;
            var size = FieldSizes[bit];
            if (p > data.Length - size) return null;
            var f = data.Slice(p, size);
            switch (bit)
            {
                case 0: r.Level = f[0]; break;
                case 1: r.Durability = (BinaryPrimitives.ReadUInt32LittleEndian(f), BinaryPrimitives.ReadUInt32LittleEndian(f[4..])); break;
                case 2: r._bit2 = f.ToArray(); break;
                case 3: r._transmog = f.ToArray(); break;
                case 4: r.Rarity = (f[0], f[1]); break;
                case 5: r.Gem = (BinaryPrimitives.ReadUInt32LittleEndian(f), f[4]); break;
            }
            p += size;
        }
        return r;
    }

    static byte[]? Sized(byte[]? value, int size, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (value is null) return null;
        if (value.Length != size) throw new ArgumentException($"{name} must be {size} bytes.", name);
        return value.ToArray();
    }
}
