using Vacuo.Core.Maintenance;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Maintenance;

public class MaintenanceSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly MaintenanceSettings On = MaintenanceSettings.Default with { Enabled = true };

    [Fact]
    public void TempItems_OnlyThoseUntouchedForSevenDays()
    {
        var scan = Scan(temp: [Temp(@"T\old", days: 8), Temp(@"T\week", days: 7), Temp(@"T\recent", days: 6)]);

        IReadOnlyList<ScanFinding> chosen = MaintenanceSelection.Select(On, scan, Now);

        Assert.Equal([@"T\old", @"T\week"], chosen.Select(finding => finding.Path));
    }

    [Fact]
    public void DownloadCaches_ButNeverModels()
    {
        var scan = Scan(known: [Known("pip-cache"), Known("npm-cache"), Known("uv-cache"), Known("pnpm-store"),
            Known("conda-pkgs-anaconda"), Known("conda-pkgs-miniconda"), Known("huggingface-hub"), Known("torch-hub")]);

        IReadOnlyList<ScanFinding> chosen = MaintenanceSelection.Select(On, scan, Now);

        Assert.Equal(["pip-cache", "npm-cache", "uv-cache", "pnpm-store", "conda-pkgs-anaconda", "conda-pkgs-miniconda"],
            chosen.Select(finding => finding.Rule.Id));
    }

    [Fact]
    public void ProjectLeftovers_OnlyBytecodeAndCheckpoints_AndOnlyWhenChosen()
    {
        var scan = Scan(projects: [Project("python-bytecode"), Project("jupyter-checkpoints"), Project("node-modules"), Project("python-venv"), Project("rust-target")]);

        Assert.Empty(MaintenanceSelection.Select(On, scan, Now));
        Assert.Equal(["python-bytecode", "jupyter-checkpoints"],
            MaintenanceSelection.Select(On with { ProjectLeftovers = true }, scan, Now).Select(finding => finding.Rule.Id));
    }

    [Fact]
    public void CategoriesTurnedOff_AreLeftAlone()
    {
        var scan = Scan(temp: [Temp(@"T\old", days: 30)], known: [Known("pip-cache")]);

        Assert.Empty(MaintenanceSelection.Select(On with { TempFiles = false, DownloadCaches = false }, scan, Now));
    }

    private static MaintenanceScan Scan(ScanFinding[]? temp = null, ScanFinding[]? known = null, ScanFinding[]? projects = null) =>
        new(temp ?? [], known ?? [], projects ?? []);

    private static ScanFinding Temp(string path, int days) =>
        new(path, UserTempScanner.Rule, new DirectorySize(100, 0), Now.UtcDateTime.AddDays(-days));

    private static ScanFinding Known(string id) =>
        new(id, KnownCacheRules.KnownLocations.Single(rule => rule.Id == id), new DirectorySize(100, 0), Now.UtcDateTime);

    private static ScanFinding Project(string id) =>
        new(id, KnownCacheRules.ProjectArtifacts.Single(rule => rule.Id == id), new DirectorySize(100, 0), Now.UtcDateTime);
}
