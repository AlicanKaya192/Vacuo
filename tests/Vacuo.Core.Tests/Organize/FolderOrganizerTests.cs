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
        public Setup(IFolderIconWriter? iconWriter = null)
        {
            var time = new FixedTimeProvider(Now);
            Journal = new ActionJournal(Tree.PathOf(@"Vacuo\journal.jsonl"));
            var runner = new ActionRunner(Journal, new FakeRecycleBin(Tree.PathOf("bin"), time), time);
            FolderIcons? icons = null;
            if (iconWriter is not null)
            {
                foreach (string name in new[] { "documents", "images" })
                {
                    AddText(this, $@"Icons\{name}.ico", name + " picture");
                }

                icons = new FolderIcons(iconWriter, Tree.PathOf("Icons"));
            }

            Organizer = new FolderOrganizer(runner, icons);
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

    /// <summary>Leaves what <c>SHGetSetFolderCustomSettings</c> left in a real folder on 24 October 2026.</summary>
    private sealed class ShellLikeIconWriter(bool refuseAfterCopy = false) : IFolderIconWriter
    {
        public void Apply(string folder, string iconFile)
        {
            string icon = Path.Combine(folder, FolderIcon.IconFile);
            File.Copy(iconFile, icon);
            File.SetAttributes(icon, FileAttributes.Hidden | FileAttributes.System);
            if (refuseAfterCopy)
            {
                throw new IOException("The shell refused.");
            }

            string desktopIni = Path.Combine(folder, FolderIcon.DesktopIni);
            File.WriteAllText(desktopIni, "[.ShellClassInfo]\r\nIconResource=folder.ico,0\r\n");
            File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System);
            new DirectoryInfo(folder).Attributes |= FileAttributes.ReadOnly;
        }
    }

    private sealed class NoLinks : IShortcutReader
    {
        public string? TargetOf(string shortcut) => null;
    }

    // Only the game's shortcut goes: the other one is left where the user put it, and undo brings the game back.
    [Fact]
    public void AGameShortcut_MovesToGames_AndUndoBringsItBack()
    {
        using var setup = new Setup();
        AddText(setup, @"Desktop\Dota.url", "[InternetShortcut]\r\nURL=steam://rungameid/570\r\n");
        AddText(setup, @"Desktop\site.url", "[InternetShortcut]\r\nURL=https://example.com/\r\n");
        OrganizePlan plan = OrganizePlanner.Plan(
            setup.Desktop, FolderItems.Read(setup.Desktop, new NoLinks()), OrganizeRule.ByType, target => target.Key + " folder", PathProbe.Disk);

        OrganizeOutcome outcome = setup.Organizer.Run(plan);

        string moved = setup.Tree.PathOf(@"Desktop\Games folder\Dota.url");
        Assert.Single(plan.Moves);
        Assert.True(File.Exists(moved));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\site.url")));

        Assert.Empty(setup.Screen.UndoBatch(outcome.BatchId));

        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Dota.url")));
        Assert.False(File.Exists(moved));
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
    public void OrganizeWithIcons_ThenUndo_LeavesTheFolderAsItWas()
    {
        using var setup = new Setup(new ShellLikeIconWriter());
        AddText(setup, @"Desktop\a.pdf", "first");
        AddText(setup, @"Desktop\c.png", "picture");
        AddText(setup, @"Desktop\Images folder\old.png", "already sorted");
        Dictionary<string, string> before = Snapshot(setup.Desktop);

        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());

        string documents = setup.Tree.PathOf(@"Desktop\Documents folder");
        Assert.True(File.Exists(Path.Combine(documents, FolderIcon.DesktopIni)));
        Assert.Equal("documents picture", File.ReadAllText(Path.Combine(documents, FolderIcon.IconFile)));
        Assert.False(File.Exists(setup.Tree.PathOf(@"Desktop\Images folder\desktop.ini")));
        Assert.NotNull(Assert.Single(setup.Journal.ReadAll().Select(entry => entry.Undo).OfType<CreatedFolder>()).Icon);

        Assert.Empty(setup.Screen.UndoBatch(outcome.BatchId));

        Assert.Equal(before, Snapshot(setup.Desktop));
        Assert.False(Directory.Exists(documents));
    }

    [Fact]
    public void IconFolder_WithSomethingNewInside_KeepsItsIconOnUndo()
    {
        using var setup = new Setup(new ShellLikeIconWriter());
        AddText(setup, @"Desktop\a.pdf", "a");
        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());
        string newFile = AddText(setup, @"Desktop\Documents folder\new.txt", "made later");

        BatchUndoFailure failure = Assert.Single(setup.Screen.UndoBatch(outcome.BatchId));

        Assert.Equal(UndoFailureReason.FolderNotEmpty, failure.Reason);
        Assert.True(File.Exists(newFile));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Documents folder\desktop.ini")));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Documents folder\folder.ico")));
    }

    [Fact]
    public void IconFolder_WhoseDesktopIniWasChanged_IsLeftAloneOnUndo()
    {
        using var setup = new Setup(new ShellLikeIconWriter());
        AddText(setup, @"Desktop\a.pdf", "a");
        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());
        string desktopIni = setup.Tree.PathOf(@"Desktop\Documents folder\desktop.ini");
        File.SetAttributes(desktopIni, FileAttributes.Normal);
        File.AppendAllText(desktopIni, "[ViewState]\r\nMode=4\r\n");

        BatchUndoFailure failure = Assert.Single(setup.Screen.UndoBatch(outcome.BatchId));

        Assert.Equal(UndoFailureReason.FolderNotEmpty, failure.Reason);
        Assert.True(File.Exists(desktopIni));
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Documents folder\folder.ico")));
    }

    [Fact]
    public void IconWriterRefusingHalfway_StillSortsAndUndoTakesWhatItLeft()
    {
        using var setup = new Setup(new ShellLikeIconWriter(refuseAfterCopy: true));
        AddText(setup, @"Desktop\a.pdf", "a");
        Dictionary<string, string> before = Snapshot(setup.Desktop);

        OrganizeOutcome outcome = setup.Organizer.Run(setup.PlanDesktop());

        Assert.Single(outcome.Moved);
        Assert.True(File.Exists(setup.Tree.PathOf(@"Desktop\Documents folder\folder.ico")));
        Assert.Empty(setup.Screen.UndoBatch(outcome.BatchId));
        Assert.Equal(before, Snapshot(setup.Desktop));
        Assert.False(Directory.Exists(setup.Tree.PathOf(@"Desktop\Documents folder")));
    }

    [Fact]
    public void IconFolder_OrganizedAgain_KeepsItsIconFilesInPlace()
    {
        using var setup = new Setup(new ShellLikeIconWriter());
        AddText(setup, @"Desktop\a.pdf", "a");
        setup.Organizer.Run(setup.PlanDesktop());

        IReadOnlyList<FolderItem> items = FolderItems.Read(setup.Tree.PathOf(@"Desktop\Documents folder"));

        Assert.Equal(["a.pdf"], items.Select(item => item.Name));
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
