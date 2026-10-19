namespace Vacuo.Core.Actions;

/// <summary>An action or undo was refused before anything on disk changed.</summary>
public class ActionRefusedException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>Another program has a file open, so nothing was moved.</summary>
public sealed class FileInUseException(string fileInUse)
    : ActionRefusedException($"'{fileInUse}' is open in another program, so nothing was moved.")
{
    public string FileInUse { get; } = fileInUse;
}
