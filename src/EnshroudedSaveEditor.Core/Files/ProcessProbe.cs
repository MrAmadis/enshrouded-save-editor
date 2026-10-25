using System.Diagnostics;

namespace EnshroudedSaveEditor.Core.Files;

public interface IProcessProbe
{
    bool IsGameRunning();
    bool IsSteamRunning();
}

public sealed class ProcessProbe : IProcessProbe
{
    public bool IsGameRunning() => IsRunning("enshrouded");
    public bool IsSteamRunning() => IsRunning("steam");

    static bool IsRunning(string name)
    {
        var found = Process.GetProcessesByName(name);
        try { return found.Length > 0; }
        finally { foreach (var p in found) p.Dispose(); }
    }
}
