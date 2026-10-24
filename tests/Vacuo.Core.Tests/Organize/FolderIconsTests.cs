using Vacuo.Core.Actions;
using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Organize;

public class FolderIconsTests
{
    private sealed class CountingWriter : IFolderIconWriter
    {
        public int Calls { get; private set; }

        public void Apply(string folder, string iconFile) => Calls++;
    }

    [Theory]
    [InlineData(OrganizeRule.ByType, "Images", "images")]
    [InlineData(OrganizeRule.ByType, "Installers", "installers")]
    [InlineData(OrganizeRule.ByTopic, "Invoices", "invoices")]
    [InlineData(OrganizeRule.ByDate, "2026-10", null)]
    [InlineData(OrganizeRule.BySource, "github.com", null)]
    public void NameOf_GivesKindsAndTopicsAnIconAndMonthsAndWebsitesNone(OrganizeRule rule, string key, string? expected)
    {
        Assert.Equal(expected, FolderIcons.NameOf(new OrganizeTarget(rule, key)));
    }

    [Fact]
    public void Stamp_FolderThatAlreadyHasAFolderIco_IsLeftAlone()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Icons\images.ico", 10);
        string usersOwn = tree.AddFile(@"Pictures\folder.ico", 20);
        var writer = new CountingWriter();

        FolderIcon? icon = new FolderIcons(writer, tree.PathOf("Icons")).Stamp(tree.PathOf("Pictures"), new OrganizeTarget(OrganizeRule.ByType, "Images"));

        Assert.Null(icon);
        Assert.Equal(0, writer.Calls);
        Assert.Equal(20, new FileInfo(usersOwn).Length);
    }

    [Fact]
    public void Stamp_IconMissingFromTheIconFolder_LeavesTheFolderPlain()
    {
        using var tree = new TempTree();
        tree.AddDirectory("Icons");
        tree.AddDirectory("Pictures");
        var writer = new CountingWriter();

        FolderIcon? icon = new FolderIcons(writer, tree.PathOf("Icons")).Stamp(tree.PathOf("Pictures"), new OrganizeTarget(OrganizeRule.ByType, "Images"));

        Assert.Null(icon);
        Assert.Equal(0, writer.Calls);
    }
}
