using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Uninstall;

namespace Vacuo.Core.Tests.Uninstall;

public class LeftoverFinderTests
{
    private static InstalledProgram Program(string name, string? publisher = null) =>
        new(name, publisher, Version: null, SizeBytes: null, InstalledOn: null, InstallLocation: null, UninstallCommand: "x.exe");

    private static IReadOnlyList<string> Find(TempTree tree, InstalledProgram removed, params InstalledProgram[] stillInstalled) =>
        LeftoverFinder.Find(removed, [tree.PathOf("Roaming"), tree.PathOf("Local")], stillInstalled)
            .Select(leftover => leftover.Path).ToList();

    [Fact]
    public void AFolderNamedAfterTheProgram_IsSuggested()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Roaming\Foo Player\settings.ini", 40);
        tree.AddFile(@"Local\Other\x.dat", 10);

        LeftoverFolder leftover = LeftoverFinder.Find(Program("Foo Player"), [tree.PathOf("Roaming"), tree.PathOf("Local")], []).Single();

        Assert.Equal(tree.PathOf(@"Roaming\Foo Player"), leftover.Path);
        Assert.Equal(40, leftover.Size.Bytes);
    }

    [Fact]
    public void AFolderUnderThePublisher_IsSuggested_ButNotThePublisherFolder()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Local\Foo Ltd\Foo Player\cache.bin", 10);
        tree.AddFile(@"Local\Foo Ltd\Foo Editor\cache.bin", 10);

        Assert.Equal([tree.PathOf(@"Local\Foo Ltd\Foo Player")], Find(tree, Program("Foo Player", "Foo Ltd")));
    }

    // Names differ in spaces, case and punctuation between installers and their data folders.
    [Fact]
    public void NamesMatchWithoutSpacesCaseOrPunctuation()
    {
        using var tree = new TempTree();
        tree.AddDirectory(@"Roaming\foo-player");

        Assert.Equal([tree.PathOf(@"Roaming\foo-player")], Find(tree, Program("Foo Player")));
    }

    [Fact]
    public void ATrailingVersion_IsIgnored()
    {
        using var tree = new TempTree();
        tree.AddDirectory(@"Roaming\Foo Player");

        Assert.Equal([tree.PathOf(@"Roaming\Foo Player")], Find(tree, Program("Foo Player 6.2.1")));
    }

    [Fact]
    public void AFolderAnInstalledProgramStillUses_IsNotSuggested()
    {
        using var tree = new TempTree();
        tree.AddDirectory(@"Roaming\Foo Player");

        Assert.Empty(Find(tree, Program("Foo Player 5"), Program("Foo Player 6")));
    }

    [Fact]
    public void APartialNameMatch_IsNotSuggested()
    {
        using var tree = new TempTree();
        tree.AddDirectory(@"Roaming\Foo Player Plugins");
        tree.AddDirectory(@"Roaming\Foo");

        Assert.Empty(Find(tree, Program("Foo Player")));
    }

    [Fact]
    public void ALinkFolder_IsNotSuggested()
    {
        using var tree = new TempTree();
        tree.AddDirectory(@"Elsewhere");
        tree.AddDirectory("Roaming");
        tree.AddJunction(@"Roaming\Foo Player", "Elsewhere");

        Assert.Empty(Find(tree, Program("Foo Player")));
    }
}
