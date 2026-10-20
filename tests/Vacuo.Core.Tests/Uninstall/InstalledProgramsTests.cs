using Vacuo.Core.Uninstall;

namespace Vacuo.Core.Tests.Uninstall;

public class InstalledProgramsTests
{
    private static UninstallEntry Entry(
        string? name = "Foo", string? uninstall = "\"C:\\Foo\\unins000.exe\"", string? version = "1.0",
        bool systemComponent = false, string? parent = null, string? releaseType = null,
        int? sizeKb = null, string? installDate = null, string? publisher = null) =>
        new(name, publisher, version, sizeKb, installDate, InstallLocation: null, uninstall, systemComponent, parent, releaseType);

    [Fact]
    public void AnOrdinaryProgram_IsListed()
    {
        InstalledProgram program = InstalledPrograms.From([Entry(publisher: "Foo Ltd", sizeKb: 2048, installDate: "20260915")]).Single();

        Assert.Equal("Foo", program.Name);
        Assert.Equal("Foo Ltd", program.Publisher);
        Assert.Equal(2048L * 1024, program.SizeBytes);
        Assert.Equal(new DateOnly(2026, 9, 15), program.InstalledOn);
        Assert.Equal("\"C:\\Foo\\unins000.exe\"", program.UninstallCommand);
    }

    [Fact]
    public void WithoutANameOrAnUninstaller_IsNotListed()
    {
        Assert.Empty(InstalledPrograms.From([Entry(name: null), Entry(name: "  "), Entry(uninstall: null)]));
    }

    [Fact]
    public void SystemComponentsAndUpdates_AreNotListed()
    {
        UninstallEntry[] entries =
        [
            Entry(systemComponent: true),
            Entry(parent: "Microsoft Office"),
            Entry(releaseType: "Security Update"),
            Entry(releaseType: "Hotfix"),
            Entry(releaseType: "Update"),
        ];

        Assert.Empty(InstalledPrograms.From(entries));
    }

    // 32-bit programs often appear under both registry views.
    [Fact]
    public void TheSameProgramTwice_IsListedOnce()
    {
        Assert.Single(InstalledPrograms.From([Entry(), Entry()]));
    }

    [Fact]
    public void ABadInstallDate_IsUnknown()
    {
        Assert.Null(InstalledPrograms.From([Entry(installDate: "2026-09-15")]).Single().InstalledOn);
    }

    [Fact]
    public void ProgramsAreSortedByName()
    {
        IReadOnlyList<InstalledProgram> programs = InstalledPrograms.From([Entry(name: "zip"), Entry(name: "Audio"), Entry(name: "browser")]);

        Assert.Equal(["Audio", "browser", "zip"], programs.Select(program => program.Name));
    }
}
