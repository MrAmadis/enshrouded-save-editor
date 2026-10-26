using System.Text;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>How a type's value is laid out (the executable's primitive-type numbers).</summary>
internal enum KfcKind : byte
{
    None, Bool, UInt8, SInt8, UInt16, SInt16, UInt32, SInt32, UInt64, SInt64, Float32, Float64, Enum,
    Bitmask8, Bitmask16, Bitmask32, Bitmask64, Typedef, Struct, StaticArray, DsArray, DsString, DsOptional, DsVariant,
    BlobArray, BlobString, BlobOptional, BlobVariant, ObjectReference, Guid,
}

internal sealed record KfcField(string Name, KfcType Type, int Offset);

/// <summary>
/// One type from the executable's reflection table. <see cref="Inner"/> is the base struct, the aliased type, the element
/// type or the referenced type, depending on <see cref="Kind"/>. <see cref="Count"/> is the field count of a struct or
/// enum and the length of a static array.
/// </summary>
internal sealed class KfcType
{
    internal KfcType(string name, string qualifiedName, KfcKind kind, int size, int count, uint hash)
    {
        Name = name; QualifiedName = qualifiedName; Kind = kind; Size = size; Count = count; Hash = hash;
    }

    public string Name { get; }
    public string QualifiedName { get; }
    public KfcKind Kind { get; }
    public int Size { get; }
    public int Count { get; }

    /// <summary>The qualified hash: FNV-1a of <see cref="QualifiedName"/>; resource ids carry it.</summary>
    public uint Hash { get; }

    public KfcType? Inner { get; internal set; }
    public IReadOnlyList<KfcField> Fields { get; internal set; } = [];
    public IReadOnlyList<(string Name, ulong Value)> EnumValues { get; internal set; } = [];

    /// <summary>The field called <paramref name="name"/>, searching the base structs too; null when there is none.</summary>
    public KfcField? Field(string name)
    {
        for (var t = this; t is not null; t = t.Kind == KfcKind.Struct ? t.Inner : null)
            foreach (var f in t.Fields)
                if (f.Name == name) return f;
        return null;
    }

    /// <summary>This type with typedefs resolved.</summary>
    public KfcType Resolved => Kind == KfcKind.Typedef && Inner is not null ? Inner.Resolved : this;

    public override string ToString() => QualifiedName;
}

/// <summary>The type metadata the game's executable carries for every resource type. See docs/research/kfc-format.md.</summary>
internal sealed class KfcTypes
{
    const int TypeSize = 0x90, FieldSize = 0x30, EnumValueSize = 0x28;

    readonly Dictionary<uint, KfcType> _byHash = new();
    readonly Dictionary<string, KfcType> _byName = new();

    public IReadOnlyList<KfcType> All { get; }

    /// <summary>Throws <see cref="InvalidDataException"/> when a typedef or base-struct chain comes back round.</summary>
    internal KfcTypes(List<KfcType> all)
    {
        All = all;
        foreach (var t in all)
        {
            _byHash.TryAdd(t.Hash, t);
            _byName.TryAdd(t.QualifiedName, t);
        }
        foreach (var t in all) RejectCycle(t);
    }

    /// <summary>
    /// <see cref="KfcType.Resolved"/> and <see cref="KfcType.Field"/> follow <see cref="KfcType.Inner"/> from typedefs and
    /// structs. In valid metadata that chain ends; misread metadata could make it loop forever.
    /// </summary>
    static void RejectCycle(KfcType type)
    {
        var seen = new HashSet<KfcType>();
        for (var t = type; t is { Kind: KfcKind.Typedef or KfcKind.Struct }; t = t.Inner)
            if (!seen.Add(t)) throw new InvalidDataException($"The type {type.QualifiedName} is based on itself.");
    }

    public KfcType? Find(uint hash) => _byHash.GetValueOrDefault(hash);

    public KfcType this[string qualifiedName] =>
        _byName.GetValueOrDefault(qualifiedName) ?? throw new KeyNotFoundException($"The executable has no type {qualifiedName}.");

