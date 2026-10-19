using System.Runtime.InteropServices;
using Vacuo.Core.Actions;

namespace Vacuo.Windows;

/// <summary>
/// The Windows Recycle Bin through the Shell, the same way Explorer recycles and restores.
/// It never empties the bin and never deletes for good (project rule 3).
/// </summary>
public sealed class ShellRecycleBin(TimeProvider time) : IRecycleBin
{
    private const uint RecycleFlags = ShellInterop.AllowUndo | ShellInterop.RecycleOnDelete | ShellInterop.WantNukeWarning
        | ShellInterop.NoConfirmation | ShellInterop.Silent | ShellInterop.NoErrorUi | ShellInterop.EarlyFailure;

    private const uint RestoreFlags = ShellInterop.NoConfirmation | ShellInterop.Silent | ShellInterop.NoErrorUi | ShellInterop.EarlyFailure;

    public RecycledItem MoveToBin(string path)
    {
        RecyclePolicy.EnsureRecyclableDrive(path);
        string binPath = StaThread.Run(() => Recycle(path));
        return new RecycledItem(path, time.GetUtcNow(), binPath);
    }

    public void Restore(RecycledItem item) => StaThread.Run(() => PutBack(item));

    private static string Recycle(string path)
    {
        var sink = new RecycleOnlySink();
        IFileOperation operation = ShellInterop.CreateFileOperation();
        operation.SetOperationFlags(RecycleFlags);
        uint cookie = operation.Advise(sink);
        try
        {
            operation.DeleteItem(ShellInterop.ItemFromPath(path), itemSink: null);
            Perform(operation, $"Windows could not move '{path}' to the Recycle Bin.");
        }
        catch (ActionRefusedException) when (sink.RefusedPermanentDelete)
        {
            throw new NotRecyclableException($"'{path}' does not fit in the Recycle Bin, so it would be deleted for good; nothing was deleted.");
        }
        finally
        {
            operation.Unadvise(cookie);
        }

        // Should the guards above ever miss a permanent delete, say so loudly: the item is gone.
        return sink.BinPath ?? throw new InvalidOperationException($"'{path}' was deleted without reaching the Recycle Bin.");
    }

    private static bool PutBack(RecycledItem item)
    {
        string folder = Path.GetDirectoryName(item.OriginalPath)!;
        if (!Directory.Exists(folder))
        {
            throw new ActionRefusedException($"The folder '{folder}' no longer exists, so '{item.OriginalPath}' cannot be put back.");
        }

        IShellItem binItem = FindInBin(item.BinPath)
            ?? throw new ActionRefusedException($"'{item.OriginalPath}' is no longer in the Recycle Bin.");

        IFileOperation operation = ShellInterop.CreateFileOperation();
        operation.SetOperationFlags(RestoreFlags);
        operation.MoveItem(binItem, ShellInterop.ItemFromPath(folder), Path.GetFileName(item.OriginalPath), itemSink: null);
        Perform(operation, $"Windows could not put '{item.OriginalPath}' back from the Recycle Bin.");
        return true;
    }

    private static IShellItem? FindInBin(string binPath) =>
        ShellInterop.RecycleBinItems().FirstOrDefault(
            item => string.Equals(FileSystemPathOf(item), binPath, StringComparison.OrdinalIgnoreCase));

    private static string? FileSystemPathOf(IShellItem item)
    {
        try
        {
            return item.GetDisplayName(ShellInterop.FileSystemPath);
        }
        catch (COMException)
        {
            // An item without a file-system path cannot be the one Vacuo stored.
            return null;
        }
    }

    private static void Perform(IFileOperation operation, string failureMessage)
    {
        try
        {
            operation.PerformOperations();
        }
        catch (COMException exception)
        {
            throw new ActionRefusedException(failureMessage, exception);
        }

        if (operation.GetAnyOperationsAborted())
        {
            throw new ActionRefusedException(failureMessage);
        }
    }
}
