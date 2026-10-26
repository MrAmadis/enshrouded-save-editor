using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace EnshroudedSaveEditor.GameData.Install;

/// <summary>Finds the Steam library folders. Strictly read-only.</summary>
public static partial class SteamLibraries
{
    [GeneratedRegex(@"""path""\s+""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();

    /// <summary>Every library folder Steam knows about, the Steam folder itself first.</summary>
    public static IReadOnlyList<string> Find() => Find(SteamRoot());

    /// <summary>The libraries listed in <c>steamapps\libraryfolders.vdf</c> under <paramref name="steamRoot"/>.</summary>
    public static IReadOnlyList<string> Find(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        try
        {
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                using var stream = new FileStream(vdf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                foreach (var p in Parse(reader.ReadToEnd()))
                    if (!libraries.Contains(p, StringComparer.OrdinalIgnoreCase)) libraries.Add(p);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return libraries;
    }

    /// <summary>The <c>path</c> values of a <c>libraryfolders.vdf</c> text, with VDF escapes resolved.</summary>
    public static IReadOnlyList<string> Parse(string vdf) =>
        PathEntry().Matches(vdf)
            .Select(m => m.Groups[1].Value.Replace(@"\\", @"\"))
            .Where(p => p.Length > 0)
            .ToList();

    static string SteamRoot()
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
}
