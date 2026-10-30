using System.Globalization;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Hosting;
using Vacuo.Core.Journal;
using Vacuo.Core.Maintenance;
using Vacuo.Core.Notices;
using Vacuo.Core.Organize;
using Vacuo.Core.Scanning;
using Vacuo.Core.Settings;
using Vacuo.Windows;

namespace Vacuo.Maintenance;

/// <summary>
/// Otomatik Bakım's helper (ADR-004): the tray starts it, without a window, when maintenance is due. It
/// checks again (the tray's view may be minutes old), scans the chosen categories, moves what qualifies
/// to the Recycle Bin as one Journal batch, sorts the Desktop by type if chosen, writes maintenance.json,
/// and when the user is back tells them in Vacuo's own notice (vacuo-kurallar 16). It stops as soon as the
/// user is back; what was moved stays in the Journal. One at a time. With <c>--memory</c> it only shows
/// the "memory is tight" notice the tray asked for. Switches, for trying it out: <c>--now</c> runs whatever
/// the schedule and the on/off setting say and tells at once; <c>--root &lt;folder&gt;</c> scans only that
/// folder (its <c>Temp</c> and <c>Desktop</c> subfolders as the temp folder and the Desktop).
/// </summary>
internal static class Program
{
    private const string SingleHelperName = @"Local\Vacuo.Maintenance";
    private const string MemorySwitch = "--memory";
    private const string DiskSwitch = "--disk";
    private const int Done = 0;
    private const int NotDue = 1;
    private const int AlreadyRunning = 2;

    // The run happens while the user is away; the notice waits for them, but not forever.
    private static readonly TimeSpan UserCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LongestWaitForUser = TimeSpan.FromHours(12);

    private static int Main(string[] args)
    {
        VacuoSettings saved = new SettingsStore(SettingsStore.DefaultPath).Load();
        string language = LanguageRule.Resolve(saved.Language, CultureInfo.CurrentUICulture.Name);
        MaintenanceSettings settings = saved.Maintenance;
        if (args.Contains(MemorySwitch))
        {
            Tell(NoticeTexts.ForTightMemory(language), settings.Sound);
            return Done;
        }

        if (args.Contains(DiskSwitch))
        {
            // Read again here: the tray's reading may be a few seconds old.
            if (SystemDrive.Read() is { } drive)
            {
                Tell(NoticeTexts.ForLowDisk(drive.AvailableFreeSpace, SystemDrive.NameOf(drive), language, GrewMost()), settings.Sound, OpenDiskInPanel);
            }

            return Done;
        }

        using var single = new Mutex(initiallyOwned: true, SingleHelperName, out bool first);
        if (!first)
        {
            return AlreadyRunning;
        }

        bool now = args.Contains("--now");
        int rootAt = Array.IndexOf(args, "--root");
        string? root = rootAt >= 0 && rootAt + 1 < args.Length ? Path.GetFullPath(args[rootAt + 1]) : null;

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
        int organized = settings.OrganizeDesktop && !outcome.Stopped ? OrganizeDesktop(root, runner, language) : 0;
        MaintenanceSummary summary = outcome.Summary with { Organized = organized };
        state.SaveRun(started, summary);

        // A trial with --root says nothing about the real drive; a user who is back should not wait on the disk.
        if (root is null && !outcome.Stopped)
        {
            RecordDiskHistory(DateOnly.FromDateTime(started.LocalDateTime));
        }

        if (NoticeTexts.ForMaintenance(summary, language) is { } notice)
        {
            // Stopped means the user is already back.
            if (now || outcome.Stopped || WaitForUser(DateTimeOffset.Now))
            {
                Tell(notice, settings.Sound);
            }
        }

        return Done;
    }

    // Gives the disk history its days even when the Disk screen stays closed (Aşama 3g dilim 2).
    private static void RecordDiskHistory(DateOnly today)
    {
        if (SystemDrive.Read() is not { } drive)
        {
            return;
        }

        IReadOnlyList<CategorySize> sizes = CategorySizer.Measure(CategoryRoots.ForThisMachine());
        new DiskHistoryStore(DiskHistoryStore.DefaultPath).Record(DiskSnapshot.From(today, drive.TotalSize - drive.AvailableFreeSpace, sizes));
    }

    private static FolderGrowth? GrewMost()
    {
        DiskHistory history = new DiskHistoryStore(DiskHistoryStore.DefaultPath).Load();
        return DiskGrowth.From(history, DateOnly.FromDateTime(DateTime.Now))?.Folders.FirstOrDefault();
    }

    /// <summary>Düzen's "by type" on the Desktop, as its own Journal batch; the folder names are the panel's.</summary>
    private static int OrganizeDesktop(string? root, ActionRunner runner, string language)
    {
        string desktop = root is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Path.Combine(root, "Desktop");
        if (!Directory.Exists(desktop))
        {
            return 0;
        }

        try
        {
            OrganizePlan plan = OrganizePlanner.Plan(
                desktop,
                FolderItems.Read(desktop),
                OrganizeRule.ByType,
                target => NoticeTexts.DesktopFolderName(Enum.Parse<FileKind>(target.Key), language),
                PathProbe.Disk);
            var icons = new FolderIcons(new FolderIconWriter(), FolderIcons.InstalledFolder);
            return new FolderOrganizer(runner, icons).Run(plan).Moved.Count;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The Desktop could not be read; the rest of the run stands.
            return 0;
        }
    }

    // True once there is keyboard or mouse input after `since`.
    private static bool WaitForUser(DateTimeOffset since)
    {
        // The scan's memory is not needed while waiting.
        GC.Collect();
        DateTimeOffset giveUpAt = since + LongestWaitForUser;
        while (DateTimeOffset.Now < giveUpAt)
        {
            if (UserPresence.LastInputAt() > since)
            {
                return true;
            }

            Thread.Sleep(UserCheckInterval);
        }

        return false;
    }

    // Silent during full screen, presentations and quiet time (vacuo-kurallar 16); the Journal still has it all.
    // A click closes the notice; <paramref name="clicked"/> also runs then.
    private static void Tell(NoticeText notice, bool sound, Action? clicked = null)
    {
        if (!NoticeGate.MayShow(UserPresence.NotificationState()))
        {
            return;
        }

        NoticeWindow.Show(notice, sound ? NoticeTone.Wave() : null, clicked);
    }

    // The user clicked, so this process may hand the foreground on; the tray opens the panel on Disk.
    private static void OpenDiskInPanel()
    {
        nint tray = NativeMethods.FindWindow(PanelProtocol.TrayWindowClass, null);
        if (tray == 0)
        {
            return;
        }

        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.AllowAnyProcess);
        _ = NativeMethods.PostMessage(tray, NativeMethods.RegisterWindowMessage(PanelProtocol.OpenDiskMessage), 0, 0);
    }
}
