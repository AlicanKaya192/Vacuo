using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Leftovers;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Leftovers;

public class AdminCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeLauncher(params ElevatedResult[] results) : IElevatedLauncher
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public bool Decline { get; init; }

        public IReadOnlyList<ElevatedResult> Run(IReadOnlyList<string> operations)
        {
            Calls.Add(operations);
            return Decline ? throw new ElevationDeclinedException() : results;
        }
    }

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            var time = new FixedTimeProvider(Now);
            Journal = new ActionJournal(Tree.PathOf(@"Vacuo\journal.jsonl"));
            Runner = new ActionRunner(Journal, new FakeRecycleBin(Tree.PathOf("bin"), time), time);
            Tree.AddDirectory(@"drive\Windows\Temp");
            Tree.AddDirectory(@"drive\Windows\SoftwareDistribution\Download");
        }

        public TempTree Tree { get; } = new();
        public ActionJournal Journal { get; }
        public ActionRunner Runner { get; }

        public WindowsLocations Machine => new(Tree.PathOf("drive"), Tree.PathOf(@"drive\Windows"), Tree.PathOf("user-temp"));

        public void Dispose() => Tree.Dispose();
    }

    private static ElevatedResult Emptied(string id, long bytes, int files) => new(id, true, bytes, files, 0, ExitCode: null, Error: null);

    [Fact]
    public void Offered_AdminFoldersThatExistAndComponentCleanup()
    {
        using var setup = new Setup();
        setup.Tree.AddDirectory(@"drive\$Windows.~BT");

        var cleanup = new AdminCleanup(setup.Machine);

        Assert.Equal(
            ["windows-temp", "update-downloads", "upgrade-files-bt", "component-cleanup"],
            cleanup.Offered.Select(item => item.Id));
    }

    [Fact]
    public void Offered_NeverTheUserTempOrWindowsOld()
    {
        using var setup = new Setup();
        setup.Tree.AddDirectory(@"drive\Windows.old");
        setup.Tree.AddDirectory("user-temp");

        var cleanup = new AdminCleanup(setup.Machine);

        Assert.DoesNotContain(cleanup.Offered, item => item.Id is "user-temp" or "windows-old");
    }

    [Fact]
    public void WindowsOld_IsShownOnlyWhenItExists()
    {
        using var setup = new Setup();
        Assert.Null(new AdminCleanup(setup.Machine).WindowsOld);

        setup.Tree.AddDirectory(@"drive\Windows.old");

        Assert.Equal("windows-old", new AdminCleanup(setup.Machine).WindowsOld?.Id);
    }

    [Fact]
    public void NothingStartsSelected()
    {
        using var setup = new Setup();

        var cleanup = new AdminCleanup(setup.Machine);

        Assert.Equal(0, cleanup.SelectedCount);
        Assert.DoesNotContain(cleanup.Offered, item => cleanup.IsSelected(item.Id));
    }

    [Fact]
    public void Select_AnItemNotOffered_Throws()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);

        Assert.Throws<ArgumentException>(() => cleanup.Select("upgrade-files-ws"));
        Assert.Throws<ArgumentException>(() => cleanup.Select("user-temp"));
    }

    [Fact]
    public void Run_AsksForTheSelectedOperationsInTableOrder()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        cleanup.Select("component-cleanup");
        cleanup.Select("windows-temp");
        var launcher = new FakeLauncher();

        cleanup.Run(launcher, setup.Runner);

        Assert.Equal([["windows-temp", "component-cleanup"]], launcher.Calls);
    }

    [Fact]
    public void Run_RecordsEachCleanupThatRemovedSomethingAsDeletedForGood()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        cleanup.Select("windows-temp");
        cleanup.Select("component-cleanup");
        var launcher = new FakeLauncher(
            Emptied("windows-temp", 100, 2),
            new ElevatedResult("component-cleanup", true, 0, 0, 0, ExitCode: 0, Error: null));

        IReadOnlyList<ElevatedResult> results = cleanup.Run(launcher, setup.Runner);

        Assert.Equal(2, results.Count);
        Assert.Equal(
            [(setup.Tree.PathOf(@"drive\Windows\Temp"), 100L), (setup.Tree.PathOf(@"drive\Windows\WinSxS"), 0L)],
            setup.Journal.ReadAll().Select(entry => (Assert.IsType<DeletedForGood>(entry.Undo).OriginalPath, entry.SizeBytes)));
    }

    [Fact]
    public void Run_FailedOrEmptyCleanup_IsNotRecorded()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        cleanup.Select("windows-temp");
        cleanup.Select("component-cleanup");
        var launcher = new FakeLauncher(
            Emptied("windows-temp", 0, 0),
            new ElevatedResult("component-cleanup", false, 0, 0, 0, ExitCode: 87, Error: null));

        cleanup.Run(launcher, setup.Runner);

        Assert.Empty(setup.Journal.ReadAll());
    }

    [Fact]
    public void Run_ResultForAnOperationNotAsked_IsNotRecorded()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        cleanup.Select("windows-temp");
        var launcher = new FakeLauncher(Emptied("windows-temp", 10, 1), Emptied("update-downloads", 50, 1));

        IReadOnlyList<ElevatedResult> results = cleanup.Run(launcher, setup.Runner);

        Assert.Equal([Emptied("windows-temp", 10, 1)], results);
        Assert.Single(setup.Journal.ReadAll());
    }

    [Fact]
    public void Run_ClearsTheSelection()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        cleanup.Select("windows-temp");

        cleanup.Run(new FakeLauncher(Emptied("windows-temp", 10, 1)), setup.Runner);

        Assert.Equal(0, cleanup.SelectedCount);
    }

    [Fact]
    public void Run_Declined_RecordsNothingAndKeepsTheSelection()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        cleanup.Select("windows-temp");

        Assert.Throws<ElevationDeclinedException>(() => cleanup.Run(new FakeLauncher { Decline = true }, setup.Runner));

        Assert.Empty(setup.Journal.ReadAll());
        Assert.True(cleanup.IsSelected("windows-temp"));
    }

    [Fact]
    public void Run_NothingSelected_DoesNotStartTheHelper()
    {
        using var setup = new Setup();
        var cleanup = new AdminCleanup(setup.Machine);
        var launcher = new FakeLauncher();

        Assert.Throws<InvalidOperationException>(() => cleanup.Run(launcher, setup.Runner));

        Assert.Empty(launcher.Calls);
    }
}
