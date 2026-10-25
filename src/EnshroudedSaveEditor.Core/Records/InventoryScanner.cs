using System.Buffers.Binary;
using System.Numerics;

namespace EnshroudedSaveEditor.Core.Records;

public enum ContainerKind { Equipped, Inventory, Extra }

/// <summary>How much of the inventory was recognised, and so which edits are safe.</summary>
public enum InventoryStatus
{
    /// <summary>The containers and their count bytes were recognised: add, move and remove are allowed.</summary>
    Full,
    /// <summary>Records were located but the layout wasn't recognised: only same-size edits are allowed.</summary>
    InPlaceOnly,
    /// <summary>No records were located: no inventory edits.</summary>
    NotFound,
}

public sealed record ItemContainer(ContainerKind Kind, List<ItemRecord> Records);

/// <summary>One piece of the <c>player_character</c> bytes from the status count to the end.</summary>
internal abstract record TailPart;

/// <summary>Bytes kept exactly as read.</summary>
internal sealed record OpaquePart(byte[] Bytes) : TailPart;

/// <summary>A u8 holding the container's record count.</summary>
internal sealed record CountPart(ItemContainer Container) : TailPart;

/// <summary>
/// The container's records, encoded one after another. <see cref="FixedCount"/> is set when no count
/// byte for this list is known, so the number of records can't change. <see cref="FixedLengths"/> is set
/// for in-place layouts, where every record must keep its encoded length. <see cref="IndexedSlots"/> is set
/// when each record's slot byte must be its index (the Equipped records of the strict layout).
/// </summary>
internal sealed record RecordsPart(ItemContainer Container, int? FixedCount, int[]? FixedLengths, bool IndexedSlots = false) : TailPart;

/// <param name="EquippedMask">The Equipped header's u32 field mask, or null when it wasn't located.</param>
internal sealed record InventoryScan(InventoryStatus Status, IReadOnlyList<ItemContainer> Containers, IReadOnlyList<TailPart> Parts,
    uint? EquippedMask);

/// <summary>
/// Locates the item containers after the skill list (layout in docs/research/player-character.md).
/// First tries the strict layout, which must account for every byte up to a fixed 22-byte tail; only that
/// gives <see cref="InventoryStatus.Full"/>. Otherwise falls back to the search from Kimikozi's lib/pc.js
/// (MIT; see THIRD_PARTY.md), which gives at most <see cref="InventoryStatus.InPlaceOnly"/>.
/// </summary>
internal static class InventoryScanner
{
    /// <summary>Status entry field sizes by bit of its u16 field mask (bits 1–4 observed; others unknown).</summary>
    static readonly Dictionary<int, int> StatusFieldSizes = new() { [1] = 4, [2] = 4, [3] = 1, [4] = 8 };

    const int StatusEntryHeader = 22;       // [u32 id][u64][u64][u16 field mask]

    /// <summary>
    /// <c>[u32 field mask]</c>: bits 0–15 are the Equipped positions, one item record each, in bit order; bits 16–18
    /// (header byte 2) each add a u16 after the records, in bit order. Bits 19–31 have never been seen.
    /// </summary>
    const int EquippedHeader = 4;
    const uint EquippedRecordBits = 0x0000_FFFF;
    const uint EquippedKnownBits = 0x0007_FFFF;
    const int EquippedExtraSize = 2;

    const int InventoryHeader = 4;          // [u8 5][u8 n2][u8 ?][u8 n3]
    const byte InventoryHeaderMarker = 0x05;
    static readonly byte[] TailStart = { 0x02, 0x00, 0x03, 0x00 };
    const int TailLength = 22;

    static readonly Func<uint, bool> AnyId = static _ => true;

    public static InventoryScan Scan(byte[] data, int statusAt, Func<uint, bool> isItem) =>
        Strict(data, statusAt) ?? Fallback(data, statusAt, isItem);

