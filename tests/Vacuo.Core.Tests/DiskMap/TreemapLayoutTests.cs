using Vacuo.Core.DiskMap;

namespace Vacuo.Core.Tests.DiskMap;

public class TreemapLayoutTests
{
    private const double Precision = 1e-6;
    private static readonly MapRect Area = new(10, 20, 600, 400);

    private static double AreaOf(MapRect rect) => rect.Width * rect.Height;

    [Fact]
    public void NoValues_NoBoxes()
    {
        Assert.Empty(TreemapLayout.Arrange([], Area));
    }

    [Fact]
    public void OneValue_FillsTheArea()
    {
        Assert.Equal(Area, TreemapLayout.Arrange([5], Area)[0]);
    }

    [Fact]
    public void EveryBox_HasTheAreaOfItsValue()
    {
        long[] values = [500, 300, 120, 50, 20, 10];

        IReadOnlyList<MapRect> boxes = TreemapLayout.Arrange(values, Area);

        double perValue = AreaOf(Area) / values.Sum();
        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(values[i] * perValue, AreaOf(boxes[i]), Precision);
        }
    }

    [Fact]
    public void Boxes_StayInsideTheArea_AndDoNotOverlap()
    {
        IReadOnlyList<MapRect> boxes = TreemapLayout.Arrange([900, 700, 400, 400, 90, 33, 12, 7, 3, 1], Area);

        foreach (MapRect box in boxes)
        {
            Assert.InRange(box.X, Area.X - Precision, Area.X + Area.Width);
            Assert.InRange(box.Y, Area.Y - Precision, Area.Y + Area.Height);
            Assert.True(box.X + box.Width <= Area.X + Area.Width + Precision);
            Assert.True(box.Y + box.Height <= Area.Y + Area.Height + Precision);
        }

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                double overlapWidth = Math.Min(boxes[i].X + boxes[i].Width, boxes[j].X + boxes[j].Width) - Math.Max(boxes[i].X, boxes[j].X);
                double overlapHeight = Math.Min(boxes[i].Y + boxes[i].Height, boxes[j].Y + boxes[j].Height) - Math.Max(boxes[i].Y, boxes[j].Y);
                Assert.False(overlapWidth > Precision && overlapHeight > Precision, $"{i} and {j} overlap");
            }
        }
    }

    // The point of squarified layout: a slice-and-dice map would make hairlines of these.
    [Fact]
    public void Boxes_AreCloseToSquare()
    {
        long[] values = Enumerable.Range(1, 12).Select(i => (long)(13 - i) * 10).ToArray();

        foreach (MapRect box in TreemapLayout.Arrange(values, new MapRect(0, 0, 600, 400)))
        {
            Assert.True(Math.Max(box.Width / box.Height, box.Height / box.Width) < 3, $"{box.Width}x{box.Height}");
        }
    }

    [Fact]
    public void AValueOfNothing_GetsAnEmptyBox_AndKeepsItsPlaceInTheList()
    {
        IReadOnlyList<MapRect> boxes = TreemapLayout.Arrange([10, 0], Area);

        Assert.Equal(Area, boxes[0]);
        Assert.Equal(0, AreaOf(boxes[1]));
    }

    [Fact]
    public void ValuesAsLargeAsADrive_DoNotOverflow()
    {
        long terabytes = 8L << 40;

        Assert.Equal(AreaOf(Area) / 2, AreaOf(TreemapLayout.Arrange([terabytes, terabytes], Area)[0]), Precision);
    }
}
