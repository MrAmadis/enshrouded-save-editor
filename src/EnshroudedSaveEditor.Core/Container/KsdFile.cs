using System.Buffers.Binary;
using System.Text;

namespace EnshroudedSaveEditor.Core.Container;

/// <summary>
/// An Enshrouded "SV2C" save container: 48-byte header, contiguous section blocks,
/// then (at the next 16-byte boundary) a table of 20-byte entries followed by the names.
/// </summary>
public sealed class KsdFile
{
    private const int HeaderSize = 48;
    private const int EntrySize = 20;
    private const int MaxSections = 4096;
    private const long MaxUnixSeconds = 253402300799; // 9999-12-31T23:59:59Z
    private static readonly byte[] Magic = "SV2C"u8.ToArray();

    private readonly byte[] _header;
    private readonly List<KsdSection> _sections;
    private byte[] _guid;

    private KsdFile(byte[] header, List<KsdSection> sections)
    {
        _header = header;
        _sections = sections;
        Version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
        _guid = header.AsSpan(12, 16).ToArray();
        Revision = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28));
        var seconds = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32));
        if (seconds > (ulong)MaxUnixSeconds) throw new KsdFormatException("Save timestamp is out of range.");
        SavedAt = DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }

    public ushort Version { get; }

    public byte[] Guid
    {
        get => _guid;
        set => _guid = value is { Length: 16 } ? value : throw new ArgumentException("A GUID is 16 bytes.", nameof(value));
    }

    public uint Revision { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public IReadOnlyList<KsdSection> Sections => _sections;

    public KsdSection this[string name] =>
        Find(name) ?? throw new KeyNotFoundException($"No section named '{name}'.");

    public KsdSection? Find(string name) => _sections.Find(s => s.Name == name);

    public static KsdFile Parse(byte[] file)
    {
        if (file.Length < HeaderSize) throw new KsdFormatException("File is shorter than the 48-byte header.");
        if (!file.AsSpan(0, 4).SequenceEqual(Magic)) throw new KsdFormatException("Missing SV2C magic.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6)) != HeaderSize)
            throw new KsdFormatException("Unexpected header size.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8)) != (uint)file.Length)
            throw new KsdFormatException("Size field does not match the file length.");

        var tableAt = 0x28L + BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x28));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x2C));
        if (count > MaxSections) throw new KsdFormatException($"Implausible section count {count}.");
        if (tableAt + count * EntrySize > file.Length) throw new KsdFormatException("Section table lies past the end of the file.");

        var sections = new List<KsdSection>((int)count);
        for (var i = 0; i < count; i++)
        {
            var at = (int)tableAt + i * EntrySize;
            var nameOff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
            var nameLen = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
            var dataOff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 8));
            var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 12));
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 16));

            if (at + (long)nameOff + nameLen > file.Length) throw new KsdFormatException($"Entry {i}: name lies past the end of the file.");
            if ((long)dataOff + dataSize > file.Length) throw new KsdFormatException($"Entry {i}: block lies past the end of the file.");

            var name = Encoding.UTF8.GetString(file, at + (int)nameOff, (int)nameLen);
            sections.Add(new KsdSection(name, file.AsSpan((int)dataOff, (int)dataSize).ToArray(), checksum));
        }

        return new KsdFile(file.AsSpan(0, HeaderSize).ToArray(), sections);
    }

    public byte[] ToBytes()
    {
        var blocksEnd = HeaderSize + _sections.Sum(s => s.Block.Length);
        var tableAt = (blocksEnd + 15) & ~15;
        var names = _sections.Select(s => Encoding.UTF8.GetBytes(s.Name)).ToList();
        var total = tableAt + _sections.Count * EntrySize + names.Sum(n => n.Length);

        var output = new byte[total];
        _header.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), (uint)total);
        _guid.CopyTo(output, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(28), Revision);
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(32), (ulong)SavedAt.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x28), (uint)(tableAt - 0x28));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x2C), (uint)_sections.Count);

        var blockAt = HeaderSize;
        var namesAt = tableAt + _sections.Count * EntrySize;
        for (var i = 0; i < _sections.Count; i++)
        {
            var s = _sections[i];
            s.Block.Span.CopyTo(output.AsSpan(blockAt));

            var at = tableAt + i * EntrySize;
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at), (uint)(namesAt - at));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 4), (uint)names[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 8), (uint)blockAt);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 12), (uint)s.Block.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 16), s.StoredChecksum);
            names[i].CopyTo(output, namesAt);

            blockAt += s.Block.Length;
            namesAt += names[i].Length;
        }
        return output;
    }
}
