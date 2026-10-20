using Vacuo.Cli;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;

namespace Vacuo.Cli.Tests;

public class ReleaseCommandTests
{
    private sealed record Run(int ExitCode, string Output, string Error);

    private static Run Execute(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = ReleaseCommand.Run(args, output, error);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void FileList_IsWrittenForTheFolder()
    {
        using var tree = new TempTree();
        tree.AddFile(@"publish\Vacuo.exe", 100);
        tree.AddFile(@"publish\zh-CN\Strings.mui", 10);

        Run run = Execute("dosya-kaydi", tree.PathOf("publish"), "0.5.0", tree.PathOf("files.json"));

        Assert.Equal(ReleaseCommand.Success, run.ExitCode);
        FileManifest written = FileManifest.Parse(File.ReadAllText(tree.PathOf("files.json")));
        Assert.Equal("0.5.0", written.Version);
        Assert.Equal(2, written.Files.Count);
    }

    [Fact]
    public void PatchList_WritesTheChangedFilesAndSaysHowMany()
    {
        using var tree = new TempTree();
        File.WriteAllText(tree.PathOf("old.json"), Manifest("0.4.0", ("Vacuo.exe", "a"), ("Gone.dll", "g")).ToJson());
        File.WriteAllText(tree.PathOf("new.json"), Manifest("0.5.0", ("Vacuo.exe", "b")).ToJson());

        Run run = Execute("yama-listesi", tree.PathOf("old.json"), tree.PathOf("new.json"), tree.PathOf("patch.iss"));

        Assert.Equal(ReleaseCommand.Success, run.ExitCode);
        string sections = File.ReadAllText(tree.PathOf("patch.iss"));
        Assert.Contains("{app}\\Gone.dll", sections, StringComparison.Ordinal);
        Assert.Contains("{#SourceDir}\\Vacuo.exe", sections, StringComparison.Ordinal);
        Assert.Contains("0.4.0 → 0.5.0: 1 dosya yazılacak", run.Output, StringComparison.Ordinal);
        Assert.Contains("1 dosya silinecek", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DamagedList_FailsWithItsReason()
    {
        using var tree = new TempTree();
        File.WriteAllText(tree.PathOf("old.json"), "{ not json");
        File.WriteAllText(tree.PathOf("new.json"), Manifest("0.5.0").ToJson());

        Run run = Execute("yama-listesi", tree.PathOf("old.json"), tree.PathOf("new.json"), tree.PathOf("patch.iss"));

        Assert.Equal(ReleaseCommand.Failed, run.ExitCode);
        Assert.NotEmpty(run.Error);
        Assert.False(File.Exists(tree.PathOf("patch.iss")));
    }

    [Theory]
    [InlineData("dosya-kaydi")]
    [InlineData("dosya-kaydi", "publish", "beta", "files.json")]
    [InlineData("yama-listesi", "old.json")]
    public void WrongUsage_PrintsHelp(params string[] args)
    {
        Run run = Execute(args);

        Assert.Equal(ReleaseCommand.UsageError, run.ExitCode);
        Assert.Contains("Kullanım", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dosya-kaydi", true)]
    [InlineData("yama-listesi", true)]
    [InlineData("tara", false)]
    public void OnlyItsOwnVerbs_AreHandled(string verb, bool handled)
    {
        Assert.Equal(handled, ReleaseCommand.Handles(verb));
    }

    private static FileManifest Manifest(string version, params (string Path, string Content)[] files) =>
        new(version, files.ToDictionary(file => file.Path, file => new ManifestEntry(new string(file.Content[0], 64), file.Content.Length)));
}
