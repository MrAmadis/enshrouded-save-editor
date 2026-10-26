using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Files;

/// <summary>Makes a new character file from an existing one. Never modifies or overwrites any existing file.</summary>
public static partial class CharacterCopier
{
    [GeneratedRegex(@"^character(\d+)\.ksd$", RegexOptions.IgnoreCase)]
    private static partial Regex CharacterFile();

    public static string Copy(string remoteDir, string fromFile, string newName, LabCopyRegistry registry)
    {
        var sourcePath = Path.IsPathRooted(fromFile) ? fromFile : Path.Combine(remoteDir, fromFile);
        return CopyTo(sourcePath, remoteDir, newName, registry);
    }

    /// <summary>
    /// Reads <paramref name="sourcePath"/> (read-only) and writes it as the next free character{N}.ksd in
    /// <paramref name="targetDir"/> with a new GUID, slot N and the given name; optionally overrides the storage tag.
    /// </summary>
    public static string CopyTo(string sourcePath, string targetDir, string newName, LabCopyRegistry registry, byte[]? storageTag = null)
    {
        var file = KsdFile.Parse(File.ReadAllBytes(sourcePath));

        var guid = RandomNumberGenerator.GetBytes(16);
        var max = -1;
        foreach (var f in Directory.EnumerateFiles(targetDir, "*.ksd"))
        {
            var m = CharacterFile().Match(Path.GetFileName(f));
            if (m.Success && int.TryParse(m.Groups[1].ValueSpan, out var n) && n > max) max = n;
        }

        for (var n = max + 1; n <= 255; n++)
        {
            var cd = CharacterData.Parse(file["character_data"].GetData()).With(name: newName, slotIndex: (byte)n, guid: guid, storageTag: storageTag);
            file["character_data"].SetData(cd.ToBytes());
            file.Guid = guid;
            var bytes = file.ToBytes();

            // A failed write leaves no partial file and throws, rather than passing for a taken number (NewFile.TryWrite).
            var target = Path.Combine(targetDir, $"character{n}.ksd");
            if (!NewFile.TryWrite(target, fs => fs.Write(bytes))) continue;

            registry.Register(target, guid);
            return target;
        }
        throw new InvalidOperationException("No free character slot number below 256.");
    }
}
