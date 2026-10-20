using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Maintenance;
using Vacuo.Core.Scanning;
using Vacuo.Core.Settings;
using Vacuo.Windows;

namespace Vacuo.Maintenance;

/// <summary>
/// Otomatik Bakım's helper (ADR-004): the tray starts it, without a window, when maintenance is due. It
/// checks again (the tray's view may be minutes old), scans the chosen categories, moves what qualifies
/// to the Recycle Bin as one Journal batch, writes maintenance.json and ends. It stops as soon as the
/// user is back; what was moved stays in the Journal. One at a time. Switches, for trying it out:
/// <c>--now</c> runs whatever the schedule and the on/off setting say; <c>--root &lt;folder&gt;</c>
/// scans only that folder (its <c>Temp</c> subfolder as the temp folder) instead of the user's own.
/// </summary>
internal static class Program
{
    private const string SingleHelperName = @"Local\Vacuo.Maintenance";
    private const int Done = 0;
    private const int NotDue = 1;
    private const int AlreadyRunning = 2;

    private static int Main(string[] args)
    {
        using var single = new Mutex(initiallyOwned: true, SingleHelperName, out bool first);
        if (!first)
        {
            return AlreadyRunning;
        }

        bool now = args.Contains("--now");
        int rootAt = Array.IndexOf(args, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < args.Length ? Path.GetFullPath(args[rootAt + 1]) : null;

        MaintenanceSettings settings = new SettingsStore(SettingsStore.DefaultPath).Load().Maintenance;
        var state = new MaintenanceStateStore(MaintenanceStateStore.DefaultPath);
        DateTimeOffset started = DateTimeOffset.Now;
        if (!now && !MaintenancePolicy.IsDue(settings, state.LoadLastRun(), UserPresence.IdleFor(), UserPresence.OnMains(), started))
        {
            return NotDue;
        }

        KnownRoots roots = root is null ? KnownRoots.ForCurrentUser() : new KnownRoots(root, root);
        string temp = root is null ? Path.GetTempPath() : Path.Combine(root, "Temp");
        var scan = new MaintenanceScan(
            settings.TempFiles ? new UserTempScanner(temp).Scan().Findings : [],
            settings.DownloadCaches ? new KnownLocationScanner(roots).Scan().Findings : [],
            settings.ProjectLeftovers ? new ProjectArtifactScanner([roots.UserProfile]).Scan().Findings : []);
        IReadOnlyList<ScanFinding> items = MaintenanceSelection.Select(settings, scan, started);

        var recycleBin = new ShellRecycleBin(TimeProvider.System);
        var runner = new ActionRunner(new ActionJournal(ActionJournal.DefaultPath), recycleBin, TimeProvider.System);
        MaintenanceOutcome outcome = new MaintenanceRun(runner, recycleBin).Run(
            items, shouldStop: () => !now && MaintenancePolicy.UserReturned(UserPresence.LastInputAt(), started));
        state.SaveRun(started, outcome.Summary);
        return Done;
    }
}
