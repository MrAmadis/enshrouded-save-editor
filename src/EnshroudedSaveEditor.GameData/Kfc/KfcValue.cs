using System.Buffers.Binary;
using System.Text;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// A typed value inside a resource's bytes. Blob arrays, strings, optionals and variants hold a 32-bit offset relative to
/// the offset field itself. Reading past the data throws <see cref="InvalidDataException"/>.
/// </summary>
internal readonly struct KfcValue
{
    readonly KfcTypes _types;
    readonly byte[] _data;
    readonly int _at;

    public KfcValue(KfcTypes types, KfcType type, byte[] data, int at = 0)
    {
        _types = types; _data = data; _at = at; Type = type.Resolved;
    }

    public KfcType Type { get; }

    /// <summary>A struct field; throws when the struct has no such field.</summary>
    public KfcValue this[string field] =>
        Get(field) ?? throw new InvalidDataException($"{Type.QualifiedName} has no field {field}.");

    /// <summary>A struct field, or null when the struct has no such field.</summary>
    public KfcValue? Get(string field) =>
        Type.Field(field) is { } f ? new KfcValue(_types, f.Type, _data, _at + f.Offset) : null;

    /// <summary>Any integer, enum or bitmask, widened.</summary>
    public long Int => Type.Kind switch
    {
        KfcKind.Bool or KfcKind.UInt8 => Span(1)[0],
        KfcKind.SInt8 => (sbyte)Span(1)[0],
        KfcKind.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(Span(2)),
        KfcKind.SInt16 => BinaryPrimitives.ReadInt16LittleEndian(Span(2)),
        KfcKind.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(Span(4)),
        KfcKind.SInt32 => BinaryPrimitives.ReadInt32LittleEndian(Span(4)),
        KfcKind.UInt64 or KfcKind.SInt64 => BinaryPrimitives.ReadInt64LittleEndian(Span(8)),
        KfcKind.Enum or KfcKind.Bitmask8 or KfcKind.Bitmask16 or KfcKind.Bitmask32 or KfcKind.Bitmask64 => Type.Size switch
        {
            1 => Span(1)[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(Span(2)),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(Span(4)),
            _ => BinaryPrimitives.ReadInt64LittleEndian(Span(8)),
        },
        KfcKind.Struct when Type.Fields.Count == 1 => this[Type.Fields[0].Name].Int,
        _ => throw Wrong("an integer"),
    };

    public uint UInt => (uint)Int;

    public double Float => Type.Kind switch
    {
        KfcKind.Float32 => BinaryPrimitives.ReadSingleLittleEndian(Span(4)),
        KfcKind.Float64 => BinaryPrimitives.ReadDoubleLittleEndian(Span(8)),
        KfcKind.Struct when Type.Fields.Count == 1 => this[Type.Fields[0].Name].Float,
        _ => throw Wrong("a float"),
    };

    /// <summary>The name of an enum value; the number when it has none.</summary>
    public string EnumName
    {
        get
        {
            if (Type.Kind != KfcKind.Enum) throw Wrong("an enum");
            var v = (ulong)Int;
            foreach (var (name, value) in Type.EnumValues)
                if (value == v) return name;
            return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public string String
    {
        get
        {
            if (Type.Kind != KfcKind.BlobString) throw Wrong("a string");
            var (at, length) = Blob();
            return length == 0 ? "" : Encoding.UTF8.GetString(Slice(at, length));
        }
    }

    /// <summary>The 16 bytes of a GUID, object reference or content hash.</summary>
    public byte[] Bytes16 => Type.Kind is KfcKind.Guid or KfcKind.ObjectReference || Type.Size == 16
        ? Span(16).ToArray()
        : throw Wrong("16 bytes");

    public Guid Guid => new(Bytes16);

    /// <summary>The elements of a blob or static array.</summary>
    public IReadOnlyList<KfcValue> Elements
    {
        get
        {
            var element = Type.Inner ?? throw Wrong("an array");
            var (at, count) = Type.Kind switch
            {
                KfcKind.BlobArray => Blob(),
                KfcKind.StaticArray => (_at, Type.Count),
                _ => throw Wrong("an array"),
            };
            var size = element.Resolved.Size;
            if (count < 0 || (long)at + (long)count * size > _data.Length) throw Truncated();
            var list = new KfcValue[count];
            for (var i = 0; i < count; i++) list[i] = new KfcValue(_types, element, _data, at + i * size);
            return list;
        }
    }

    /// <summary>The value of a blob optional (null when empty) or of a blob variant (its stored type, or null when empty).</summary>
    public KfcValue? Value
    {
        get
        {
            switch (Type.Kind)
            {
                case KfcKind.BlobOptional:
                    var offset = BinaryPrimitives.ReadUInt32LittleEndian(Span(4));
                    return offset == 0 ? null : new KfcValue(_types, Type.Inner ?? throw Wrong("an optional with a type"), _data, _at + (int)offset);
                case KfcKind.BlobVariant:
                    var hash = BinaryPrimitives.ReadUInt32LittleEndian(Span(4));
                    var rel = BinaryPrimitives.ReadUInt32LittleEndian(Slice(_at + 4, 4));
                    if (hash == 0 && rel == 0) return null;
                    var type = _types.Find(hash) ?? throw new InvalidDataException($"Unknown variant type {hash:x8} in {Type}.");
                    return new KfcValue(_types, type, _data, _at + 4 + (int)rel);
                default:
                    throw Wrong("an optional or variant");
            }
        }
    }

    public override string ToString() => $"{Type.QualifiedName} @{_at}";

    (int At, int Count) Blob()
    {
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(Span(8));
        var count = BinaryPrimitives.ReadInt32LittleEndian(Slice(_at + 4, 4));
        return offset == 0 ? (0, 0) : (_at + (int)offset, count);
    }

    ReadOnlySpan<byte> Span(int n) => Slice(_at, n);

    ReadOnlySpan<byte> Slice(int at, int n) =>
        at >= 0 && n >= 0 && (long)at + n <= _data.Length ? _data.AsSpan(at, n) : throw Truncated();

    InvalidDataException Wrong(string what) => new($"{Type.QualifiedName} ({Type.Kind}) is not {what}.");

    InvalidDataException Truncated() => new($"{Type.QualifiedName} runs past the end of its data.");
}
