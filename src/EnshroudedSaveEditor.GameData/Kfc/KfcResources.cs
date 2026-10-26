namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>The install's resources as typed values: the archive read with the executable's type metadata.</summary>
internal sealed class KfcResources : IDisposable
{
    readonly KfcArchive _archive;

    public KfcResources(KfcArchive archive, KfcTypes types)
    {
        _archive = archive; Types = types;
    }

    /// <summary>Opens <c>enshrouded.kfc</c> and the type metadata of <c>enshrouded.exe</c> in the game folder <paramref name="dir"/>.</summary>
    public static KfcResources Open(string dir)
    {
        var types = KfcTypes.Load(Path.Combine(dir, "enshrouded.exe"));
        return new KfcResources(KfcArchive.Open(dir), types);
    }

    public KfcTypes Types { get; }

    /// <summary>Every resource of the type called <paramref name="typeName"/>.</summary>
    public IEnumerable<KfcValue> All(string typeName)
    {
        var type = Types[typeName];
        foreach (var id in _archive.ResourcesOfType(type.Hash))
            yield return new KfcValue(Types, type, _archive.ReadResource(id)!);
    }

    /// <summary>The first resource of the type called <paramref name="typeName"/> (registries have exactly one).</summary>
    public KfcValue First(string typeName) =>
        All(typeName).Cast<KfcValue?>().FirstOrDefault() ?? throw new InvalidDataException($"The install has no {typeName}.");

    /// <summary>
    /// The resource an object reference points to: the one of the referenced type, else any resource with that GUID (a
    /// subtype), typed as stored. Null for an empty or unknown reference.
    /// </summary>
    public KfcValue? Resolve(KfcValue reference)
    {
        var guid = reference.Guid;
        if (guid == Guid.Empty) return null;
        if (reference.Type.Inner is { } target && _archive.ReadResource(new ResourceId(guid, target.Hash, 0)) is { } data)
            return new KfcValue(Types, target, data);
        if (_archive.Find(guid) is not { } id || Types.Find(id.TypeHash) is not { } type) return null;
        return new KfcValue(Types, type, _archive.ReadResource(id)!);
    }

    /// <summary>
    /// The resource of the type called <paramref name="typeName"/> with <paramref name="guid"/> (its first part, the only
    /// one icon textures have), or null when there is none.
    /// </summary>
    public KfcValue? Find(Guid guid, string typeName)
    {
        var type = Types[typeName];
        return _archive.ReadResource(new ResourceId(guid, type.Hash, 0)) is { } data ? new KfcValue(Types, type, data) : null;
    }

    /// <summary>The content blob a content hash (16 bytes) names, read as the type called <paramref name="typeName"/>.</summary>
    public KfcValue? Content(KfcValue hash, string typeName) =>
        _archive.ReadContent(hash.Bytes16) is { } data ? new KfcValue(Types, Types[typeName], data) : null;

    /// <summary>The bytes of the content blob a content hash (16 bytes) names; null when it isn't in the archive.</summary>
    public byte[]? ContentData(KfcValue hash) => _archive.ReadContent(hash.Bytes16);

    public void Dispose() => _archive.Dispose();
}
