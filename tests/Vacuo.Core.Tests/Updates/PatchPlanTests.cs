using System.Security.Cryptography;
using System.Text;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class PatchPlanTests
{
    [Fact]
    public void Manifest_ListsEveryFileWithItsDigestAndSize()
    {
        using var tree = new TempTree();
        File.WriteAllText(tree.PathOf("Vacuo.exe"), "MZ program");
        tree.AddDirectory("zh-CN");
        File.WriteAllText(tree.PathOf(@"zh-CN\Strings.mui"), "strings");

        FileManifest manifest = FileManifest.Of("0.4.0", tree.Root);

        Assert.Equal("0.4.0", manifest.Version);
        Assert.Equal(["Vacuo.exe", "zh-CN/Strings.mui"], manifest.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new ManifestEntry(Digest("MZ program"), 10), manifest.Files["Vacuo.exe"]);
    }

    [Fact]
    public void Manifest_ComesBackTheSameFromJson()
    {
        var manifest = new FileManifest("0.4.0", new Dictionary<string, ManifestEntry>
        {
            ["Vacuo.exe"] = new(new string('a', 64), 120),
            ["zh-CN/Strings.mui"] = new(new string('b', 64), 7),
        });

        FileManifest read = FileManifest.Parse(manifest.ToJson());

        Assert.Equal("0.4.0", read.Version);
        Assert.Equal(manifest.Files, read.Files);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "Version": "0.4.0" }""")]
    [InlineData("""{ "Version": "x", "Files": {} }""")]
    public void DamagedManifest_IsRefused(string json)
    {
        Assert.Throws<FormatException>(() => FileManifest.Parse(json));
    }

    [Fact]
    public void Patch_WritesChangedAndNewFiles_AndRemovesDroppedOnes()
    {
        FileManifest old = Manifest("0.4.0", ("Vacuo.exe", "a"), ("Same.dll", "s"), ("Dropped.dll", "d"));
        FileManifest current = Manifest("0.5.0", ("Vacuo.exe", "b"), ("Same.dll", "s"), ("zh-CN/New.mui", "n"));

        PatchPlan plan = PatchPlan.Between(old, current);

        Assert.Equal(["Vacuo.exe", "zh-CN/New.mui"], plan.Write);
        Assert.Equal(["Dropped.dll"], plan.Remove);
    }

    [Fact]
    public void SameSizeButOtherContent_IsWritten()
    {
        FileManifest old = Manifest("0.4.0", ("Vacuo.dll", "aaaa"));
        FileManifest current = Manifest("0.5.0", ("Vacuo.dll", "bbbb"));

        Assert.Equal(["Vacuo.dll"], PatchPlan.Between(old, current).Write);
    }

    [Fact]
    public void InnoSections_RemoveThenCopyIntoTheSameFolders()
    {
        var plan = new PatchPlan(Write: ["Vacuo.exe", "zh-CN/New.mui"], Remove: ["Dropped.dll"]);

        Assert.Equal(
            "[InstallDelete]\r\n"
            + "Type: files; Name: \"{app}\\Dropped.dll\"\r\n"
            + "\r\n"
            + "[Files]\r\n"
            + "Source: \"{#SourceDir}\\Vacuo.exe\"; DestDir: \"{app}\"; Flags: ignoreversion\r\n"
            + "Source: \"{#SourceDir}\\zh-CN\\New.mui\"; DestDir: \"{app}\\zh-CN\"; Flags: ignoreversion\r\n",
            plan.ToInnoSections());
    }

    [Fact]
    public void InnoSections_KeepABraceInANameAsText()
    {
        var plan = new PatchPlan(Write: ["{x}.dll"], Remove: []);

        Assert.Contains("Source: \"{#SourceDir}\\{{x}.dll\"", plan.ToInnoSections(), StringComparison.Ordinal);
    }

    private static FileManifest Manifest(string version, params (string Path, string Content)[] files) =>
        new(version, files.ToDictionary(file => file.Path, file => new ManifestEntry(Digest(file.Content), file.Content.Length)));

    private static string Digest(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
