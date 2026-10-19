using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class KnownLocationScannerTests
{
    private static KnownLocationScanner ScannerFor(TempTree tree) =>
        new(new KnownRoots(LocalAppData: tree.PathOf("Local"), UserProfile: tree.PathOf("Home")));

    [Fact]
    public void Scan_PipCache_IsFoundInRebuildsItselfBucketWithItsSize()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Local\pip\Cache\http-v2\a\entry", 300);
        tree.AddFile(@"Local\pip\Cache\wheels\b.whl", 700);

        ScanFinding finding = Assert.Single(ScannerFor(tree).Scan().Findings);

        Assert.Equal("pip-cache", finding.Rule.Id);
        Assert.Equal(Bucket.RebuildsItself, finding.Rule.Bucket);
        Assert.Equal(RebuildCost.Redownloaded, finding.Rule.Cost);
        Assert.Equal(tree.PathOf(@"Local\pip\Cache"), finding.Path);
        Assert.Equal(1000, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_HuggingFaceHub_IsFoundUnderUserProfile()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Home\.cache\huggingface\hub\models--x\blobs\weights", 2048);

        ScanFinding finding = Assert.Single(ScannerFor(tree).Scan().Findings);

        Assert.Equal("huggingface-hub", finding.Rule.Id);
        Assert.Equal(2048, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_NoKnownLocationsExist_FindsNothing()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Local\SomeApp\data.bin", 50);

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }

    [Fact]
    public void Scan_KnownLocationThatIsAJunction_IsNotFollowed()
    {
        using var tree = new TempTree();
        tree.AddFile(@"OtherDrive\pip-moved\entry", 400);
        tree.AddDirectory(@"Local\pip");
        tree.AddJunction(@"Local\pip\Cache", "OtherDrive\\pip-moved");

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }
}
