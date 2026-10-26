namespace EnshroudedSaveEditor.Core.Files;

/// <summary>
/// Writes a file that must not exist yet: backups, Lab copies and the Lab's watch snapshots, which never overwrite
/// anything.
/// </summary>
public static class NewFile
{
    /// <summary>
    /// Creates <paramref name="path"/> and writes it with <paramref name="write"/>. Returns false, writing nothing, when
    /// a file of that name already exists. Only that counts as a name collision: when the write fails after the file
    /// was created (a full disk), the partial file is deleted and the error rethrown.
    /// </summary>
    public static bool TryWrite(string path, Action<Stream> write)
    {
        FileStream fs;
        try
        {
            fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }

        try
        {
            using (fs) write(fs);
        }
        catch
        {
            // The file is ours (CreateNew), so a partial one can go. A failed delete mustn't hide the write's error.
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return true;
    }
}
