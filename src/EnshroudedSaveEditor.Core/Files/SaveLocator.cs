using System.Text.RegularExpressions;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;
using Microsoft.Win32;

namespace EnshroudedSaveEditor.Core.Files;

public enum CharacterStatus { Ok, Damaged, Unsupported }

public sealed record CharacterInfo(
    string Path, string FileName, int SlotIndex, string Name, int Level, uint Xp,
    uint Revision, DateTimeOffset SavedAt, CharacterStatus Status, string? StatusMessage);

public static class SupportedVersions
{
    public const ushort Container = 2;

    public static IReadOnlyDictionary<string, uint> Sections { get; } = new Dictionary<string, uint>
    {
        ["character_data"] = 3,
        ["player_character"] = 5,
        ["player_knowledge"] = 100,
        ["voiceover"] = 1,
        ["fogofwar"] = 1,
    };
}

/// <summary>Finds and summarises character saves. Strictly read-only.</summary>
public sealed partial class SaveLocator
{
    const string AppId = "1203620";

    [GeneratedRegex(@"^character(\d+)\.ksd$")]
    private static partial Regex CharacterFile();

    public static IReadOnlyList<string> DefaultRoots()
    {
        var roots = new List<string>();
        try
        {
            var users = System.IO.Path.Combine(SteamPath(), "userdata");
            if (Directory.Exists(users))
                foreach (var u in Directory.EnumerateDirectories(users))
                {
                    var remote = System.IO.Path.Combine(u, AppId, "remote");
                    if (Directory.Exists(remote)) roots.Add(remote);
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        try
        {
            var profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                var sg = System.IO.Path.Combine(profile, "Saved Games", "Enshrouded");
                if (Directory.Exists(sg) && Directory.EnumerateFiles(sg, "character*.ksd").Any())
                    roots.Add(sg);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return roots;
    }

    static string SteamPath()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string s && s.Length > 0)
                    return s.Replace('/', '\\');
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        }
        return @"C:\Program Files (x86)\Steam";
    }

    public IReadOnlyList<CharacterInfo> Scan(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<CharacterInfo>();
        var result = new List<CharacterInfo>();
        foreach (var path in Directory.EnumerateFiles(root, "*.ksd"))
        {
            var name = System.IO.Path.GetFileName(path);
            if (!CharacterFile().IsMatch(name)) continue;
            result.Add(Inspect(path, name));
        }
        return result
            .OrderByDescending(c => c.SavedAt)
            .ThenBy(c => c.FileName, StringComparer.Ordinal)
            .ToList();
    }

    static CharacterInfo Inspect(string path, string fileName)
    {
        CharacterInfo Fail(CharacterStatus s, string msg, KsdFile? f = null) =>
            new(path, fileName, -1, "", 0, 0, f?.Revision ?? 0, f?.SavedAt ?? DateTimeOffset.MinValue, s, msg);

        KsdFile file;
        try { file = KsdFile.Parse(File.ReadAllBytes(path)); }
        catch (Exception e) when (e is KsdFormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        { return Fail(CharacterStatus.Damaged, e.Message); }

        try
        {
            if (file.Version != SupportedVersions.Container)
                return Fail(CharacterStatus.Unsupported, $"Unsupported container version {file.Version}.", file);

            foreach (var secName in SupportedVersions.Sections.Keys)
            {
                var s = file.Find(secName);
                if (s is null)
                {
                    // Optional sections (e.g. voiceover) are absent from some saves.
                    if (secName is "character_data" or "player_character")
                        return Fail(CharacterStatus.Damaged, $"Missing section '{secName}'.", file);
                    continue;
                }
                if (!s.ChecksumValid) return Fail(CharacterStatus.Damaged, $"Checksum mismatch in section '{secName}'.", file);
            }
            foreach (var (secName, ver) in SupportedVersions.Sections)
            {
                var s = file.Find(secName);
                if (s is not null && s.DataVersion != ver)
                    return Fail(CharacterStatus.Unsupported, $"Section '{secName}' has unsupported data version {s.DataVersion} (expected {ver}).", file);
            }

            var cd = CharacterData.Parse(file["character_data"].GetData());
            var pc = PlayerCharacter.Parse(file["player_character"].GetData(), _ => false);
            return new CharacterInfo(path, fileName, cd.SlotIndex, cd.Name, (int)pc.Level, pc.Xp,
                file.Revision, file.SavedAt, CharacterStatus.Ok, null);
        }
        catch (UnsupportedLayoutException e) { return Fail(CharacterStatus.Unsupported, e.Message, file); }
        catch (Exception e) when (e is KsdFormatException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
        { return Fail(CharacterStatus.Damaged, e.Message, file); }
    }
}
