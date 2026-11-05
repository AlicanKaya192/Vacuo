namespace Vacuo.Core.Hosting;

/// <summary>The panel's tabs, left to right.</summary>
public enum PanelTab
{
    Disk,
    Programs,
    Organize,
    History,
}

/// <summary>Everything the panel offers; each has one card, on one tab.</summary>
public enum PanelFeature
{
    DiskCleanup,
    WindowsLeftovers,
    DiskMap,
    DiskGrowth,
    Duplicates,
    Uninstall,
    Drivers,
    Memory,
    Battery,
    Organize,
    OrganizeReminders,
    Journal,
    Week,
}

/// <summary>
/// Which feature lives on which tab (Alican, 5 November 2026: "her birinin ayrı yeri olsun").
/// A new feature joins one of these four groups; a fifth tab is Alican's call.
/// </summary>
public static class PanelMenu
{
    private static readonly IReadOnlyDictionary<PanelTab, PanelFeature[]> Groups = new Dictionary<PanelTab, PanelFeature[]>
    {
        [PanelTab.Disk] = [PanelFeature.DiskCleanup, PanelFeature.WindowsLeftovers, PanelFeature.DiskMap, PanelFeature.DiskGrowth, PanelFeature.Duplicates],
        [PanelTab.Programs] = [PanelFeature.Uninstall, PanelFeature.Drivers, PanelFeature.Memory, PanelFeature.Battery],
        [PanelTab.Organize] = [PanelFeature.Organize, PanelFeature.OrganizeReminders],
        [PanelTab.History] = [PanelFeature.Journal, PanelFeature.Week],
    };

    public static IReadOnlyList<PanelTab> Tabs { get; } = [PanelTab.Disk, PanelTab.Programs, PanelTab.Organize, PanelTab.History];

    /// <summary>The tab's features in card order; the battery only on a machine that has one.</summary>
    public static IReadOnlyList<PanelFeature> FeaturesOn(PanelTab tab, bool hasBattery) =>
        Groups[tab].Where(feature => hasBattery || feature != PanelFeature.Battery).ToList();

    public static PanelTab TabOf(PanelFeature feature) => Groups.Single(group => group.Value.Contains(feature)).Key;
}
