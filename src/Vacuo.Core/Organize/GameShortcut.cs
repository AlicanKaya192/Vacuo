namespace Vacuo.Core.Organize;

/// <summary>Reads where a .lnk shortcut points; <see langword="null"/> when it cannot be told.</summary>
public interface IShortcutReader
{
    string? TargetOf(string shortcut);
}

/// <summary>Tells a game's shortcut from any other (Aşama 3h): only these may move; launchers themselves stay.</summary>
public static class GameShortcut
{
    private const long LongestUrlFile = 64 * 1024;
    private const string UrlLine = "URL=";

    private static readonly string[] GameUrls = ["steam://rungameid/", "com.epicgames.launcher://apps/"];
    private static readonly string[] GameFolders = [@"\steamapps\common\", @"\Epic Games\", @"\XboxGames\", @"\Riot Games\", @"\GOG Galaxy\Games\"];
    private static readonly string[] LauncherFolders = [@"\Epic Games\Launcher\", @"\Riot Games\Riot Client\"];

    public static bool IsShortcut(string name) =>
        Path.GetExtension(name).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || IsUrlFile(name);

    public static bool IsGameUrl(string urlFileText) =>
        urlFileText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(UrlLine, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[UrlLine.Length..].Trim())
            .Any(url => GameUrls.Any(game => url.StartsWith(game, StringComparison.OrdinalIgnoreCase)));

    public static bool IsGameTarget(string? target) =>
        !string.IsNullOrEmpty(target)
        && GameFolders.Any(folder => target.Contains(folder, StringComparison.OrdinalIgnoreCase))
        && !LauncherFolders.Any(folder => target.Contains(folder, StringComparison.OrdinalIgnoreCase));

    /// <summary>False also when the shortcut cannot be read: what is not known to be a game stays where it is.</summary>
    public static bool IsGame(string path, IShortcutReader reader)
    {
        if (!IsShortcut(path))
        {
            return false;
        }

        return IsUrlFile(path) ? IsGameUrlFile(path) : IsGameTarget(reader.TargetOf(path));
    }

    private static bool IsUrlFile(string name) => Path.GetExtension(name).Equals(".url", StringComparison.OrdinalIgnoreCase);

    private static bool IsGameUrlFile(string path)
    {
        try
        {
            return new FileInfo(path).Length <= LongestUrlFile && IsGameUrl(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
