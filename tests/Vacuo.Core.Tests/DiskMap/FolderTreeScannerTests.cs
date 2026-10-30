using System.Runtime.Versioning;
using Vacuo.Core.DiskMap;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.DiskMap;

public class FolderTreeScannerTests
{
    private const long Kept = 100;

    private static FolderTreeScan Scan(TempTree tree, long usedBytes = 0, IProgress<double>? progress = null, CancellationToken cancellation = default) =>
        new FolderTreeScanner(Kept).Scan(tree.Root, usedBytes, progress, cancellation);

    private static FolderNode Child(FolderNode node, string name) => node.Children.Single(child => child.Name == name);

    private sealed class Reports : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }

    [Fact]
    public void Root_AddsUpEverythingBelowIt()
    {
        using var tree = new TempTree();
        tree.AddFile("a.bin", 300);
        tree.AddFile(@"Docs\b.bin", 200);
        tree.AddFile(@"Docs\Deep\c.bin", 50);

        Assert.Equal(550, Scan(tree).Root.Bytes);
    }

    [Fact]
    public void AFolder_HoldsItsOwnTotal()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Docs\b.bin", 200);
        tree.AddFile(@"Docs\Deep\c.bin", 150);

        FolderNode docs = Child(Scan(tree).Root, "Docs");

        Assert.False(docs.IsFile);
        Assert.Equal(350, docs.Bytes);
    }

    [Fact]
    public void ALargeFile_IsItsOwnNode()
    {
        using var tree = new TempTree();
        tree.AddFile("big.iso", 500);

        FolderNode file = Child(Scan(tree).Root, "big.iso");

        Assert.True(file.IsFile);
        Assert.Equal(500, file.Bytes);
    }

    [Fact]
    public void SmallFiles_AreOneTotalNotNodes()
    {
        using var tree = new TempTree();
        tree.AddFile("a.txt", 40);
        tree.AddFile("b.txt", 30);

        FolderNode root = Scan(tree).Root;

        Assert.Empty(root.Children);
        Assert.Equal(70, root.SmallBytes);
        Assert.Equal(70, root.Bytes);
    }

    [Fact]
    public void ASmallFolder_FoldsIntoItsParent()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Tiny\a.txt", 40);

        FolderNode root = Scan(tree).Root;

        Assert.Empty(root.Children);
        Assert.Equal(40, root.SmallBytes);
    }

    [Fact]
    public void AFolderOfManySmallFiles_StaysWhenItIsLargeInTotal()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Many\a.txt", 60);
        tree.AddFile(@"Many\b.txt", 60);

        FolderNode many = Child(Scan(tree).Root, "Many");

        Assert.Equal(120, many.SmallBytes);
    }

    [Fact]
    public void Children_AreLargestFirst()
    {
        using var tree = new TempTree();
        tree.AddFile("middle.bin", 200);
        tree.AddFile(@"Largest\x.bin", 300);
        tree.AddFile("smallest.bin", 150);

        Assert.Equal(["Largest", "middle.bin", "smallest.bin"], Scan(tree).Root.Children.Select(child => child.Name));
    }

    [Fact]
    public void FullPath_IsWhereTheNodeIsOnDisk()
    {
        using var tree = new TempTree();
        string file = tree.AddFile(@"Docs\Deep\c.bin", 300);

        FolderNode deep = Child(Child(Scan(tree).Root, "Docs"), "Deep");

        Assert.Equal(file, Child(deep, "c.bin").FullPath);
    }

    [Fact]
    public void AJunction_IsNeitherFollowedNorCounted()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Real\a.bin", 300);
        tree.AddJunction("Link", "Real");

        FolderNode root = Scan(tree).Root;

        Assert.Equal(300, root.Bytes);
        Assert.DoesNotContain(root.Children, child => child.Name == "Link");
    }

    [Fact]
    public void OfflineFiles_TakeNoSpace()
    {
        using var tree = new TempTree();
        string online = tree.AddFile("cloud.bin", 500);
        File.SetAttributes(online, FileAttributes.Offline);

        Assert.Equal(0, Scan(tree).Root.Bytes);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void AnUnreadableFolder_IsCountedAsSkipped()
    {
        using var tree = new TempTree();
        tree.AddFile("readable.bin", 30);
        tree.AddFile(@"locked\hidden.bin", 900);
        tree.DenyListing("locked");

        FolderTreeScan scan = Scan(tree);

        Assert.Equal(1, scan.SkippedFolders);
        Assert.Equal(30, scan.Root.Bytes);
    }

    [Fact]
    public void AMissingRoot_IsSkippedNotAnError()
    {
        using var tree = new TempTree();

        FolderTreeScan scan = new FolderTreeScanner(Kept).Scan(tree.PathOf("gone"), 0);

        Assert.Equal(1, scan.SkippedFolders);
        Assert.Equal(0, scan.Root.Bytes);
    }

    [Fact]
    public void UnreadBytes_AreTheUsedSpaceNothingWasFoundFor()
    {
        using var tree = new TempTree();
        tree.AddFile("a.bin", 300);

        Assert.Equal(700, Scan(tree, usedBytes: 1_000).UnreadBytes);
    }

    // Hard links count once per name, so what was found can exceed the used space.
    [Fact]
    public void UnreadBytes_AreNeverNegative()
    {
        using var tree = new TempTree();
        tree.AddFile("a.bin", 300);

        Assert.Equal(0, Scan(tree, usedBytes: 100).UnreadBytes);
    }

    [Fact]
    public void Progress_EndsAtOne_AndNeverGoesBack()
    {
        using var tree = new TempTree();
        tree.AddFile(@"A\a.bin", 300);
        tree.AddFile(@"B\b.bin", 300);
        var reports = new Reports();

        Scan(tree, usedBytes: 1_000, progress: reports);

        Assert.Equal(1.0, reports.Values[^1]);
        Assert.Equal(reports.Values.Order(), reports.Values);
        Assert.All(reports.Values, value => Assert.InRange(value, 0.0, 1.0));
    }

    [Fact]
    public void Cancelling_StopsTheScan()
    {
        using var tree = new TempTree();
        tree.AddFile(@"A\a.bin", 300);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Scan(tree, cancellation: cancellation.Token));
    }
}
