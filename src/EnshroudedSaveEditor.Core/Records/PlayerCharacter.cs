using System.Buffers.Binary;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>Thrown when a player_character record does not match any layout we understand.</summary>
public sealed class UnsupportedLayoutException : KsdFormatException
{
    public UnsupportedLayoutException(string message) : base(message) { }
}

public readonly record struct SkillEntry(uint NodeId, byte Rank);

/// <summary>
/// The <c>player_character</c> record: opaque 58-byte prefix, level, XP, skills, then the status list
/// (kept opaque) and the item containers (layout in docs/research/player-character.md). Every byte that
/// isn't a located record or count byte is kept as read.
/// </summary>
public sealed class PlayerCharacter
{
    public const int LevelOffset = 58;

    readonly byte[] _prefix;
    readonly IReadOnlyList<TailPart> _tail;

    public uint Level { get; set; }
    public uint Xp { get; set; }
    public List<SkillEntry> Skills { get; }

    public InventoryStatus InventoryStatus { get; }

    /// <summary>The located containers; empty when <see cref="InventoryStatus.NotFound"/>.</summary>
    public IReadOnlyList<ItemContainer> Containers { get; }

    public ItemContainer? Container(ContainerKind k) => Containers.FirstOrDefault(c => c.Kind == k);

    /// <summary>
    /// The Equipped header's u32 field mask: bits 0–15 are the Equipped positions that hold a record, bits 16–18 the
    /// u16 fields after the records (meaning unknown, kept as read). Null when it wasn't located. It is read only:
    /// the Equipped record count can't change.
    /// </summary>
    public uint? EquippedMask { get; }

    /// <summary>
    /// The Equipped position (0–15, the mask's set bits in order) of each Equipped record, by record index; null when
    /// the mask wasn't located.
    /// </summary>
    public IReadOnlyList<int>? EquippedPositions { get; }

    PlayerCharacter(byte[] prefix, uint level, uint xp, List<SkillEntry> skills, InventoryScan inventory)
    {
        _prefix = prefix; Level = level; Xp = xp; Skills = skills;
        InventoryStatus = inventory.Status; Containers = inventory.Containers; _tail = inventory.Parts;
        EquippedMask = inventory.EquippedMask;
        if (inventory.EquippedMask is { } mask)
            EquippedPositions = Enumerable.Range(0, 16).Where(bit => (mask >> bit & 1) != 0).ToArray();
    }

