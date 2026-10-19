using System.Runtime.InteropServices;

namespace Vacuo.Windows;

/// <summary>
/// Watches a delete operation. It cancels the operation when the Shell is about to delete
/// for good instead of recycling, and it remembers where the bin stored the item.
/// </summary>
[ComVisible(true)]
internal sealed class RecycleOnlySink : IFileOperationProgressSink
{
    public bool RefusedPermanentDelete { get; private set; }

    /// <summary>The item's path inside the Recycle Bin; null while nothing was recycled.</summary>
    public string? BinPath { get; private set; }

    public int PreDeleteItem(uint flags, IShellItem item)
    {
        if (RecyclePolicy.AllowsDelete(flags))
        {
            return ShellInterop.Ok;
        }

        RefusedPermanentDelete = true;
        return ShellInterop.Abort;
    }

    public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newlyCreated)
    {
        if (result >= 0 && newlyCreated is not null)
        {
            BinPath = newlyCreated.GetDisplayName(ShellInterop.FileSystemPath);
        }

        return ShellInterop.Ok;
    }

    public int StartOperations() => ShellInterop.Ok;
    public int FinishOperations(int result) => ShellInterop.Ok;
    public int PreRenameItem(uint flags, IShellItem item, string? newName) => ShellInterop.Ok;
    public int PostRenameItem(uint flags, IShellItem item, string? newName, int result, IShellItem? newlyCreated) => ShellInterop.Ok;
    public int PreMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName) => ShellInterop.Ok;
    public int PostMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName, int result, IShellItem? newlyCreated) => ShellInterop.Ok;
    public int PreCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName) => ShellInterop.Ok;
    public int PostCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName, int result, IShellItem? newlyCreated) => ShellInterop.Ok;
    public int PreNewItem(uint flags, IShellItem destinationFolder, string? newName) => ShellInterop.Ok;
    public int PostNewItem(uint flags, IShellItem destinationFolder, string? newName, string? templateName, uint fileAttributes, int result, IShellItem? newItem) => ShellInterop.Ok;
    public int UpdateProgress(uint workTotal, uint workSoFar) => ShellInterop.Ok;
    public int ResetTimer() => ShellInterop.Ok;
    public int PauseTimer() => ShellInterop.Ok;
    public int ResumeTimer() => ShellInterop.Ok;
}
