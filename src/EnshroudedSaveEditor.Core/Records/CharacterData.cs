using System.Buffers.Binary;
using System.Text;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>The <c>character_data</c> record: slot index, signature, name, GUID and an opaque rest.</summary>
public sealed class CharacterData
{
    static readonly byte[] Signature = { 0x93, 0x7B, 0x40, 0xB6 };
    const int MaxNameBytes = 256;

    public byte SlotIndex { get; }
    public string Name { get; }
    public byte[] Guid { get; }
    readonly byte[] _rest;

    CharacterData(byte slot, string name, byte[] guid, byte[] rest)
    {
        SlotIndex = slot; Name = name; Guid = guid; _rest = rest;
    }

    public static CharacterData Parse(byte[] data)
    {
        if (data.Length < 9 || !data.AsSpan(1, 4).SequenceEqual(Signature))
            throw new KsdFormatException("Unexpected character_data layout.");
        var len = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(5));
        if (len > MaxNameBytes || 9L + len + 16 > data.Length)
            throw new KsdFormatException("Character name length is out of range.");
        var l = (int)len;
        var name = Encoding.UTF8.GetString(data, 9, l);
        var guid = data.AsSpan(9 + l, 16).ToArray();
        var rest = data.AsSpan(9 + l + 16).ToArray();
        return new CharacterData(data[0], name, guid, rest);
    }

    public byte[] ToBytes()
    {
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        var o = new byte[9 + nameBytes.Length + 16 + _rest.Length];
        o[0] = SlotIndex;
        Signature.CopyTo(o, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(5), (uint)nameBytes.Length);
        nameBytes.CopyTo(o, 9);
        Guid.CopyTo(o, 9 + nameBytes.Length);
        _rest.CopyTo(o, 9 + nameBytes.Length + 16);
        return o;
    }

    public CharacterData With(string? name = null, byte? slotIndex = null, byte[]? guid = null)
    {
        if (name is not null && Encoding.UTF8.GetByteCount(name) > MaxNameBytes)
            throw new ArgumentException("Name is too long.", nameof(name));
        if (guid is not null && guid.Length != 16)
            throw new ArgumentException("GUID must be 16 bytes.", nameof(guid));
        return new CharacterData(slotIndex ?? SlotIndex, name ?? Name, (guid ?? Guid).ToArray(), _rest);
    }
}