    /// <summary>
    /// Parses the record. <paramref name="isItem"/> vets records only when the strict layout isn't
    /// recognised and the fallback search runs; the strict layout keeps ids missing from the reference data.
    /// </summary>
    public static PlayerCharacter Parse(byte[] data, Func<uint, bool> isItem)
    {
        ArgumentNullException.ThrowIfNull(isItem);
        if (data.Length < LevelOffset + 16) throw new UnsupportedLayoutException("player_character is too short.");
        for (var o = 38; o <= 54; o += 4)
        {
            var f = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(o));
            if (!float.IsFinite(f) || f < 0f || f > 50f)
                throw new UnsupportedLayoutException($"Unexpected player_character header (float at {o}).");
        }
        var level = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LevelOffset));
        if (level < 1 || level > 200) throw new UnsupportedLayoutException("Level is out of range.");
        var xp = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LevelOffset + 4));
        var n = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(LevelOffset + 8));
        if (n > 400) throw new UnsupportedLayoutException("Skill count is out of range.");
        var statusAt = LevelOffset + 12 + 5 * (int)n;
        if (statusAt + 4 > data.Length) throw new UnsupportedLayoutException("Skill list runs past the end of the data.");
        var skills = new List<SkillEntry>((int)n);
        for (var i = 0; i < n; i++)
        {
            var p = LevelOffset + 12 + 5 * i;
            var rank = data[p + 4];
            if (rank > 15) throw new UnsupportedLayoutException("Skill rank is out of range.");
            skills.Add(new SkillEntry(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)), rank));
        }
        var status = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(statusAt));
        if (status > 64) throw new UnsupportedLayoutException("Status list is out of range.");
        return new PlayerCharacter(data.AsSpan(0, LevelOffset).ToArray(), level, xp, skills,
            InventoryScanner.Scan(data, statusAt, isItem));
    }

    /// <summary>
    /// Writes the record, re-encoding item records where they were found. Throws
    /// <see cref="InvalidOperationException"/> for a change the located layout can't express: a record
    /// count change without a known count byte (Equipped, or any list when not <see cref="InventoryStatus.Full"/>),
    /// a size change in an in-place layout, slots out of order, an Equipped slot byte that isn't its record index
    /// (<see cref="InventoryStatus.Full"/>), or a record the reader would reject.
    /// </summary>
    public byte[] ToBytes()
    {
        var tailLength = 0;
        foreach (var part in _tail) tailLength += Measure(part);
        var o = new byte[LevelOffset + 12 + 5 * Skills.Count + tailLength];
        _prefix.CopyTo(o, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(LevelOffset), Level);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(LevelOffset + 4), Xp);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(LevelOffset + 8), (uint)Skills.Count);
        for (var i = 0; i < Skills.Count; i++)
        {
            var p = LevelOffset + 12 + 5 * i;
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(p), Skills[i].NodeId);
            o[p + 4] = Skills[i].Rank;
        }
        var at = LevelOffset + 12 + 5 * Skills.Count;
        foreach (var part in _tail) at += Write(part, o.AsSpan(at));
        return o;
    }

    /// <summary>Checks that <paramref name="part"/> can be written as it stands and returns its length.</summary>
    static int Measure(TailPart part)
    {
        switch (part)
        {
            case OpaquePart p:
                return p.Bytes.Length;
            case CountPart c:
                if (c.Container.Records.Count > byte.MaxValue)
                    throw new InvalidOperationException($"{c.Container.Kind} holds more than {byte.MaxValue} records.");
                return 1;
            case RecordsPart r:
                var records = r.Container.Records;
                if (r.FixedCount is { } count && records.Count != count)
                    throw new InvalidOperationException($"{r.Container.Kind} records can't be added or removed in this save.");
                var length = 0;
                for (var i = 0; i < records.Count; i++)
                {
                    var rec = records[i];
                    if (i > 0 && rec.Slot <= records[i - 1].Slot)
                        throw new InvalidOperationException($"{r.Container.Kind} slots must be in increasing order.");
                    if (r.IndexedSlots && rec.Slot != i)
                        throw new InvalidOperationException($"{r.Container.Kind} record {i} must have slot byte {i}.");
                    if (rec.Count == 0 && rec.Flags != 0)
                        throw new InvalidOperationException($"{r.Container.Kind} slot {rec.Slot}: a record with fields needs a count of at least 1.");
                    if (r.FixedLengths is { } lengths && rec.EncodedLength != lengths[i])
                        throw new InvalidOperationException($"{r.Container.Kind} slot {rec.Slot}: only same-size edits are possible in this save.");
                    length += rec.EncodedLength;
                }
                return length;
            default:
                throw new InvalidOperationException($"Unknown part {part.GetType().Name}.");
        }
    }

    static int Write(TailPart part, Span<byte> o)
    {
        switch (part)
        {
            case OpaquePart p:
                p.Bytes.CopyTo(o);
                return p.Bytes.Length;
            case CountPart c:
                o[0] = (byte)c.Container.Records.Count;
                return 1;
            case RecordsPart r:
                var at = 0;
                foreach (var rec in r.Container.Records)
                {
                    rec.WriteTo(o[at..]);
                    at += rec.EncodedLength;
                }
                return at;
            default:
                throw new InvalidOperationException($"Unknown part {part.GetType().Name}.");
        }
    }
}
