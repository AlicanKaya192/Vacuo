using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Organize;

public sealed record OrganizeOutcome(Guid BatchId, IReadOnlyList<JournalEntry> Moved, IReadOnlyList<MoveFailure> Failed);

/// <summary>
/// Carries out a Düzen plan as one batch in the journal, so <see cref="JournalScreen.UndoBatch"/> puts
/// everything back at once. Each file moves on its own: one that is open or gone stays, the rest still
/// move. Folders it made and left empty are removed straight away; the others are logged for undo.
/// </summary>
public sealed class FolderOrganizer(ActionRunner runner)
{
    // Same code as UnauthorizedAccessException.
    private const int AccessDenied = unchecked((int)0x8007_0005);

    public OrganizeOutcome Run(OrganizePlan plan)
    {
        EnsureStaysInside(plan);
        var batchId = Guid.NewGuid();
        foreach (string folder in plan.FoldersToCreate)
        {
            Directory.CreateDirectory(folder);
        }

        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();
        foreach (PlannedMove move in plan.Moves)
        {
            if (!File.Exists(move.Source))
            {
                failed.Add(new MoveFailure(move.Source, MoveFailureReason.Missing));
                continue;
            }

            try
            {
                moved.Add(runner.Run(new MoveFileAction(move.Source, move.Destination), batchId, JournalBatchKind.Organize));
            }
            catch (Exception exception) when (exception is ActionRefusedException or IOException or UnauthorizedAccessException)
            {
                failed.Add(new MoveFailure(move.Source, ReasonFor(exception)));
            }
        }

        foreach (string folder in plan.FoldersToCreate)
        {
            KeepOrRemove(folder, batchId);
        }

        return new OrganizeOutcome(batchId, moved, failed);
    }

    private static MoveFailureReason ReasonFor(Exception failure) => failure switch
    {
        FileInUseException => MoveFailureReason.InUse,
        UnauthorizedAccessException or { InnerException.HResult: AccessDenied } => MoveFailureReason.NoAccess,
        _ => MoveFailureReason.Refused,
    };

    private void KeepOrRemove(string folder, Guid batchId)
    {
        if (Directory.EnumerateFileSystemEntries(folder).Any())
        {
            runner.RecordCreatedFolder(folder, batchId);
        }
        else
        {
            Directory.Delete(folder, recursive: false);
        }
    }

    // A plan only ever moves top-level files of an allowed folder into its own subfolders (B-161).
    private static void EnsureStaysInside(OrganizePlan plan)
    {
        ScanRootGuard.ForThisMachine().EnsureAllowed(plan.Folder);
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Folder));
        bool inside = plan.Moves.All(move => IsChildOf(move.Source, folder) && IsChildOf(ParentOf(move.Destination), folder))
            && plan.FoldersToCreate.All(created => IsChildOf(created, folder));
        if (!inside)
        {
            throw new ArgumentException($"The plan for '{plan.Folder}' moves something outside that folder.", nameof(plan));
        }
    }

    private static string ParentOf(string path) => Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;

    private static bool IsChildOf(string path, string folder) =>
        string.Equals(ParentOf(path), folder, StringComparison.OrdinalIgnoreCase);
}
