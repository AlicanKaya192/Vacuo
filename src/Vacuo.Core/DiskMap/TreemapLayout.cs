namespace Vacuo.Core.DiskMap;

public readonly record struct MapRect(double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap (Bruls, Huizing, van Wijk): each box's area follows its value and boxes stay close to
/// square, so even small ones can be seen and clicked. Values must be largest first; a value of nothing gets an
/// empty box and keeps its place in the list.
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<MapRect> Arrange(IReadOnlyList<long> values, MapRect area)
    {
        var boxes = new MapRect[values.Count];
        List<int> shown = Enumerable.Range(0, values.Count).Where(i => values[i] > 0).ToList();
        double total = shown.Sum(i => (double)values[i]);
        if (total <= 0)
        {
            return boxes;
        }

        double scale = area.Width * area.Height / total;
        double x = area.X, y = area.Y, width = area.Width, height = area.Height;
        for (int start = 0; start < shown.Count;)
        {
            int end = EndOfRow(shown, values, start, scale, Math.Min(width, height));
            double rowArea = Enumerable.Range(start, end - start).Sum(i => values[shown[i]] * scale);
            if (width >= height)
            {
                double columnWidth = rowArea / height;
                double top = y;
                for (int i = start; i < end; i++)
                {
                    double boxHeight = values[shown[i]] * scale / columnWidth;
                    boxes[shown[i]] = new MapRect(x, top, columnWidth, boxHeight);
                    top += boxHeight;
                }

                x += columnWidth;
                width -= columnWidth;
            }
            else
            {
                double rowHeight = rowArea / width;
                double left = x;
                for (int i = start; i < end; i++)
                {
                    double boxWidth = values[shown[i]] * scale / rowHeight;
                    boxes[shown[i]] = new MapRect(left, y, boxWidth, rowHeight);
                    left += boxWidth;
                }

                y += rowHeight;
                height -= rowHeight;
            }

            start = end;
        }

        return boxes;
    }

    // A row grows for as long as adding the next box does not make its worst box less square.
    private static int EndOfRow(List<int> shown, IReadOnlyList<long> values, int start, double scale, double side)
    {
        double sum = 0, largest = 0, smallest = double.MaxValue, worst = double.MaxValue;
        int end = start;
        while (end < shown.Count)
        {
            double box = values[shown[end]] * scale;
            double nextSum = sum + box;
            double nextLargest = Math.Max(largest, box);
            double nextSmallest = Math.Min(smallest, box);
            double ratio = Math.Max(side * side * nextLargest / (nextSum * nextSum), nextSum * nextSum / (side * side * nextSmallest));
            if (end > start && ratio > worst)
            {
                break;
            }

            sum = nextSum;
            largest = nextLargest;
            smallest = nextSmallest;
            worst = ratio;
            end++;
        }

        return end;
    }
}
