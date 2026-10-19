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

/// <summary>
/// The Recycle Bin cannot take the item (the drive has no bin, or the item does not fit), so it
/// would be deleted for good; nothing was deleted.
/// </summary>
public sealed class NotRecyclableException(string message) : ActionRefusedException(message);

/// <summary>The item is no longer in the Recycle Bin (restored or emptied outside Vacuo), so it cannot be put back.</summary>
public sealed class ItemNotInBinException(string message) : ActionRefusedException(message);
