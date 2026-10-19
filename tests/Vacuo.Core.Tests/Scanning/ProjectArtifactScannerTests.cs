using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class ProjectArtifactScannerTests
{
    private static IReadOnlyList<ScanFinding> ScanProjects(TempTree tree) =>
        new ProjectArtifactScanner([tree.PathOf("projects")]).Scan().Findings;

    [Fact]
    public void Scan_NodeModulesNextToPackageJson_IsFoundWithItsSize()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\web\package.json", 20);
        tree.AddFile(@"projects\web\node_modules\left-pad\index.js", 600);
        tree.AddFile(@"projects\web\node_modules\left-pad\package.json", 80);

        ScanFinding finding = Assert.Single(ScanProjects(tree));

        Assert.Equal("node-modules", finding.Rule.Id);
        Assert.Equal(Bucket.RebuildsItself, finding.Rule.Bucket);
        Assert.Equal(RebuildCost.ReinstalledByCommand, finding.Rule.Cost);
        Assert.Equal(tree.PathOf(@"projects\web\node_modules"), finding.Path);
        Assert.Equal(680, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_NodeModulesWithoutPackageJson_IsIgnored()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\notes\node_modules\thesis.docx", 600);

        Assert.Empty(ScanProjects(tree));
    }

    [Fact]
    public void Scan_NestedNodeModules_ReportsOnlyTheOutermost()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\web\package.json", 20);
        tree.AddFile(@"projects\web\node_modules\a\package.json", 10);
        tree.AddFile(@"projects\web\node_modules\a\node_modules\b\index.js", 30);

        ScanFinding finding = Assert.Single(ScanProjects(tree));

        Assert.Equal(40, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_VirtualEnvironmentWithPyvenvCfg_IsFoundWhateverItsName()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\ml\env\pyvenv.cfg", 5);
        tree.AddFile(@"projects\ml\env\Lib\site-packages\numpy\core.pyd", 995);

        ScanFinding finding = Assert.Single(ScanProjects(tree));

        Assert.Equal("python-venv", finding.Rule.Id);
        Assert.Equal(1000, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_DotVenvWithoutPyvenvCfg_IsIgnored()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\ml\.venv\my-notes.txt", 50);

        Assert.Empty(ScanProjects(tree));
    }

    [Fact]
    public void Scan_PycacheHoldingOnlyBytecode_IsFound()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\ml\train.py", 10);
        tree.AddFile(@"projects\ml\__pycache__\train.cpython-312.pyc", 70);

        ScanFinding finding = Assert.Single(ScanProjects(tree));

        Assert.Equal("python-bytecode", finding.Rule.Id);
        Assert.Equal(RebuildCost.RegeneratedAutomatically, finding.Rule.Cost);
    }

    [Fact]
    public void Scan_PycacheHoldingOtherFiles_IsIgnored()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\ml\__pycache__\train.cpython-312.pyc", 70);
        tree.AddFile(@"projects\ml\__pycache__\results.csv", 70);

        Assert.Empty(ScanProjects(tree));
    }

    [Fact]
    public void Scan_JupyterCheckpointsNextToNotebook_IsFound()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\ml\analysis.ipynb", 10);
        tree.AddFile(@"projects\ml\.ipynb_checkpoints\analysis-checkpoint.ipynb", 90);

        ScanFinding finding = Assert.Single(ScanProjects(tree));

        Assert.Equal("jupyter-checkpoints", finding.Rule.Id);
    }

    [Fact]
    public void Scan_RustTargetWithCargoTomlAndCacheDirTag_IsFound()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\tool\Cargo.toml", 10);
        tree.AddFile(@"projects\tool\target\CACHEDIR.TAG", 5);
        tree.AddFile(@"projects\tool\target\debug\tool.exe", 495);

        ScanFinding finding = Assert.Single(ScanProjects(tree));

        Assert.Equal("rust-target", finding.Rule.Id);
        Assert.Equal(500, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_TargetWithoutCacheDirTag_IsIgnored()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\tool\Cargo.toml", 10);
        tree.AddFile(@"projects\tool\target\release-notes.md", 5);

        Assert.Empty(ScanProjects(tree));
    }

    [Theory]
    [InlineData(@".vscode\extensions\ms-python.python")]
    [InlineData(@"AppData\Local\Programs\Editor\resources\app")]
    [InlineData(@"anaconda3\share\jupyter\lab\staging")]
    [InlineData(@"tool\.git\hooks")]
    public void Scan_NodeModulesInsideInstalledSoftware_IsIgnored(string installFolder)
    {
        using var tree = new TempTree();
        tree.AddFile($@"projects\{installFolder}\package.json", 20);
        tree.AddFile($@"projects\{installFolder}\node_modules\lib\index.js", 600);

        Assert.Empty(ScanProjects(tree));
    }

    [Fact]
    public void Scan_DocumentsOnly_FindsNothing()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\thesis\chapter1.docx", 400);
        tree.AddFile(@"projects\thesis\cache\figure.png", 400);
        tree.AddFile(@"projects\thesis\target\goals.xlsx", 400);

        Assert.Empty(ScanProjects(tree));
    }

    [Fact]
    public void Scan_JunctionToAnotherProject_IsNotFollowed()
    {
        using var tree = new TempTree();
        tree.AddFile(@"elsewhere\web\package.json", 20);
        tree.AddFile(@"elsewhere\web\node_modules\lib\index.js", 600);
        tree.AddDirectory("projects");
        tree.AddJunction(@"projects\linked", "elsewhere");

        Assert.Empty(ScanProjects(tree));
    }

    [Fact]
    public void Scan_SeveralProjects_FindsEachArtifact()
    {
        using var tree = new TempTree();
        tree.AddFile(@"projects\web\package.json", 20);
        tree.AddFile(@"projects\web\node_modules\lib\index.js", 600);
        tree.AddFile(@"projects\ml\.venv\pyvenv.cfg", 5);

        string[] ruleIds = ScanProjects(tree).Select(finding => finding.Rule.Id).Order().ToArray();

        Assert.Equal(["node-modules", "python-venv"], ruleIds);
    }

    [Fact]
    public void Constructor_WindowsFolderAsRoot_Throws()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.Throws<ArgumentException>(() => new ProjectArtifactScanner([windows]));
    }
}
