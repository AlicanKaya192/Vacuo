using Vacuo.Core.Organize;

namespace Vacuo.Core.Tests.Organize;

public class OrganizePlannerTests
{
    private const string Desktop = @"C:\Users\test\Desktop";
    private static readonly DateTimeOffset October = new(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    private static FolderItem Item(string name) => new(name, October, SourceHost: null);

    private static string Under(params string[] parts) => Path.Combine([Desktop, .. parts]);

    private static OrganizePlan Plan(IEnumerable<string> names, string[]? files = null, string[]? folders = null)
    {
        var existingFiles = new HashSet<string>(files ?? [], StringComparer.OrdinalIgnoreCase);
        var existingFolders = new HashSet<string>(folders ?? [], StringComparer.OrdinalIgnoreCase);
        return OrganizePlanner.Plan(
            Desktop,
            names.Select(Item),
            OrganizeRule.ByType,
            target => target.Key + " folder",
            new PathProbe(existingFiles.Contains, existingFolders.Contains));
    }

    [Fact]
    public void EachFile_MovesIntoItsTargetFolderInsideTheSameFolder()
    {
        OrganizePlan plan = Plan(["a.pdf", "b.png"]);

        Assert.Equal(
            [
                new PlannedMove(Under("a.pdf"), Under("Documents folder", "a.pdf")),
                new PlannedMove(Under("b.png"), Under("Images folder", "b.png")),
            ],
            plan.Moves);
    }

    [Fact]
    public void MissingTargetFolders_AreListedOnceToBeCreated()
    {
        OrganizePlan plan = Plan(["a.pdf", "b.pdf", "c.png"], folders: [Under("Images folder")]);

        Assert.Equal([Under("Documents folder")], plan.FoldersToCreate);
    }

    [Fact]
    public void FoldersToCreate_EachKnowTheirTarget()
    {
        OrganizePlan plan = Plan(["a.pdf", "b.pdf", "c.png"], folders: [Under("Images folder")]);

        Assert.Equal(
            new Dictionary<string, OrganizeTarget> { [Under("Documents folder")] = new(OrganizeRule.ByType, "Documents") },
            plan.FolderTargets);
    }

    [Fact]
    public void FileWithoutATarget_IsLeftOut()
    {
        Assert.Empty(Plan(["notes.unknownext"]).Moves);
    }

    [Fact]
    public void NameTakenInTheTargetFolder_GetsANumberInsteadOfOverwriting()
    {
        OrganizePlan plan = Plan(["a.pdf"], files: [Under("Documents folder", "a.pdf"), Under("Documents folder", "a (2).pdf")], folders: [Under("Documents folder")]);

        Assert.Equal(Under("Documents folder", "a (3).pdf"), Assert.Single(plan.Moves).Destination);
    }

    [Fact]
    public void FileInTheWayOfATargetFolder_KeepsThoseFilesWhereTheyAre()
    {
        OrganizePlan plan = Plan(["a.pdf", "b.png"], files: [Under("Documents folder")]);

        Assert.Equal([Under("b.png")], plan.Moves.Select(move => move.Source));
    }

    [Fact]
    public void Moves_ComeInNameOrder()
    {
        OrganizePlan plan = Plan(["c.pdf", "A.pdf", "b.pdf"]);

        Assert.Equal(["A.pdf", "b.pdf", "c.pdf"], plan.Moves.Select(move => Path.GetFileName(move.Source)));
    }
}
