using System.Text;
using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Ram;

public class ForegroundSnapshotTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static ForegroundHistory RoundTrip(ForegroundHistory history) =>
        ForegroundHistory.From(ForegroundSnapshot.Decode(history.Snapshot().Encode()));

    [Fact]
    public void RoundTrip_KeepsWhenEachAppLeftTheFront()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("code", Start.AddMinutes(5));
        history.Switched("chrome", Start.AddMinutes(50));

        ForegroundHistory copy = RoundTrip(history);

        Assert.Equal(Start.AddMinutes(50), copy.UnusedSince("code"));
        Assert.Null(copy.UnusedSince("chrome"));
        Assert.Equal(Start, copy.UnusedSince("blender"));
    }

    [Fact]
    public void RoundTrip_EmptyHistory_KeepsTheStart()
    {
        ForegroundHistory copy = RoundTrip(new ForegroundHistory(Start));

        Assert.Equal(Start, copy.UnusedSince("code"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"inFront\":null,\"leftFront\":{}}")]
    [InlineData("{\"startedAt\":\"yesterday\",\"inFront\":null,\"leftFront\":{}}")]
    [InlineData("{\"startedAt\":\"2026-10-20T09:00:00+00:00\",\"inFront\":null,\"leftFront\":{\"code\":42}}")]
    [InlineData("{\"startedAt\":\"2026-10-20T09:00:00+00:00\",\"inFront\":null,\"leftFront\":{\"\":\"2026-10-20T09:00:00+00:00\"}}")]
    public void Decode_SomethingElse_Throws(string text)
    {
        Assert.Throws<FormatException>(() => ForegroundSnapshot.Decode(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Decode_MoreThanTheLimit_Throws()
    {
        byte[] tooLong = new byte[ForegroundSnapshot.MaxBytes + 1];

        Assert.Throws<FormatException>(() => ForegroundSnapshot.Decode(tooLong));
    }
}
