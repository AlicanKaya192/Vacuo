using Vacuo.Cli;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Cli.Tests;

public class ScanCommandTests
{
    private sealed record Run(int ExitCode, string Output, string Error);

    private static Run Execute(TempTree tree, params string[] args)
    {
        var context = new ScanContext(
            new KnownRoots(LocalAppData: tree.PathOf("Local"), UserProfile: tree.PathOf("Home")),
            DefaultProjectRoot: tree.PathOf("Home"));
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = ScanCommand.Run(args, context, output, error);

        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private static TempTree TreeWithCaches()
    {
        var tree = new TempTree();
        tree.AddFile(@"Local\pip\Cache\http-v2\entry", 1536);
        tree.AddFile(@"Home\projects\web\package.json", 20);
        tree.AddFile(@"Home\projects\web\node_modules\lib\index.js", 3 * 1024 * 1024);
        tree.AddFile(@"Home\Documents\thesis.docx", 4096);
        return tree;
    }

    [Theory]
    [InlineData]
    [InlineData("temizle")]
    [InlineData("tara", "--bilinmeyen")]
    public void Run_UnknownUsage_PrintsHelpAndReturnsUsageError(params string[] args)
    {
        using var tree = new TempTree();

        Run run = Execute(tree, args);

        Assert.Equal(ScanCommand.UsageError, run.ExitCode);
        Assert.Contains("vacuo tara", run.Error);
    }

    [Fact]
    public void Run_RootOptionWithoutFolder_ReturnsUsageError()
    {
        using var tree = new TempTree();

        Assert.Equal(ScanCommand.UsageError, Execute(tree, "tara", "--kok").ExitCode);
    }

    [Fact]
    public void Run_SystemFolderAsRoot_RefusesWithAMessage()
    {
        using var tree = new TempTree();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Run run = Execute(tree, "tara", "--kok", windows);

        Assert.Equal(ScanCommand.RootRefused, run.ExitCode);
        Assert.Contains(windows, run.Error);
    }

    [Fact]
    public void Run_Scan_ListsEachFindingWithTurkishSizes()
    {
        using TempTree tree = TreeWithCaches();

        Run run = Execute(tree, "tara");

        Assert.Equal(ScanCommand.Success, run.ExitCode);
        Assert.Contains("node-modules", run.Output);
        Assert.Contains("3,0 MB", run.Output);
        Assert.Contains("pip-cache", run.Output);
        Assert.Contains("1,5 KB", run.Output);
        Assert.Contains(tree.PathOf(@"Home\projects\web\node_modules"), run.Output);
        Assert.DoesNotContain("thesis.docx", run.Output);
    }

    [Fact]
    public void Run_Scan_PrintsBucketAndOverallTotals()
    {
        using TempTree tree = TreeWithCaches();

        Run run = Execute(tree, "tara");

        Assert.Contains("Kendini yeniden kurar: 2 öğe, 3,0 MB", run.Output);
        Assert.Contains("Toplam: 3,0 MB", run.Output);
    }

    [Fact]
    public void Run_Scan_LeavesEveryFileInPlace()
    {
        using TempTree tree = TreeWithCaches();
        Dictionary<string, long> before = Snapshot(tree.Root);

        Execute(tree, "tara");

        Assert.Equal(before, Snapshot(tree.Root));
    }

    [Fact]
    public void Run_ScanWithRootOption_SearchesOnlyThatFolderForProjects()
    {
        using TempTree tree = TreeWithCaches();
        tree.AddFile(@"Home\other\app\package.json", 20);
        tree.AddFile(@"Home\other\app\node_modules\x.js", 10);

        Run run = Execute(tree, "tara", "--kok", tree.PathOf(@"Home\other"));

        Assert.Contains(tree.PathOf(@"Home\other\app\node_modules"), run.Output);
        Assert.DoesNotContain(tree.PathOf(@"Home\projects\web\node_modules"), run.Output);
    }

    [Fact]
    public void Run_ScanFindingNothing_SaysSo()
    {
        using var tree = new TempTree();
        tree.AddDirectory("Home");

        Run run = Execute(tree, "tara");

        Assert.Equal(ScanCommand.Success, run.ExitCode);
        Assert.Contains("Yeniden kurulabilir bir şey bulunmadı.", run.Output);
    }

    private static Dictionary<string, long> Snapshot(string root) =>
        new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(file => file.FullName, file => file.Length);
}
