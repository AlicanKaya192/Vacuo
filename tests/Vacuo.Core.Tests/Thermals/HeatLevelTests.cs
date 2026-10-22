using Vacuo.Core.Thermals;

namespace Vacuo.Core.Tests.Thermals;

public class HeatLevelTests
{
    [Theory]
    [InlineData(30, 0f)]
    [InlineData(40, 0f)]
    [InlineData(65, 0.5f)]
    [InlineData(90, 1f)]
    [InlineData(105, 1f)]
    public void Temperature_MapsBetweenCoolAndHot(double celsius, float level)
    {
        Assert.Equal(level, HeatLevel.From(celsius), precision: 3);
    }

    [Fact]
    public void Warmer_IsNeverLessHeat()
    {
        Assert.True(HeatLevel.From(70) > HeatLevel.From(50));
    }
}
