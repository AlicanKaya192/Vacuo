using Vacuo.Core.Actions;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Leftovers;

/// <summary>
/// The administrator part of the Windows screen without its looks (ADR-003): which cleanups are
/// offered, the user's choice and the run. Nothing starts selected; every chosen cleanup runs
/// under one administrator prompt, and each one that removed something goes to the Journal as
/// deleted for good.
/// </summary>
public sealed class AdminCleanup
{
    private readonly WindowsLocations _locations;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    public AdminCleanup(WindowsLocations locations)
    {
        _locations = locations;
        IReadOnlyList<WindowsLeftover> table = WindowsLeftovers.For(locations);
        Offered = table
            .Where(item => item.Kind == LeftoverKind.ComponentCleanup
                || (item.Kind == LeftoverKind.AdminFolder && Directory.Exists(item.Path)))
            .ToList();
        WindowsOld = table.SingleOrDefault(item => item.Kind == LeftoverKind.WindowsCleanupTool && Directory.Exists(item.Path));
    }

    /// <summary>Cleanups the helper can do here, in table order: folders that exist, and DISM.</summary>
    public IReadOnlyList<WindowsLeftover> Offered { get; }

    /// <summary>The previous Windows installation, left to Windows' own Disk Cleanup; null when there is none.</summary>
    public WindowsLeftover? WindowsOld { get; }

    public int SelectedCount => _selected.Count;

    public bool IsSelected(string id) => _selected.Contains(id);

    public void Select(string id)
    {
        if (!Offered.Any(item => item.Id == id))
        {
            throw new ArgumentException($"'{id}' is not offered on this machine.", nameof(id));
        }

        _selected.Add(id);
    }

    public void Deselect(string id) => _selected.Remove(id);

    /// <summary>Runs the chosen cleanups and returns what the helper reported for each of them.</summary>
    /// <exception cref="ElevationDeclinedException">The user said no at the prompt; the choice is kept.</exception>
    public IReadOnlyList<ElevatedResult> Run(IElevatedLauncher launcher, ActionRunner runner)
    {
        List<WindowsLeftover> chosen = Offered.Where(item => _selected.Contains(item.Id)).ToList();
        if (chosen.Count == 0)
        {
            throw new InvalidOperationException("No cleanup is selected.");
        }

        // The result file lives in a folder the user can write to, so only answers to what was asked count.
        List<ElevatedResult> results = launcher.Run(chosen.Select(item => item.Id).ToList())
            .Where(result => chosen.Any(item => item.Id == result.Operation))
            .ToList();
        _selected.Clear();

        foreach (ElevatedResult result in results)
        {
            WindowsLeftover leftover = chosen.Single(item => item.Id == result.Operation);
            if (RemovedSomething(leftover, result))
            {
                runner.RecordDeletedForGood(JournalPathOf(leftover), result.DeletedBytes);
            }
        }

        return results;
    }

    // DISM reports no size; it counts once it finished without error.
    private static bool RemovedSomething(WindowsLeftover leftover, ElevatedResult result) =>
        result.Succeeded && (leftover.Kind == LeftoverKind.ComponentCleanup || result.DeletedFiles > 0);

    // DISM has no folder of its own in the table; what it cleans is the component store.
    private string JournalPathOf(WindowsLeftover leftover) =>
        leftover.Path ?? Path.Combine(_locations.Windows, "WinSxS");
}
