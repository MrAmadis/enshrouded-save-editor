using EnshroudedSaveEditor.Core.Files;

namespace EnshroudedSaveEditor.Core.Editing;

public sealed class CharacterEditor(SaveSession s)
{
    public const int MaxNameLength = 32;

    void EnsureWritable()
    {
        if (s.ReadOnly) throw new EditRejectedException($"This save is read-only: {s.ReadOnlyReason}");
    }

    /// <summary>Trimmed; 1 to 32 UTF-16 characters; no control characters.</summary>
    public void SetName(string name)
    {
        EnsureWritable();
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new EditRejectedException("The name cannot be empty.");
        if (n.Length > MaxNameLength) throw new EditRejectedException($"The name can be at most {MaxNameLength} characters.");
        if (n.Any(char.IsControl)) throw new EditRejectedException("The name cannot contain control characters.");
        if (n == s.Character.Name) throw new EditRejectedException($"Already {n}");
        s.Character = s.Character.With(name: n);
        s.MarkChanged();
    }

    public void SetLevel(int level, int cap)
    {
        EnsureWritable();
        if (level < 1 || level > cap) throw new EditRejectedException($"The level must be between 1 and {cap}.");
        if ((uint)level == s.Player.Level) throw new EditRejectedException($"Already {level}");
        s.Player.Level = (uint)level;
        s.MarkChanged();
    }

    public void SetXp(long xp)
    {
        EnsureWritable();
        if (xp < 0 || xp > uint.MaxValue) throw new EditRejectedException($"XP must be between 0 and {uint.MaxValue}.");
        if ((uint)xp == s.Player.Xp) throw new EditRejectedException($"Already {xp}");
        s.Player.Xp = (uint)xp;
        s.MarkChanged();
    }
}
