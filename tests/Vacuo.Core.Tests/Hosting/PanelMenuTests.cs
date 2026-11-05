using Vacuo.Core.Hosting;

namespace Vacuo.Core.Tests.Hosting;

public class PanelMenuTests
{
    [Fact]
    public void Tabs_AreDiskProgramsOrganizeHistoryInThatOrder()
    {
        Assert.Equal([PanelTab.Disk, PanelTab.Programs, PanelTab.Organize, PanelTab.History], PanelMenu.Tabs);
    }

    [Fact]
    public void EveryFeature_IsOnExactlyOneTab()
    {
        foreach (PanelFeature feature in Enum.GetValues<PanelFeature>())
        {
            int tabs = PanelMenu.Tabs.Count(tab => PanelMenu.FeaturesOn(tab, hasBattery: true).Contains(feature));

            Assert.True(tabs == 1, $"{feature} is on {tabs} tabs");
        }
    }

    [Fact]
    public void FeatureTab_MatchesTheTabListingIt()
    {
        foreach (PanelTab tab in PanelMenu.Tabs)
        {
            Assert.All(PanelMenu.FeaturesOn(tab, hasBattery: true), feature => Assert.Equal(tab, PanelMenu.TabOf(feature)));
        }
    }

    [Fact]
    public void Battery_IsListedOnlyWhenThereIsOne()
    {
        Assert.Contains(PanelFeature.Battery, PanelMenu.FeaturesOn(PanelTab.Programs, hasBattery: true));
        Assert.DoesNotContain(PanelFeature.Battery, PanelMenu.FeaturesOn(PanelTab.Programs, hasBattery: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoTab_IsEmpty(bool hasBattery)
    {
        Assert.All(PanelMenu.Tabs, tab => Assert.NotEmpty(PanelMenu.FeaturesOn(tab, hasBattery)));
    }

    [Fact]
    public void Groups_AreTheOnesAlicanChose()
    {
        Assert.Equal(
            [PanelFeature.DiskCleanup, PanelFeature.WindowsLeftovers, PanelFeature.DiskMap, PanelFeature.DiskGrowth, PanelFeature.Duplicates],
            PanelMenu.FeaturesOn(PanelTab.Disk, hasBattery: false));
        Assert.Equal(
            [PanelFeature.Uninstall, PanelFeature.Drivers, PanelFeature.Memory],
            PanelMenu.FeaturesOn(PanelTab.Programs, hasBattery: false));
        Assert.Equal([PanelFeature.Organize, PanelFeature.OrganizeReminders], PanelMenu.FeaturesOn(PanelTab.Organize, hasBattery: false));
        Assert.Equal([PanelFeature.Journal, PanelFeature.Week], PanelMenu.FeaturesOn(PanelTab.History, hasBattery: false));
    }
}
