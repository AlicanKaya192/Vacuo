using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class DiskFlowTests
{
    private static CategoryTotal Total(DiskCategory category, long bytes) => new(category, bytes, bytes / 1000.0);

    private static DiskCategories CategoriesOf(IReadOnlyList<CategoryTotal> totals, params CategoryFlow[] flows) =>
        new(totals, flows);

    [Fact]
    public void WhatNoFindingTakes_FlowsIntoKeep()
    {
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.AppsAndData, 300)],
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 50));

        DiskFlow flow = DiskFlow.From(categories);

        Assert.Equal(
            [
                new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 50),
                new CategoryFlow(DiskCategory.AppsAndData, Bucket.Keep, 250),
            ],
            flow.Streams);
    }

    [Fact]
    public void Streams_AreOrderedByCategoryThenBucket()
    {
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.AppsAndData, 100), Total(DiskCategory.DocumentsAndMedia, 100)],
            new CategoryFlow(DiskCategory.DocumentsAndMedia, Bucket.Yours, 40),
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.Yours, 10),
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 20));

        DiskFlow flow = DiskFlow.From(categories);

        Assert.Equal(
            [
                (DiskCategory.AppsAndData, Bucket.RebuildsItself),
                (DiskCategory.AppsAndData, Bucket.Yours),
                (DiskCategory.AppsAndData, Bucket.Keep),
                (DiskCategory.DocumentsAndMedia, Bucket.Yours),
                (DiskCategory.DocumentsAndMedia, Bucket.Keep),
            ],
            flow.Streams.Select(stream => (stream.Category, stream.Bucket)));
    }

    [Fact]
    public void EmptyCategory_HasNoStream_ButIsStillListed()
    {
        DiskCategories categories = CategoriesOf([Total(DiskCategory.AppsAndData, 100), Total(DiskCategory.AiAgents, 0)]);

        DiskFlow flow = DiskFlow.From(categories);

        Assert.DoesNotContain(flow.Streams, stream => stream.Category == DiskCategory.AiAgents);
        Assert.Contains(flow.Categories, total => total.Category == DiskCategory.AiAgents);
    }

    [Fact]
    public void FindingsAsLargeAsTheCategory_LeaveNothingToKeep()
    {
        // A category can measure smaller than its findings (compressed folders, a stale measurement).
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.Developer, 100)],
            new CategoryFlow(DiskCategory.Developer, Bucket.RebuildsItself, 120));

        DiskFlow flow = DiskFlow.From(categories);

        Assert.DoesNotContain(flow.Streams, stream => stream.Bucket == Bucket.Keep);
    }

    [Fact]
    public void TheThreeBuckets_AreAlwaysShown_InOrder()
    {
        DiskFlow flow = DiskFlow.From(CategoriesOf([Total(DiskCategory.AppsAndData, 100)]));

        Assert.Equal([Bucket.RebuildsItself, Bucket.Yours, Bucket.Keep], flow.Buckets);
    }

    [Fact]
    public void AnotherBucketWithAStream_IsShownToo()
    {
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.DocumentsAndMedia, 100)],
            new CategoryFlow(DiskCategory.DocumentsAndMedia, Bucket.SameFiles, 30));

        DiskFlow flow = DiskFlow.From(categories);

        Assert.Equal([Bucket.RebuildsItself, Bucket.Yours, Bucket.SameFiles, Bucket.Keep], flow.Buckets);
    }

    [Fact]
    public void BytesInto_AddsUpEveryCategoryFlowingIntoTheBucket()
    {
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.AppsAndData, 100), Total(DiskCategory.Developer, 60)],
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 30),
            new CategoryFlow(DiskCategory.Developer, Bucket.RebuildsItself, 20));

        DiskFlow flow = DiskFlow.From(categories);

        Assert.Equal(50, flow.BytesInto(Bucket.RebuildsItself));
        Assert.Equal(110, flow.BytesInto(Bucket.Keep));
        Assert.Equal(0, flow.BytesInto(Bucket.Yours));
    }

    [Fact]
    public void Particles_FollowTheBytes_AboveTheMinimum()
    {
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.AppsAndData, 400)],
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 100));

        IReadOnlyList<int> particles = DiskFlow.From(categories).ParticlesPerStream(budget: 400, minimum: 0);

        Assert.Equal([100, 300], particles);
    }

    [Fact]
    public void Particles_GiveEveryStreamTheMinimum_AndStayWithinTheBudget()
    {
        // A tiny cache next to hundreds of gigabytes that stay must still be seen flowing.
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.AppsAndData, 1_000_000), Total(DiskCategory.AiAgents, 10)],
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 1),
            new CategoryFlow(DiskCategory.AiAgents, Bucket.RebuildsItself, 2));

        IReadOnlyList<int> particles = DiskFlow.From(categories).ParticlesPerStream(budget: 600, minimum: 10);

        Assert.Equal(4, particles.Count);
        Assert.All(particles, count => Assert.True(count >= 10));
        Assert.True(particles.Sum() <= 600);
        Assert.Equal(particles.Max(), particles[1]);
    }

    [Fact]
    public void Particles_BelowTheMinimums_StillGiveEachStreamTheMinimum()
    {
        DiskCategories categories = CategoriesOf(
            [Total(DiskCategory.AppsAndData, 100)],
            new CategoryFlow(DiskCategory.AppsAndData, Bucket.RebuildsItself, 50));

        IReadOnlyList<int> particles = DiskFlow.From(categories).ParticlesPerStream(budget: 5, minimum: 10);

        Assert.Equal([10, 10], particles);
    }
}