    /// <summary>The offset just past the status list, or null when an entry has an unknown field bit.</summary>
    static int? StatusEnd(byte[] d, int statusAt)
    {
        if (statusAt > d.Length - 4) return null;
        var n = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(statusAt));
        var p = statusAt + 4;
        for (var i = 0; i < n; i++)
        {
            if (p > d.Length - StatusEntryHeader) return null;
            var mask = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p + 20));
            p += StatusEntryHeader;
            for (var bit = 0; bit < 16; bit++)
            {
                if ((mask >> bit & 1) == 0) continue;
                if (!StatusFieldSizes.TryGetValue(bit, out var size)) return null;
                p += size;
            }
            if (p > d.Length) return null;
        }
        return p;
    }

    /// <summary>
    /// Status list; the Equipped header <c>[u32 field mask]</c> (see <see cref="EquippedHeader"/>), one record per set
    /// bit 0–15 with slot bytes 0…n−1, then one u16 per set bit 16–18; the inventory header <c>[u8 5][u8 n2][u8][u8 n3]</c>;
    /// n2 Inventory and n3 Extra records; a 22-byte tail starting <c>02 00 03 00</c> that ends the data. Item ids
    /// aren't checked: the layout itself is the check.
    /// </summary>
    static InventoryScan? Strict(byte[] d, int statusAt)
    {
        if (StatusEnd(d, statusAt) is not { } p || p > d.Length - EquippedHeader) return null;
        var mask = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
        if ((mask & ~EquippedKnownBits) != 0) return null;
        var eqAt = p + EquippedHeader;
        p = eqAt;
        if (ReadCount(d, ref p, BitOperations.PopCount(mask & EquippedRecordBits), AnyId) is not { } equipped) return null;
        for (var i = 0; i < equipped.Count; i++)
            if (equipped[i].Slot != i) return null;
        var eqEnd = p;

        var header = eqEnd + EquippedExtraSize * BitOperations.PopCount(mask & ~EquippedRecordBits);
        if (header > d.Length - InventoryHeader || d[header] != InventoryHeaderMarker) return null;
        p = header + InventoryHeader;
        if (ReadCount(d, ref p, d[header + 1], AnyId) is not { } inventory) return null;
        if (ReadCount(d, ref p, d[header + 3], AnyId) is not { } extra) return null;
        if (d.Length - p != TailLength || !d.AsSpan(p, TailStart.Length).SequenceEqual(TailStart)) return null;

        var eq = new ItemContainer(ContainerKind.Equipped, equipped);
        var inv = new ItemContainer(ContainerKind.Inventory, inventory);
        var ext = new ItemContainer(ContainerKind.Extra, extra);
        return new InventoryScan(InventoryStatus.Full, [eq, inv, ext],
        [
            Opaque(d, statusAt, eqAt),
            new RecordsPart(eq, equipped.Count, null, IndexedSlots: true),
            Opaque(d, eqEnd, header + 1),
            new CountPart(inv),
            Opaque(d, header + 2, header + 3),
            new CountPart(ext),
            new RecordsPart(inv, null, null),
            new RecordsPart(ext, null, null),
            Opaque(d, p, d.Length),
        ], mask);
    }

    /// <summary>
    /// Kimikozi's search: an Equipped run starting at slot 0 just after the status list, then an
    /// inventory header ending <c>[type][n2][type][n3]</c> within 4–12 bytes of it. Records must pass
    /// <paramref name="isItem"/>. Everything else stays opaque and every record keeps its size. The Equipped mask
    /// (for the records' positions) is taken from the 4 bytes before the run only when they fit it: byte 3 is 0 and
    /// bits 0–15 count exactly the run's records.
    /// </summary>
    static InventoryScan Fallback(byte[] d, int statusAt, Func<uint, bool> isItem)
    {
        var n = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(statusAt));
        var buffStart = statusAt + 4;
        var windows = new List<(int From, int To)>();
        if (StatusEnd(d, statusAt) is { } end) windows.Add((end, end + 8));
        windows.Add((buffStart + 27 * n, buffStart + 27 * n + 8));   // Kimikozi: entries are usually 27 bytes
        windows.Add((buffStart + 22 * n, buffStart + 64 * n + 16));

        List<ItemRecord>? equipped = null;
        var eqAt = -1;
        foreach (var (from, to) in windows)
        {
            for (var s = from; s <= Math.Min(to, d.Length - ItemRecord.HeaderLength) && equipped is null; s++)
            {
                var run = ReadRun(d, s, 64, isItem, out _);
                if (run.Count > 0 && run[0].Slot == 0) { equipped = run; eqAt = s; }
            }
            if (equipped is not null) break;
        }
        if (equipped is null) return new InventoryScan(InventoryStatus.NotFound, [], [Opaque(d, statusAt, d.Length)], null);

        uint? mask = null;
        if (eqAt - EquippedHeader >= buffStart)
        {
            var m = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(eqAt - EquippedHeader));
            if (m >> 24 == 0 && BitOperations.PopCount(m & EquippedRecordBits) == equipped.Count) mask = m;
        }

        var eqEnd = eqAt + equipped.Sum(r => r.EncodedLength);
        var eq = new ItemContainer(ContainerKind.Equipped, equipped);
        var parts = new List<TailPart> { Opaque(d, statusAt, eqAt), Fixed(eq) };
        var containers = new List<ItemContainer> { eq };
        var rest = eqEnd;

        for (var s = eqEnd + 4; s <= Math.Min(eqEnd + 12, d.Length - ItemRecord.HeaderLength); s++)
        {
            int n2 = d[s - 3], n3 = d[s - 1];
            if (n2 == 0) continue;
            var a = ReadRun(d, s, n2, isItem, out var aEnd);
            if (a.Count != n2) continue;
            var b = ReadRun(d, aEnd, n3, isItem, out var bEnd);
            if (b.Count != n3) continue;

            var inv = new ItemContainer(ContainerKind.Inventory, a);
            var ext = new ItemContainer(ContainerKind.Extra, b);
            containers.Add(inv); containers.Add(ext);
            parts.Add(Opaque(d, eqEnd, s));
            parts.Add(Fixed(inv));
            parts.Add(Fixed(ext));
            rest = bEnd;
            break;
        }
        parts.Add(Opaque(d, rest, d.Length));
        return new InventoryScan(InventoryStatus.InPlaceOnly, containers, parts, mask);
    }

    /// <summary>Exactly <paramref name="count"/> records with strictly increasing slots, or null.</summary>
    static List<ItemRecord>? ReadCount(byte[] d, ref int p, int count, Func<uint, bool> isItem)
    {
        var run = ReadRun(d, p, count, isItem, out var end);
        if (run.Count != count) return null;
        p = end;
        return run;
    }

    /// <summary>Up to <paramref name="limit"/> records from <paramref name="at"/>, while slots strictly increase.</summary>
    static List<ItemRecord> ReadRun(byte[] d, int at, int limit, Func<uint, bool> isItem, out int end)
    {
        var run = new List<ItemRecord>();
        end = at;
        while (run.Count < limit)
        {
            var r = ItemRecord.TryRead(d, end, isItem);
            if (r is null || (run.Count > 0 && r.Slot <= run[^1].Slot)) break;
            run.Add(r);
            end += r.EncodedLength;
        }
        return run;
    }

    static RecordsPart Fixed(ItemContainer c) =>
        new(c, c.Records.Count, c.Records.Select(r => r.EncodedLength).ToArray());

    static OpaquePart Opaque(byte[] d, int from, int to) => new(d.AsSpan(from, to - from).ToArray());
}
