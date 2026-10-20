namespace Vacuo.Core.Ram;

/// <summary>
/// What a close request achieved, measured some time after it (B-153): which asked apps are gone,
/// which still run (most likely waiting on a "save changes?" question), and the change in available
/// RAM and in committed memory. The numbers are reported as measured, even when they went the other way.
/// </summary>
public sealed record CloseOutcome(IReadOnlyList<string> Closed, IReadOnlyList<string> StillRunning, long AvailableGainedBytes, long CommitDroppedBytes)
{
    public static CloseOutcome From(IReadOnlyList<string> requested, IEnumerable<RunningApp> after, MemorySnapshot before, MemorySnapshot afterSnapshot)
    {
        if (requested.Count == 0)
        {
            throw new ArgumentException("At least one app has to be asked to close.", nameof(requested));
        }

        var running = new HashSet<string>(after.Select(app => app.Name), StringComparer.OrdinalIgnoreCase);
        return new CloseOutcome(
            requested.Where(name => !running.Contains(name)).ToList(),
            requested.Where(running.Contains).ToList(),
            afterSnapshot.AvailablePhysicalBytes - before.AvailablePhysicalBytes,
            before.CommittedBytes - afterSnapshot.CommittedBytes);
    }
}
