using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class UserTempScannerTests
{
    [Fact]
    public void Scan_ListsEachTopLevelFileAndFolderWithItsSize()
    {
        using var tree = new TempTree();
        tree.AddFile(@"temp\setup.log", 40);
        tree.AddFile(@"temp\installer\a.bin", 100);
        tree.AddFile(@"temp\installer\deep\b.bin", 60);

        ScanResult result = new UserTempScanner(tree.PathOf("temp")).Scan();

        Assert.Equal(
            [(tree.PathOf(@"temp\installer"), 160L), (tree.PathOf(@"temp\setup.log"), 40L)],
            result.Findings.Select(finding => (finding.Path, finding.Size.Bytes)).OrderBy(pair => pair.Path));
        Assert.All(result.Findings, finding => Assert.Equal(UserTempScanner.Rule, finding.Rule));
    }

    [Fact]
    public void Scan_LinkAtTheTopLevel_IsNotListed()
    {
        using var tree = new TempTree();
        tree.AddFile(@"elsewhere\keep.bin", 70);
        tree.AddDirectory("temp");
        tree.AddJunction(@"temp\link", "elsewhere");

        Assert.Empty(new UserTempScanner(tree.PathOf("temp")).Scan().Findings);
    }

    [Fact]
    public void Scan_MissingTempFolder_FindsNothing()
    {
        using var tree = new TempTree();

        ScanResult result = new UserTempScanner(tree.PathOf("no-such-temp")).Scan();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.SkippedEntries);
    }

    [Fact]
    public void Scan_TempThatIsALink_IsNotFollowed()
    {
        using var tree = new TempTree();
        tree.AddFile(@"elsewhere\keep.bin", 70);
        tree.AddJunction("temp", "elsewhere");

        Assert.Empty(new UserTempScanner(tree.PathOf("temp")).Scan().Findings);
    }
}
