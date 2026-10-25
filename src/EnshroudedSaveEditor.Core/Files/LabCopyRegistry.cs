using System.Text.Json;

namespace EnshroudedSaveEditor.Core.Files;

public sealed record LabCopyEntry(string Path, string Guid);

/// <summary>
/// The list of character copies the Lab tool made. It is the write guard: a file may only be
/// modified when it is registered here and its header GUID still equals the registered one.
/// </summary>
public sealed class LabCopyRegistry(string registryPath)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnshroudedSaveEditor", "lab", "copies.json");

    public string RegistryPath { get; } = registryPath;

    public IReadOnlyList<LabCopyEntry> List() => Load();

    public void Register(string path, byte[] guid)
    {
        if (guid.Length != 16) throw new ArgumentException("A GUID is 16 bytes.", nameof(guid));
        var full = Normalize(path);
        var hex = Convert.ToHexString(guid);
        var list = Load().Where(e => !SamePath(e.Path, full)).ToList();
        list.Add(new LabCopyEntry(full, hex));

        var dir = Path.GetDirectoryName(Path.GetFullPath(RegistryPath))!;
        Directory.CreateDirectory(dir);
        var tmp = RegistryPath + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
        if (File.Exists(RegistryPath)) File.Replace(tmp, RegistryPath, null);
        else File.Move(tmp, RegistryPath);
    }

    public bool IsWritableCopy(string path)
    {
        var full = Normalize(path);
        var entry = Load().FirstOrDefault(e => SamePath(e.Path, full));
        if (entry is null) return false;
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[28];
            fs.ReadExactly(head);
            if (head[0] != (byte)'S' || head[1] != (byte)'V' || head[2] != (byte)'2' || head[3] != (byte)'C') return false;
            return string.Equals(Convert.ToHexString(head.AsSpan(12, 16)), entry.Guid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public void EnsureWritable(string path)
    {
        if (!IsWritableCopy(path))
            throw new InvalidOperationException($"Refusing to write {path}: not a Lab copy");
    }

    List<LabCopyEntry> Load()
    {
        if (!File.Exists(RegistryPath)) return [];
        try { return JsonSerializer.Deserialize<List<LabCopyEntry>>(File.ReadAllText(RegistryPath)) ?? []; }
        catch (JsonException) { return []; }
    }

    static string Normalize(string path) => Path.GetFullPath(path);
    static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
