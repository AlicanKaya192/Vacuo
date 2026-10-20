using System.Security.Cryptography;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Organize;

public class FolderOrganizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            var time = new FixedTimeProvider(Now);
            Journal = new ActionJournal(Tree.PathOf(@"Vacuo\journal.jsonl"));
            var runner = new ActionRunner(Journal, new FakeRecycleBin(Tree.PathOf("bin"), time), time);
            Organizer = new FolderOrganizer(runner);
            Screen = new JournalScreen(Journal, runner);
        }

        public TempTree Tree { get; } = new();
        public ActionJournal Journal { get; }
        public FolderOrganizer Organizer { get; }
        public JournalScreen Screen { get; }
        public string Desktop => Tree.PathOf("Desktop");

        public OrganizePlan PlanDesktop() => OrganizePlanner.Plan(
            Desktop, FolderItems.Read(Desktop), OrganizeRule.ByType, target => target.Key + " folder", PathProbe.Disk);

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void OrganizeThenUndo_PutsEveryFileBackUnchanged()
    {
        using var setup = new Setup();
        AddText(setup, @"Desktop\a.pdf", "first");
        AddText(setup, @"Desktop\b.pdf", "other"); // same size as a.pdf, different contents
        AddText(setup, @"Desktop\c.png", "picture");
        AddText(setup, @"Desktop\Images folder\old.png", "already sorted");
        Dictionary<string, string> before = Snapshot(setup.Desktop);

        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());

        Assert.Empty(outcome.Failed);
        Assert.Equal(3, outcome.Moved.Count);
        Assert.Equal(before.Values.Order(), Snapshot(setup.Desktop).Values.Order());
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Documents folder\b.pdf")));

        Assert.Empty(setup.Screen.UndoBatch(outcome.BatchId));

        Assert.Equal(before, Snapshot(setup.Desktop));
        Assert.False(Directory.Exists(setup.Tree.PathOf(@"Desktop\Documents folder")));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Images folder\old.png")));
    }

    [Fact]
    public void Organize_WritesOneBatchToTheJournal()
    {
        using var setup = new Setup();
        AddText(setup, @"Desktop\a.pdf", "a");
        AddText(setup, @"Desktop\b.png", "b");

        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());

        IReadOnlyList<JournalEntry> entries = setup.Journal.ReadAll();
        Assert.All(entries, entry => Assert.Equal(outcome.BatchId, entry.BatchId));
        Assert.Equal(2, entries.Count(entry => entry.Undo is CreatedFolder));
        Assert.Equal(2, entries.Count(entry => entry.Undo is MovedItem));
    }

    [Fact]
    public void FileOpenInAnotherProgram_StaysAndTheOthersStillMove()
    {
        using var setup = new Setup();
        string busy = AddText(setup, @"Desktop\a.pdf", "a");
        AddText(setup, @"Desktop\b.png", "b");
        OrganizePlan plan = setup.PlanDesktop();
        using FileStream otherProgram = new(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        OrganizeOutcome outcome = setup.Organizer.Run(plan);

        Assert.Equal(new MoveFailure(busy, MoveFailureReason.InUse), Assert.Single(outcome.Failed));
        Assert.True(File.Exists(busy));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Images folder\b.png")));
    }

    [Fact]
    public void Undo_CreatedFolderWithSomethingNewInside_IsLeftAlone()
    {
        using var setup = new Setup();
        AddText(setup, @"Desktop\a.pdf", "a");
        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());
        string newFile = AddText(setup, @"Desktop\Documents folder\new.txt", "made later");

        BatchUndoFailure failure = Assert.Single(setup.Screen.UndoBatch(outcome.BatchId));

        Assert.Equal(UndoFailureReason.FolderNotEmpty, failure.Reason);
        Assert.True(File.Exists(newFile));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\a.pdf")));
    }

    [Fact]
    public void PlanForASystemFolder_IsRefusedBeforeAnythingMoves()
    {
        using var setup = new Setup();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var plan = new OrganizePlan(windows, [new PlannedMove(Path.Combine(windows, "a.txt"), Path.Combine(windows, "Docs", "a.txt"))], []);

        Assert.Throws<ArgumentException>(() => setup.Organizer.Run(plan));
        Assert.Empty(setup.Journal.ReadAll());
    }

    [Fact]
    public void MoveLeavingTheFolder_IsRefusedBeforeAnythingMoves()
    {
        using var setup = new Setup();
        string a = AddText(setup, @"Desktop\a.pdf", "a");
        var plan = new OrganizePlan(setup.Desktop, [new PlannedMove(a, setup.Tree.PathOf(@"Elsewhere\a.pdf"))], []);

        Assert.Throws<ArgumentException>(() => setup.Organizer.Run(plan));
        Assert.True(File.Exists(a));
    }

    private static string AddText(Setup setup, string relativePath, string text)
    {
        string path = setup.Tree.PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    // Relative path → SHA-256 of the contents, for every file under the folder.
    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(folder, path),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.OrdinalIgnoreCase);
}
