using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>A 64-bit Windows executable held in memory, with addresses mapped to file offsets.</summary>
internal sealed class PeImage
{
    readonly record struct Section(string Name, uint Address, uint RawSize, uint RawOffset);

    readonly Section[] _sections;

    public byte[] Data { get; }
    public ulong ImageBase { get; }

    PeImage(byte[] data)
    {
        Data = data;
        if (data.Length < 0x40 || U16(0) != 0x5A4D) throw new InvalidDataException("Not an executable (no MZ header).");
        var pe = (int)U32(0x3C);
        if (pe <= 0 || pe + 24 > data.Length || U32(pe) != 0x4550) throw new InvalidDataException("Not a PE file.");
        int count = U16(pe + 6), optionalSize = U16(pe + 20);
        var optional = pe + 24;
        if (U16(optional) != 0x20B) throw new InvalidDataException("Not a 64-bit PE file.");
        ImageBase = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(optional + 24));
        _sections = new Section[count];
        for (var i = 0; i < count; i++)
        {
            var s = optional + optionalSize + i * 40;
            var name = Encoding.ASCII.GetString(data, s, 8).TrimEnd('\0');
            _sections[i] = new Section(name, U32(s + 12), U32(s + 16), U32(s + 20));
        }
    }

    public static PeImage Load(string path) => new(KfcArchive.ReadAll(path));

    /// <summary>The file range of the named section.</summary>
    public (int Start, int End) SectionRange(string name)
    {
        var s = _sections.FirstOrDefault(x => x.Name == name);
        if (s.Name is null) throw new InvalidDataException($"The executable has no {name} section.");
        return ((int)s.RawOffset, (int)(s.RawOffset + s.RawSize));
    }

    /// <summary>The file offset of a virtual address, or -1 when no section holds it.</summary>
    public int Offset(ulong va)
    {
        if (va < ImageBase) return -1;
        var rva = va - ImageBase;
        foreach (var s in _sections)
            if (rva >= s.Address && rva < s.Address + s.RawSize) return (int)(s.RawOffset + (rva - s.Address));
        return -1;
    }

    /// <summary>The virtual address of a file offset.</summary>
    public ulong Address(int offset)
    {
        foreach (var s in _sections)
            if (offset >= s.RawOffset && offset < s.RawOffset + s.RawSize) return ImageBase + s.Address + (uint)(offset - s.RawOffset);
        throw new InvalidDataException($"File offset {offset} is outside every section.");
    }

    /// <summary>The 8-byte-aligned file offsets in [start, end) whose 64-bit value is <paramref name="value"/>.</summary>
    public IEnumerable<int> FindQword(ulong value, int start, int end)
    {
        start = (start + 7) & ~7;
        var hits = new List<int>();
        var words = MemoryMarshal.Cast<byte, ulong>(Data.AsSpan(start, (end - start) & ~7));
        for (var i = 0; i < words.Length; i++)
            if (words[i] == value) hits.Add(start + i * 8);
        return hits;
    }

    public ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(offset));
    public uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(offset));
    public ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset));

    /// <summary>The string a (pointer, length) pair at <paramref name="offset"/> describes.</summary>
    public string String(int offset)
    {
        var at = Offset(U64(offset));
        var length = (int)U64(offset + 8);
        return at < 0 || length <= 0 ? "" : Encoding.UTF8.GetString(Data, at, length);
    }
}
