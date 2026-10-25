namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>An edit that was refused; the message is suitable to show to the user.</summary>
public sealed class EditRejectedException(string message) : Exception(message);
