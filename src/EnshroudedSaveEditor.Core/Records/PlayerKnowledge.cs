using System.Buffers.Binary;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Core.Records;

/// <summary>The <c>player_knowledge</c> record: [u32 version][u32 n][n ids][n values].</summary>
public sealed class PlayerKnowledge
{
    readonly HashSet<uint> _ids;

    public uint Version { get; }
    public IReadOnlyList<(uint Id, uint Value)> Entries { get; }

    PlayerKnowledge(uint version, List<(uint Id, uint Value)> entries)
    {
        Version = version; Entries = entries; _ids = entries.Select(e => e.Id).ToHashSet();
    }

    public bool Contains(uint id) => _ids.Contains(id);

    /// <summary>
    /// A copy with each of <paramref name="ids"/> it doesn't know appended after the existing entries, with value 1 (a
    /// known Bool), as the game appends what a character learns. Known entries keep their values.
    /// </summary>
    public PlayerKnowledge Learn(IEnumerable<uint> ids)
    {
        var entries = Entries.ToList();
        var known = new HashSet<uint>(_ids);
        foreach (var id in ids)
            if (known.Add(id)) entries.Add((id, 1));
        return new PlayerKnowledge(Version, entries);
    }

    public static PlayerKnowledge Parse(byte[] data)
    {
        if (data.Length < 8) throw new KsdFormatException("Unexpected player_knowledge layout.");
        var n = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (8L + 8L * n != data.Length) throw new KsdFormatException("Unexpected player_knowledge layout.");
        var entries = new List<(uint Id, uint Value)>((int)n);
        for (var i = 0; i < n; i++)
            entries.Add((BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8 + 4 * i)),
                         BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8 + 4 * (int)n + 4 * i))));
        return new PlayerKnowledge(BinaryPrimitives.ReadUInt32LittleEndian(data), entries);
    }

    public byte[] ToBytes()
    {
        var n = Entries.Count;
        var o = new byte[8 + 8 * n];
        BinaryPrimitives.WriteUInt32LittleEndian(o, Version);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(4), (uint)n);
        for (var i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(8 + 4 * i), Entries[i].Id);
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(8 + 4 * n + 4 * i), Entries[i].Value);
        }
        return o;
    }
}
