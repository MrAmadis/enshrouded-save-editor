using System.Buffers.Binary;
using System.Text;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>The <c>character_data</c> record: slot index, storage tag, name, GUID and an opaque rest.</summary>
public sealed class CharacterData
{
    const int MaxNameBytes = 256;

    public byte SlotIndex { get; }
    /// <summary>Bytes 1-4. Steam characters carry 93 7B 40 B6, local ones a different tag (e.g. A8 23 B2 28).</summary>
    public byte[] StorageTag { get; }
    public string Name { get; }
    public byte[] Guid { get; }
    readonly byte[] _rest;

    CharacterData(byte slot, byte[] tag, string name, byte[] guid, byte[] rest)
    {
        SlotIndex = slot; StorageTag = tag; Name = name; Guid = guid; _rest = rest;
    }

    public static CharacterData Parse(byte[] data)
    {
        if (data.Length < 9)
            throw new KsdFormatException("Unexpected character_data layout.");
        var len = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(5));
        if (len > MaxNameBytes || 9L + len + 16 > data.Length)
            throw new KsdFormatException("Character name length is out of range.");
        var l = (int)len;
        var name = Encoding.UTF8.GetString(data, 9, l);
        var guid = data.AsSpan(9 + l, 16).ToArray();
        var rest = data.AsSpan(9 + l + 16).ToArray();
        return new CharacterData(data[0], data.AsSpan(1, 4).ToArray(), name, guid, rest);
    }

    public byte[] ToBytes()
    {
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        var o = new byte[9 + nameBytes.Length + 16 + _rest.Length];
        o[0] = SlotIndex;
        StorageTag.CopyTo(o, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(5), (uint)nameBytes.Length);
        nameBytes.CopyTo(o, 9);
        Guid.CopyTo(o, 9 + nameBytes.Length);
        _rest.CopyTo(o, 9 + nameBytes.Length + 16);
        return o;
    }

    public CharacterData With(string? name = null, byte? slotIndex = null, byte[]? guid = null, byte[]? storageTag = null)
    {
        if (name is not null && Encoding.UTF8.GetByteCount(name) > MaxNameBytes)
            throw new ArgumentException("Name is too long.", nameof(name));
        if (guid is not null && guid.Length != 16)
            throw new ArgumentException("GUID must be 16 bytes.", nameof(guid));
        if (storageTag is not null && storageTag.Length != 4)
            throw new ArgumentException("Storage tag must be 4 bytes.", nameof(storageTag));
        return new CharacterData(slotIndex ?? SlotIndex, (storageTag ?? StorageTag).ToArray(), name ?? Name, (guid ?? Guid).ToArray(), _rest);
    }
}
