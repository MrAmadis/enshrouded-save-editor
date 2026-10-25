namespace EnshroudedSaveEditor.Core.Container;

/// <summary>Thrown when a .ksd file is structurally invalid.</summary>
public sealed class KsdFormatException : Exception
{
    public KsdFormatException(string message) : base(message) { }
}
