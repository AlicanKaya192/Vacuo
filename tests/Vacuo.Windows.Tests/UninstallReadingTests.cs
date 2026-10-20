using System.Diagnostics;
using Vacuo.Core.Uninstall;

namespace Vacuo.Windows.Tests;

/// <summary>Reads this machine's installed programs; only reads, and prints no program names.</summary>
public class UninstallReadingTests
{
    [Fact]
    public void EveryListedProgram_HasAnUninstallerThatParses()
    {
        IReadOnlyList<InstalledProgram> programs = InstalledPrograms.From(UninstallRegistry.Read());

        Assert.NotEmpty(programs);
        Assert.All(programs, program => Assert.NotNull(UninstallCommand.Parse(program.UninstallCommand)));
    }

    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        var clock = Stopwatch.StartNew();
        IReadOnlyList<UninstallEntry> entries = UninstallRegistry.Read();
        IReadOnlyList<InstalledProgram> programs = InstalledPrograms.From(entries);

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{entries.Count} keys, {programs.Count} programs listed in {clock.ElapsedMilliseconds} ms; " +
            $"with size {programs.Count(program => program.SizeBytes is not null)}, with date {programs.Count(program => program.InstalledOn is not null)}, " +
            $"MSI {programs.Count(program => program.UninstallCommand.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))}");
    }
}
