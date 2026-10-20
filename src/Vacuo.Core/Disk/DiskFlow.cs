using Vacuo.Core.Scanning;

namespace Vacuo.Core.Disk;

/// <summary>
/// What the Disk screen's flow strip draws (Aşama 3d A, dilim 2): every category on the left, the
/// buckets on the right and one stream for each pair that shares bytes. What the scan found goes
/// to its bucket; the rest of each category stays, so it flows into "Keep" and the streams add up
/// to the used space.
/// </summary>
public sealed record DiskFlow(IReadOnlyList<CategoryTotal> Categories, IReadOnlyList<Bucket> Buckets, IReadOnlyList<CategoryFlow> Streams)
{
    private static readonly Bucket[] AlwaysShown = [Bucket.RebuildsItself, Bucket.Yours, Bucket.Keep];

    public static DiskFlow From(DiskCategories categories)
    {
        var streams = new List<CategoryFlow>(categories.Flows);
        foreach (CategoryTotal total in categories.Totals)
        {
            long found = categories.Flows.Where(flow => flow.Category == total.Category).Sum(flow => flow.Bytes);
            long stays = total.Bytes - found;
            if (stays > 0)
            {
                streams.Add(new CategoryFlow(total.Category, Bucket.Keep, stays));
            }
        }

        List<CategoryFlow> ordered = streams
            .Where(stream => stream.Bytes > 0)
            .OrderBy(stream => stream.Category)
            .ThenBy(stream => stream.Bucket)
            .ToList();
        List<Bucket> buckets = AlwaysShown.Union(ordered.Select(stream => stream.Bucket)).Order().ToList();
        return new DiskFlow(categories.Totals, buckets, ordered);
    }

    public long BytesInto(Bucket bucket) => Streams.Where(stream => stream.Bucket == bucket).Sum(stream => stream.Bytes);

    /// <summary>
    /// How many particles each stream gets, in <see cref="Streams"/> order: every stream the minimum,
    /// so a small cache is still seen flowing, and the rest of the budget by bytes.
    /// </summary>
    public IReadOnlyList<int> ParticlesPerStream(int budget, int minimum)
    {
        long totalBytes = Streams.Sum(stream => stream.Bytes);
        int rest = Math.Max(0, budget - Streams.Count * minimum);
        return Streams
            .Select(stream => minimum + (int)(rest * ((double)stream.Bytes / totalBytes)))
            .ToList();
    }
}
