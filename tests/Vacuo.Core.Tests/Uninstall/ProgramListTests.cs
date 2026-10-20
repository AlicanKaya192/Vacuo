using Vacuo.Core.Uninstall;

namespace Vacuo.Core.Tests.Uninstall;

public class ProgramListTests
{
    private static InstalledProgram Program(string name, string? publisher = null, long? sizeBytes = null, DateOnly? installedOn = null) =>
        new(name, publisher, Version: null, sizeBytes, installedOn, InstallLocation: null, UninstallCommand: "x.exe");

    private static readonly InstalledProgram Office = Program("Microsoft Office", "Microsoft Corporation", 3_000, new DateOnly(2026, 1, 5));
    private static readonly InstalledProgram Player = Program("Foo Player", "Foo Ltd", 500, new DateOnly(2026, 9, 1));
    private static readonly InstalledProgram Tool = Program("bar tool", publisher: null, sizeBytes: null, installedOn: null);
    private static readonly IReadOnlyList<InstalledProgram> All = [Office, Player, Tool];

    [Fact]
    public void Show_EmptyQueryByName_ListsEverythingAlphabetically()
    {
        Assert.Equal([Tool, Player, Office], ProgramList.Show(All, query: "", ProgramSort.Name));
    }

    [Theory]
    [InlineData("office")]
    [InlineData("  OFFICE ")]
    [InlineData("corporation")]
    public void Show_Query_MatchesNameOrPublisherIgnoringCase(string query)
    {
        Assert.Equal([Office], ProgramList.Show(All, query, ProgramSort.Name));
    }

    [Fact]
    public void Show_BySize_PutsTheLargestFirstAndUnknownSizesLast()
    {
        Assert.Equal([Office, Player, Tool], ProgramList.Show(All, query: "", ProgramSort.Size));
    }

    [Fact]
    public void Show_ByDate_PutsTheNewestFirstAndUnknownDatesLast()
    {
        Assert.Equal([Player, Office, Tool], ProgramList.Show(All, query: "", ProgramSort.InstalledOn));
    }

    [Fact]
    public void IsStillInstalled_IsTrueWhileTheNameIsStillListed()
    {
        InstalledProgram updated = Player with { Version = "7.0", Name = "foo player" };

        Assert.True(ProgramList.IsStillInstalled(Player, [Office, updated]));
        Assert.False(ProgramList.IsStillInstalled(Player, [Office, Tool]));
    }
}
