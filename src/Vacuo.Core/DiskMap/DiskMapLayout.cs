namespace Vacuo.Core.DiskMap;

public enum MapItemKind
{
    Folder,
    File,

    /// <summary>Small files and whatever did not fit the box limit, as one total.</summary>
    Rest,

    /// <summary>The used space no readable folder accounts for; only on the drive's top level.</summary>
    Unread,
}

public sealed record MapItem(MapItemKind Kind, long Bytes, FolderNode? Node);

/// <summary>One box on the map. <see cref="Rect"/> is relative to the map, or to the outer box for an inner one.</summary>
public sealed record MapBox(MapItem Item, MapRect Rect, int Heat, bool IsCore, IReadOnlyList<MapBox> Inner);

/// <summary>
/// The "gravity map" the user picked (draft B, Aşama 3k): a large folder already shows what is inside it, colour
/// follows the share of what is on screen, and the largest thing that can be seen into is drawn as the black hole.
/// </summary>
public static class DiskMapLayout
{
    public const int MostBoxes = 120;
    public const int HottestHeat = 5;
    public const double HeaderHeight = 22;

    private const double SmallestNestedWidth = 120;
    private const double SmallestNestedHeight = 84;
    private const double InnerMargin = 3;

    private static readonly double[] HeatSteps = [0.012, 0.03, 0.07, 0.15, 0.3];

    public static IReadOnlyList<MapBox> Arrange(FolderNode folder, long unreadBytes, double width, double height)
    {
        double viewBytes = folder.Bytes + unreadBytes;
        List<MapBox> boxes = Place(ItemsOf(folder, unreadBytes), new MapRect(0, 0, width, height), viewBytes)
            .Select(box => WithInner(box, viewBytes))
            .ToList();

        MapBox? core = boxes.SelectMany(box => box.Inner.Count > 0 ? box.Inner : [box])
            .Where(box => box.Item.Kind is MapItemKind.Folder or MapItemKind.File)
            .MaxBy(box => box.Item.Bytes);
        return core is null ? boxes : boxes.Select(box => MarkCore(box, core)).ToList();
    }

    private static List<MapItem> ItemsOf(FolderNode folder, long unreadBytes)
    {
        int room = MostBoxes - (unreadBytes > 0 ? 2 : 1);
        List<MapItem> items = folder.Children.Take(room)
            .Select(child => new MapItem(child.IsFile ? MapItemKind.File : MapItemKind.Folder, child.Bytes, child))
            .ToList();
        long rest = folder.SmallBytes + folder.Children.Skip(room).Sum(child => child.Bytes);
        if (rest > 0)
        {
            items.Add(new MapItem(MapItemKind.Rest, rest, Node: null));
        }

        if (unreadBytes > 0)
        {
            items.Add(new MapItem(MapItemKind.Unread, unreadBytes, Node: null));
        }

        return items.OrderByDescending(item => item.Bytes).ToList();
    }

    private static IEnumerable<MapBox> Place(List<MapItem> items, MapRect area, double viewBytes)
    {
        IReadOnlyList<MapRect> rects = TreemapLayout.Arrange(items.Select(item => item.Bytes).ToList(), area);
        return items.Select((item, i) => new MapBox(item, rects[i], HeatOf(item.Bytes / viewBytes), IsCore: false, Inner: []));
    }

    private static MapBox WithInner(MapBox box, double viewBytes)
    {
        if (box.Item.Node is not { Children.Count: > 0 } node || box.Rect.Width < SmallestNestedWidth || box.Rect.Height < SmallestNestedHeight)
        {
            return box;
        }

        var area = new MapRect(InnerMargin, HeaderHeight, box.Rect.Width - 2 * InnerMargin, box.Rect.Height - HeaderHeight - InnerMargin);
        return box with { Inner = Place(ItemsOf(node, unreadBytes: 0), area, viewBytes).ToList() };
    }

    private static MapBox MarkCore(MapBox box, MapBox core) => ReferenceEquals(box, core)
        ? box with { IsCore = true }
        : box with { Inner = box.Inner.Select(inner => ReferenceEquals(inner, core) ? inner with { IsCore = true } : inner).ToList() };

    private static int HeatOf(double share)
    {
        int heat = Array.FindIndex(HeatSteps, step => share < step);
        return heat < 0 ? HottestHeat : heat;
    }
}