    /// <summary>FNV-1a, 32 bits: the game's name and GUID hash.</summary>
    public static uint Fnv1a(ReadOnlySpan<byte> data)
    {
        var h = 0x811C9DC5u;
        foreach (var b in data) h = (h ^ b) * 0x01000193;
        return h;
    }

    /// <summary>Reads the reflection table from <c>enshrouded.exe</c>.</summary>
    public static KfcTypes Load(string exePath) => Read(PeImage.Load(exePath));

    static KfcTypes Read(PeImage pe)
    {
        var (table, count) = FindTable(pe);
        var addresses = new ulong[count];
        var index = new Dictionary<ulong, int>();
        for (var i = 0; i < count; i++)
        {
            addresses[i] = pe.U64(table + i * 8);
            index.TryAdd(addresses[i], i);
        }

        var types = new List<KfcType>(count);
        foreach (var va in addresses)
        {
            var at = Required(pe, va);
            types.Add(new KfcType(pe.String(at), pe.String(at + 0x20), (KfcKind)pe.Data[at + 0x4C],
                (int)pe.U32(at + 0x40), (int)pe.U32(at + 0x48), pe.U32(at + 0x50)));
        }

        KfcType? Ref(int at) => index.TryGetValue(pe.U64(at), out var i) ? types[i] : null;

        for (var i = 0; i < count; i++)
        {
            var at = Required(pe, addresses[i]);
            var t = types[i];
            t.Inner = Ref(at + 0x38);
            if (pe.U64(at + 0x58) is var fields and not 0)
            {
                var f = Required(pe, fields);
                t.Fields = Enumerable.Range(0, t.Count).Select(k => f + k * FieldSize)
                    .Select(x => new KfcField(pe.String(x), Ref(x + 0x10) ?? throw new InvalidDataException(
                        $"Field {pe.String(x)} of {t.QualifiedName} has an unknown type."), (int)pe.U64(x + 0x18)))
                    .ToList();
            }
            if (pe.U64(at + 0x60) is var values and not 0)
            {
                var e = Required(pe, values);
                t.EnumValues = Enumerable.Range(0, t.Count).Select(k => e + k * EnumValueSize)
                    .Select(x => (pe.String(x), pe.U64(x + 0x10))).ToList();
            }
        }
        return new KfcTypes(types);
    }

    static int Required(PeImage pe, ulong va)
    {
        var at = pe.Offset(va);
        if (at < 0 || at + TypeSize > pe.Data.Length) throw new InvalidDataException($"Address {va:x} is outside the executable.");
        return at;
    }

    /// <summary>
    /// Finds the table of type pointers: it starts with the types <c>BlobString</c> and <c>uint32</c>, and a
    /// (pointer, count) pair points to it.
    /// </summary>
    static (int At, int Count) FindTable(PeImage pe)
    {
        var (start, _) = pe.SectionRange(".rdata");
        var end = pe.Data.Length;
        foreach (var blob in TypesNamed(pe, "BlobString", start, end))
            foreach (var u32 in TypesNamed(pe, "uint32", start, end))
                foreach (var entry in pe.FindQword(blob, start, end))
                {
                    if (pe.U64(entry + 8) != u32) continue;
                    foreach (var p in pe.FindQword(pe.Address(entry), start, end))
                    {
                        var count = pe.U64(p + 8);
                        if (count is > 2 and < 1_000_000 && entry + (long)count * 8 <= end) return (entry, (int)count);
                    }
                }
        throw new InvalidDataException("The executable's type table was not found.");
    }

    /// <summary>The addresses of type records whose name is <paramref name="name"/> (a name pointer then its length).</summary>
    static IEnumerable<ulong> TypesNamed(PeImage pe, string name, int start, int end)
    {
        var needle = Encoding.ASCII.GetBytes("\0" + name + "\0");
        var data = pe.Data.AsSpan(start, end - start);
        var found = new List<ulong>();
        for (var from = 0; ;)
        {
            var i = data[from..].IndexOf(needle);
            if (i < 0) break;
            var text = pe.Address(start + from + i + 1);
            foreach (var at in pe.FindQword(text, start, end))
                if (pe.U64(at + 8) == (ulong)name.Length) found.Add(pe.Address(at));
            from += i + 1;
        }
        return found;
    }
}
