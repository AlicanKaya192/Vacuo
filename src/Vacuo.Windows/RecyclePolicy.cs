using Vacuo.Core.Actions;

namespace Vacuo.Windows;

/// <summary>
/// When the Shell may be asked to recycle. Anything that could end in a permanent delete is
/// refused (project rule 3): drives without a Recycle Bin up front, and any item the Shell
/// says it cannot recycle at the moment of deletion.
/// </summary>
public static class RecyclePolicy
{
    // TRANSFER_SOURCE_FLAGS.TSF_DELETE_RECYCLE_IF_POSSIBLE, ShObjIdl_core.h.
    private const uint DeleteRecycleIfPossible = 0x80;

    /// <summary>Only local fixed drives have a Recycle Bin; removable and network drives delete for good.</summary>
    public static bool IsRecyclableDrive(DriveType driveType) => driveType == DriveType.Fixed;

    public static bool AllowsDelete(uint transferFlags) => (transferFlags & DeleteRecycleIfPossible) != 0;

    internal static void EnsureRecyclableDrive(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path))!;
        if (!IsRecyclableDrive(new DriveInfo(root).DriveType))
        {
            throw new NotRecyclableException($"'{root}' has no Recycle Bin, so '{path}' would be deleted for good; nothing was moved.");
        }
    }
}
