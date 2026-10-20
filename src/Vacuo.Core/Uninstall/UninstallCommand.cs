namespace Vacuo.Core.Uninstall;

/// <summary>
/// A program's own uninstaller, split from its <c>UninstallString</c> so it can be started with
/// its own window. Vacuo never adds silent switches: the program asks its own questions.
/// </summary>
public sealed record UninstallCommand(string Program, string Arguments)
{
    /// <summary>Null when nothing usable is there, such as an unclosed quote.</summary>
    public static UninstallCommand? Parse(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            int closing = trimmed.IndexOf('"', 1);
            return closing < 0 ? null : new UninstallCommand(trimmed[1..closing], trimmed[(closing + 1)..].Trim());
        }

        // Unquoted paths may hold spaces ("C:\Program Files\..."): the program ends at ".exe".
        int exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            int end = exe + ".exe".Length;
            return new UninstallCommand(trimmed[..end], trimmed[end..].Trim());
        }

        int space = trimmed.IndexOf(' ');
        return space < 0 ? new UninstallCommand(trimmed, string.Empty) : new UninstallCommand(trimmed[..space], trimmed[(space + 1)..].Trim());
    }
}
