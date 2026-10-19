using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class KnownCacheRulesTests
{
    [Fact]
    public void RuleIds_AreUnique()
    {
        string[] ids = KnownCacheRules.All.Select(rule => rule.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void KnownLocations_AreRelativePathsThatStayInsideTheirRoot()
    {
        foreach (KnownLocationRule rule in KnownCacheRules.KnownLocations)
        {
            Assert.False(Path.IsPathRooted(rule.RelativePath), rule.Id);
            Assert.DoesNotContain("..", rule.RelativePath.Split('\\'));
        }
    }
}
