using System.Text.Json;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Elevated.Tests;

public class ElevatedAppTests
{
    private sealed class FakeRunner(int exitCode) : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public int Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments));
            return exitCode;
        }
    }

    private sealed class Setup : IDisposable
    {
        public TempTree Tree { get; } = new();

        public WindowsLocations Machine => new(Tree.PathOf("drive"), Tree.PathOf(@"drive\Windows"), Tree.PathOf("user-temp"));

        public string ResultFolder => Tree.PathOf("results");

        public ElevatedResult ResultOf(Guid id) =>
            JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(Path.Combine(ResultFolder, $"{id:D}.json")))!;

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void Run_WindowsTemp_EmptiesOnlyThatFolderAndWritesTheResult()
    {
        using var setup = new Setup();
        setup.Tree.AddFile(@"drive\Windows\Temp\a.tmp", 100);
        string keep = setup.Tree.AddFile(@"drive\Windows\System32\keep.dll", 10);
        string userTemp = setup.Tree.AddFile(@"user-temp\mine.tmp", 10);
        var id = Guid.NewGuid();

        int exitCode = ElevatedApp.Run(["windows-temp", id.ToString("D")], setup.Machine, new FakeRunner(0), setup.ResultFolder);

        Assert.Equal(ElevatedApp.Succeeded, exitCode);
        Assert.False(File.Exists(setup.Tree.PathOf(@"drive\Windows\Temp\a.tmp")));
        Assert.True(Directory.Exists(setup.Tree.PathOf(@"drive\Windows\Temp")));
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(userTemp));
        Assert.Equal(new ElevatedResult("windows-temp", true, 100, 1, 0, ExitCode: null, Error: null), setup.ResultOf(id));
    }

    [Fact]
    public void Run_RefusedRequest_TouchesNothingAndWritesNoResult()
    {
        using var setup = new Setup();
        string file = setup.Tree.AddFile(@"drive\Windows\Temp\a.tmp", 100);

        int exitCode = ElevatedApp.Run([setup.Tree.PathOf(@"drive\Windows\Temp"), Guid.NewGuid().ToString("D")], setup.Machine, new FakeRunner(0), setup.ResultFolder);

        Assert.Equal(ElevatedApp.Refused, exitCode);
        Assert.True(File.Exists(file));
        Assert.False(Directory.Exists(setup.ResultFolder));
    }

    [Fact]
    public void Run_ComponentCleanup_RunsDismWithStartComponentCleanupOnly()
    {
        using var setup = new Setup();
        var runner = new FakeRunner(0);
        var id = Guid.NewGuid();

        int exitCode = ElevatedApp.Run(["component-cleanup", id.ToString("D")], setup.Machine, runner, setup.ResultFolder);

        Assert.Equal(ElevatedApp.Succeeded, exitCode);
        (string fileName, IReadOnlyList<string> arguments) = Assert.Single(runner.Calls);
        Assert.Equal(setup.Tree.PathOf(@"drive\Windows\System32\Dism.exe"), fileName);
        Assert.Equal(["/Online", "/Cleanup-Image", "/StartComponentCleanup"], arguments);
        Assert.Equal(new ElevatedResult("component-cleanup", true, 0, 0, 0, ExitCode: 0, Error: null), setup.ResultOf(id));
    }

    [Fact]
    public void Run_ComponentCleanupThatFails_ReportsDismsExitCode()
    {
        using var setup = new Setup();
        var id = Guid.NewGuid();

        int exitCode = ElevatedApp.Run(["component-cleanup", id.ToString("D")], setup.Machine, new FakeRunner(87), setup.ResultFolder);

        Assert.Equal(ElevatedApp.Failed, exitCode);
        Assert.Equal(new ElevatedResult("component-cleanup", false, 0, 0, 0, ExitCode: 87, Error: null), setup.ResultOf(id));
    }
}
