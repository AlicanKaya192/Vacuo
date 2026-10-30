using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Organize;

public class GameShortcutTests
{
    private sealed class FixedTarget(string? target) : IShortcutReader
    {
        public string? TargetOf(string shortcut) => target;
    }

    [Theory]
    [InlineData("[InternetShortcut]\r\nURL=steam://rungameid/570\r\nIconIndex=0\r\n")]
    [InlineData("[InternetShortcut]\nurl=STEAM://RUNGAMEID/570\n")]
    [InlineData("[InternetShortcut]\r\nURL=com.epicgames.launcher://apps/Fortnite?action=launch\r\n")]
    public void UrlOfAGame_IsAGame(string text) => Assert.True(GameShortcut.IsGameUrl(text));

    [Theory]
    [InlineData("[InternetShortcut]\r\nURL=https://store.steampowered.com/\r\n")]
    [InlineData("[InternetShortcut]\r\nURL=https://example.com/steam://rungameid/570\r\n")]
    [InlineData("[InternetShortcut]\r\nURL=steam://open/library\r\n")]
    [InlineData("Comment=URL=steam://rungameid/570\r\n")]
    [InlineData("")]
    public void AnyOtherUrl_IsNotAGame(string text) => Assert.False(GameShortcut.IsGameUrl(text));

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Dota 2\dota2.exe")]
    [InlineData(@"D:\SteamLibrary\STEAMAPPS\COMMON\Game\game.exe")]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\game.exe")]
    [InlineData(@"C:\XboxGames\Forza\Content\forza.exe")]
    [InlineData(@"C:\Riot Games\VALORANT\live\VALORANT.exe")]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\Games\Witcher\witcher.exe")]
    public void TargetInAGameFolder_IsAGame(string target) => Assert.True(GameShortcut.IsGameTarget(target));

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")]
    [InlineData(@"C:\Riot Games\Riot Client\RiotClientServices.exe")]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe")]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE")]
    [InlineData(@"C:\Users\test\Documents\Epic Games.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherTarget_IsNotAGame(string? target) => Assert.False(GameShortcut.IsGameTarget(target));

    [Fact]
    public void LnkFile_IsToldByWhereItPoints()
    {
        using var tree = new TempTree();
        string shortcut = tree.AddFile("a.LNK", 10);

        Assert.True(GameShortcut.IsGame(shortcut, new FixedTarget(@"C:\XboxGames\Forza\forza.exe")));
        Assert.False(GameShortcut.IsGame(shortcut, new FixedTarget(@"C:\Windows\notepad.exe")));
        Assert.False(GameShortcut.IsGame(shortcut, new FixedTarget(null)));
    }

    [Fact]
    public void UrlFile_IsToldByWhatItOpens()
    {
        using var tree = new TempTree();
        string shortcut = tree.PathOf("Dota.url");
        File.WriteAllText(shortcut, "[InternetShortcut]\r\nURL=steam://rungameid/570\r\n");

        Assert.True(GameShortcut.IsGame(shortcut, new FixedTarget(null)));
    }

    // Nothing a real shortcut holds is this long; such a file is not read at all.
    [Fact]
    public void AVeryLargeUrlFile_IsNotAGame()
    {
        using var tree = new TempTree();
        string shortcut = tree.PathOf("big.url");
        File.WriteAllText(shortcut, "URL=steam://rungameid/570\r\n" + new string(' ', 100_000));

        Assert.False(GameShortcut.IsGame(shortcut, new FixedTarget(null)));
    }

    [Fact]
    public void AnotherFile_IsNotAGame()
    {
        using var tree = new TempTree();
        string file = tree.AddFile("dota2.exe", 10);

        Assert.False(GameShortcut.IsGame(file, new FixedTarget(@"C:\XboxGames\Forza\forza.exe")));
    }

    [Fact]
    public void FolderItems_ReadGameShortcuts_OnlyWhenAskedTo()
    {
        using var tree = new TempTree();
        File.WriteAllText(Path.Join(Directory.CreateDirectory(tree.PathOf("Desktop")).FullName, "Dota.url"), "URL=steam://rungameid/570\r\n");
        tree.AddFile(@"Desktop\Word.lnk", 10);
        tree.AddFile(@"Desktop\a.pdf", 10);
        var reader = new FixedTarget(@"C:\Windows\notepad.exe");

        IReadOnlyList<FolderItem> withGames = FolderItems.Read(tree.PathOf("Desktop"), reader);
        IReadOnlyList<FolderItem> without = FolderItems.Read(tree.PathOf("Desktop"));

        Assert.Equal(["a.pdf", "Dota.url"], withGames.Select(item => item.Name).Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(withGames.Single(item => item.Name == "Dota.url").IsGameShortcut);
        Assert.False(withGames.Single(item => item.Name == "a.pdf").IsGameShortcut);
        Assert.Equal(["a.pdf"], without.Select(item => item.Name));
    }

    [Fact]
    public void AGameShortcut_GoesToGames_ByType()
    {
        var item = new FolderItem("Dota.lnk", DateTimeOffset.UnixEpoch, SourceHost: null, IsGameShortcut: true);

        Assert.Equal(new OrganizeTarget(OrganizeRule.ByType, "Games"), OrganizeClassifier.TargetOf(item, OrganizeRule.ByType));
    }

    // The other rules sort the user's files; a shortcut has no month or website of its own.
    [Theory]
    [InlineData(OrganizeRule.ByDate)]
    [InlineData(OrganizeRule.BySource)]
    [InlineData(OrganizeRule.ByTopic)]
    public void AGameShortcut_StaysPut_UnderOtherRules(OrganizeRule rule)
    {
        var item = new FolderItem("homework game.lnk", DateTimeOffset.UnixEpoch, "example.com", IsGameShortcut: true);

        Assert.Null(OrganizeClassifier.TargetOf(item, rule));
    }

    [Fact]
    public void Games_HaveTheirOwnFolderIcon() =>
        Assert.Equal("games", FolderIcons.NameOf(new OrganizeTarget(OrganizeRule.ByType, "Games")));
}
