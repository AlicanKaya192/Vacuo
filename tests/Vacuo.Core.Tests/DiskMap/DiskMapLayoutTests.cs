using Vacuo.Core.DiskMap;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.DiskMap;

public class DiskMapLayoutTests
{
    private const long Kept = 100;
    private const double Width = 688;
    private const double Height = 416;

    private static FolderNode Root(TempTree tree) => new FolderTreeScanner(Kept).Scan(tree.Root, 0).Root;

    private static IReadOnlyList<MapBox> Arrange(TempTree tree, long unread = 0) =>
        DiskMapLayout.Arrange(Root(tree), unread, Width, Height);

    private static MapBox Box(IEnumerable<MapBox> boxes, string name) => boxes.Single(box => box.Item.Node?.Name == name);

    [Fact]
    public void Items_AreLargestFirst_WithSmallFilesAsOneBox()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Big\a.bin", 500);
        tree.AddFile("middle.bin", 200);
        tree.AddFile("tiny.txt", 40);

        IReadOnlyList<MapBox> boxes = Arrange(tree);

        Assert.Equal([MapItemKind.Folder, MapItemKind.File, MapItemKind.Rest], boxes.Select(box => box.Item.Kind));
        Assert.Equal(40, boxes[2].Item.Bytes);
    }

    [Fact]
    public void WhatNoFolderAccountsFor_IsItsOwnBox()
    {
        using var tree = new TempTree();
        tree.AddFile("a.bin", 300);

        MapBox unread = Arrange(tree, unread: 900)[0];

        Assert.Equal(MapItemKind.Unread, unread.Item.Kind);
        Assert.Equal(900, unread.Item.Bytes);
        Assert.Null(unread.Item.Node);
    }

    [Fact]
    public void AFolderWithThousandsOfChildren_StaysWithinTheBoxLimit()
    {
        using var tree = new TempTree();
        for (int i = 0; i < DiskMapLayout.MostBoxes + 30; i++)
        {
            tree.AddFile($"file{i:D3}.bin", 100 + i);
        }

        IReadOnlyList<MapBox> boxes = Arrange(tree);

        Assert.Equal(DiskMapLayout.MostBoxes, boxes.Count);
        MapBox rest = boxes.Single(box => box.Item.Kind == MapItemKind.Rest);
        Assert.True(rest.Item.Bytes > 0);
        Assert.Equal(boxes.Sum(box => box.Item.Bytes), Root(tree).Bytes);
    }

    [Fact]
    public void ALargeFolder_ShowsWhatIsInsideIt_UnderItsHeader()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Big\a.bin", 600);
        tree.AddFile(@"Big\b.bin", 400);
        tree.AddFile("other.bin", 300);

        MapBox big = Box(Arrange(tree), "Big");

        Assert.Equal(["a.bin", "b.bin"], big.Inner.Select(box => box.Item.Node!.Name));
        Assert.All(big.Inner, inner =>
        {
            Assert.True(inner.Rect.Y >= DiskMapLayout.HeaderHeight - 1e-6);
            Assert.True(inner.Rect.X + inner.Rect.Width <= big.Rect.Width + 1e-6);
            Assert.True(inner.Rect.Y + inner.Rect.Height <= big.Rect.Height + 1e-6);
        });
    }

    [Fact]
    public void ASmallFolder_IsOneFlatBox()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Huge\x.bin", 100_000);
        tree.AddFile(@"Little\y.bin", 150);
        tree.AddFile(@"Little\z.bin", 150);

        Assert.Empty(Box(Arrange(tree), "Little").Inner);
    }

    [Fact]
    public void TheLargestLeaf_IsTheCore_AndOnlyOne()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Big\a.bin", 800);
        tree.AddFile(@"Big\b.bin", 400);
        tree.AddFile("other.bin", 500);

        IReadOnlyList<MapBox> boxes = Arrange(tree);
        List<MapBox> all = boxes.Concat(boxes.SelectMany(box => box.Inner)).ToList();

        MapBox core = Assert.Single(all, box => box.IsCore);
        Assert.Equal("a.bin", core.Item.Node!.Name);
    }

    [Fact]
    public void WhatCannotBeSeenInto_IsNeverTheCore()
    {
        using var tree = new TempTree();
        tree.AddFile("a.bin", 200);

        IReadOnlyList<MapBox> boxes = Arrange(tree, unread: 9_000);

        Assert.False(boxes.Single(box => box.Item.Kind == MapItemKind.Unread).IsCore);
        Assert.True(boxes.Single(box => box.Item.Kind == MapItemKind.File).IsCore);
    }

    [Fact]
    public void Heat_RisesWithTheShareOfTheView()
    {
        using var tree = new TempTree();
        tree.AddFile("huge.bin", 9_000);
        tree.AddFile("small.bin", 200);

        IReadOnlyList<MapBox> boxes = Arrange(tree);

        Assert.True(boxes[0].Heat > boxes[1].Heat);
        Assert.InRange(boxes[1].Heat, 0, DiskMapLayout.HottestHeat);
        Assert.Equal(DiskMapLayout.HottestHeat, boxes[0].Heat);
    }

    [Fact]
    public void AnEmptyFolder_HasNoBoxes()
    {
        using var tree = new TempTree();

        Assert.Empty(Arrange(tree));
    }
}
