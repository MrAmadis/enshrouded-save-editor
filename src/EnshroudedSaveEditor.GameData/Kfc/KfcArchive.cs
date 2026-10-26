using System.Buffers.Binary;
using ZstdSharp;

namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>A resource's key: its GUID, the qualified hash of its type, and its part index.</summary>
internal readonly record struct ResourceId(Guid Guid, uint TypeHash, uint Part);

/// <summary>
/// The game's resource database: <c>enshrouded.kfc</c> (the index), <c>enshrouded.kfc_resources</c> (typed resources in
/// zstd chunks) and <c>enshrouded_NNN.dat</c> (content blobs). Every file is opened read-only. See docs/research/kfc-format.md.
/// </summary>
internal sealed class KfcArchive : IDisposable
{
    const uint Magic = 0x3443464B; // "KFC4"
    const int CachedChunks = 4;

    readonly record struct Chunk(long FileOffset, int CompressedSize, long Offset, int Size);

    readonly string _dir;
    readonly Dictionary<ResourceId, (long Offset, int Size)> _resources = new();
    readonly Dictionary<uint, List<ResourceId>> _byType = new();
    readonly Dictionary<Guid, ResourceId> _byGuid = new();
    readonly Dictionary<UInt128, (long Offset, int Container)> _contents = new();
    readonly Chunk[] _chunks;
    readonly FileStream _resourceFile;
    readonly Dictionary<int, FileStream> _containers = new();
    readonly LinkedList<(int Index, byte[] Data)> _chunkCache = new();

    KfcArchive(string dir, byte[] kfc)
    {
        _dir = dir;
        if (kfc.Length < 0x90 || U32(kfc, 0) != Magic)
            throw new InvalidDataException("enshrouded.kfc is not a KFC4 file.");

        var (contentKeys, contentCount) = Location(kfc, 0x48);
        var (contentValues, _) = Location(kfc, 0x50);
        for (var i = 0; i < contentCount; i++)
        {
            var key = Key128(kfc, contentKeys + i * 16);
            var at = contentValues + i * 16;
            _contents[key] = (U32(kfc, at), BinaryPrimitives.ReadUInt16LittleEndian(kfc.AsSpan(at + 6)));
        }

        var (resourceKeys, resourceCount) = Location(kfc, 0x60);
        var (resourceValues, _) = Location(kfc, 0x68);
        for (var i = 0; i < resourceCount; i++)
        {
            var at = resourceKeys + i * 32;
            var id = new ResourceId(new Guid(kfc.AsSpan(at, 16)), U32(kfc, at + 16), U32(kfc, at + 20));
            _resources[id] = (U32(kfc, resourceValues + i * 8), (int)U32(kfc, resourceValues + i * 8 + 4));
            if (id.Part == 0) _byGuid.TryAdd(id.Guid, id);
            if (!_byType.TryGetValue(id.TypeHash, out var list)) _byType[id.TypeHash] = list = new List<ResourceId>();
            list.Add(id);
        }

        var (chunksAt, chunkCount) = Location(kfc, 0x88);
        _chunks = new Chunk[chunkCount];
        for (var i = 0; i < chunkCount; i++)
        {
            var at = chunksAt + i * 20;
            _chunks[i] = new Chunk(U32(kfc, at), (int)U32(kfc, at + 8), U32(kfc, at + 12), (int)U32(kfc, at + 16));
        }

        _resourceFile = OpenRead(Path.Combine(dir, "enshrouded.kfc_resources"));
    }

    /// <summary>Opens the archive in <paramref name="dir"/>, the game folder.</summary>
    public static KfcArchive Open(string dir) => new(dir, ReadAll(Path.Combine(dir, "enshrouded.kfc")));

    /// <summary>Every resource whose type has the qualified hash <paramref name="typeHash"/>.</summary>
    public IReadOnlyList<ResourceId> ResourcesOfType(uint typeHash) =>
        _byType.TryGetValue(typeHash, out var list) ? list : [];

    /// <summary>The first part of the resource with <paramref name="guid"/>, whatever its type; null when there is none.</summary>
    public ResourceId? Find(Guid guid) => _byGuid.TryGetValue(guid, out var id) ? id : null;

    /// <summary>A resource's bytes, or null when it isn't in the archive.</summary>
    public byte[]? ReadResource(ResourceId id)
    {
        if (!_resources.TryGetValue(id, out var r)) return null;
        var result = new byte[r.Size];
        var done = 0;
        while (done < r.Size)
        {
            var at = r.Offset + done;
            var index = ChunkAt(at);
            var chunk = ChunkData(index);
            var from = (int)(at - _chunks[index].Offset);
            var n = Math.Min(r.Size - done, chunk.Length - from);
            chunk.AsSpan(from, n).CopyTo(result.AsSpan(done));
            done += n;
        }
        return result;
    }

    /// <summary>The content blob a 16-byte content hash names (its first 4 bytes are the size); null when unknown.</summary>
    public byte[]? ReadContent(ReadOnlySpan<byte> contentHash)
    {
        if (!_contents.TryGetValue(Key128(contentHash), out var c)) return null;
        var size = BinaryPrimitives.ReadInt32LittleEndian(contentHash);
        if (!_containers.TryGetValue(c.Container, out var file))
            _containers[c.Container] = file = OpenRead(Path.Combine(_dir, $"enshrouded_{c.Container:000}.dat"));
        var data = new byte[size];
        file.Position = c.Offset;
        file.ReadExactly(data);
        return data;
    }

    public void Dispose()
    {
        _resourceFile.Dispose();
        foreach (var f in _containers.Values) f.Dispose();
    }

    int ChunkAt(long offset)
    {
        for (var i = 0; i < _chunks.Length; i++)
            if (offset >= _chunks[i].Offset && offset < _chunks[i].Offset + _chunks[i].Size) return i;
        throw new InvalidDataException($"Resource offset {offset} is outside every chunk.");
    }

    /// <summary>A decompressed chunk; the last few are kept, most recent first.</summary>
    byte[] ChunkData(int index)
    {
        for (var n = _chunkCache.First; n is not null; n = n.Next)
        {
            if (n.Value.Index != index) continue;
            _chunkCache.Remove(n);
            _chunkCache.AddFirst(n);
            return n.Value.Data;
        }
        var c = _chunks[index];
        var compressed = new byte[c.CompressedSize];
        _resourceFile.Position = c.FileOffset;
        _resourceFile.ReadExactly(compressed);
        var data = new byte[c.Size];
        try
        {
            using var d = new Decompressor();
            if (d.Unwrap(compressed, data) != c.Size) throw new InvalidDataException($"Resource chunk {index} is short.");
        }
        catch (ZstdException e)
        {
            throw new InvalidDataException($"Resource chunk {index} is corrupt: {e.Message}");
        }
        _chunkCache.AddFirst((index, data));
        if (_chunkCache.Count > CachedChunks) _chunkCache.RemoveLast();
        return data;
    }

    /// <summary>A location field: a 32-bit offset relative to the field itself, then a count.</summary>
    static (int At, int Count) Location(byte[] kfc, int field) => (field + (int)U32(kfc, field), (int)U32(kfc, field + 4));

    static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    static UInt128 Key128(byte[] b, int at) => Key128(b.AsSpan(at, 16));

    static UInt128 Key128(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt128LittleEndian(b);

    internal static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);

    internal static byte[] ReadAll(string path)
    {
        using var f = OpenRead(path);
        var data = new byte[f.Length];
        f.ReadExactly(data);
        return data;
    }
}
