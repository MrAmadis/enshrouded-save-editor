using System.Text.Json;

namespace EnshroudedSaveEditor.UI.Services;

/// <summary>User settings, kept as camelCase JSON in <see cref="EditorOptions.SettingsPath"/>.</summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The Enshrouded install folder, when the user picked one (the install reader, Task 19).</summary>
    public string? GameDir { get; set; }

    /// <summary>A saves folder the user picked; listed alongside the folders found automatically.</summary>
    public string? SaveRoot { get; set; }

    /// <summary>The saved settings, or defaults when the file is missing or can't be read.</summary>
    public static Settings Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
