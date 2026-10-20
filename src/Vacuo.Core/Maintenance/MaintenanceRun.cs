using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Maintenance;

/// <summary>What a run did, kept for Settings and the notification (dilim 2).</summary>
public sealed record MaintenanceSummary(int Moved, long Bytes, int Failed, bool Stopped)
{
    /// <summary>Desktop files Düzen sorted into folders in the same run (its own Journal batch).</summary>
    public int Organized { get; init; }
}

public sealed record MaintenanceOutcome(Guid BatchId, IReadOnlyList<JournalEntry> Moved, IReadOnlyList<MoveFailure> Failed, bool Stopped)
{
    public long MovedBytes => Moved.Sum(entry => entry.SizeBytes);

    public MaintenanceSummary Summary => new(Moved.Count, MovedBytes, Failed.Count, Stopped);
}

/// <summary>
/// Moves the chosen items to the Recycle Bin as one Journal batch ("Otomatik Bakım"), so all of it can be
/// put back from the Journal. Only the Recycle Bin, never a permanent delete (vacuo-kurallar 3). Before each
/// item it asks <c>shouldStop</c>: when the user is back, it stops and keeps what was moved.
/// </summary>
public sealed class MaintenanceRun(ActionRunner runner, IRecycleBin recycleBin)
{
    public MaintenanceOutcome Run(IReadOnlyList<ScanFinding> items, Func<bool> shouldStop)
    {
        var batchId = Guid.NewGuid();
        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();
        foreach (ScanFinding item in items)
        {
            if (shouldStop())
            {
                return new MaintenanceOutcome(batchId, moved, failed, Stopped: true);
            }

            if (!Path.Exists(item.Path))
            {
                failed.Add(new MoveFailure(item.Path, MoveFailureReason.Missing));
                continue;
            }

            try
            {
                moved.Add(runner.Run(new MoveToRecycleBinAction(item.Path, recycleBin), batchId, JournalBatchKind.Maintenance));
            }
            catch (ActionRefusedException refusal)
            {
                failed.Add(new MoveFailure(item.Path, MoveFailures.ReasonFor(refusal)));
            }
        }

        return new MaintenanceOutcome(batchId, moved, failed, Stopped: false);
    }
}
