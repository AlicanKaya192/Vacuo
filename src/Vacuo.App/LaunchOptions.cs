namespace Vacuo.App;

/// <summary>
/// Command-line switches used during Stage 0 measurement:
/// <c>--lang en-US</c>, <c>--measure &lt;file.json&gt;</c>, <c>--open</c>.
/// </summary>
internal sealed record LaunchOptions(string? Language, string? MeasurementPath, bool OpenPanelAtStart)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? language = null;
        string? measurementPath = null;
        bool openPanel = false;

        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--lang" when i + 1 < args.Count:
                    language = args[++i];
                    break;
                case "--measure" when i + 1 < args.Count:
                    measurementPath = args[++i];
                    break;
                case "--open":
                    openPanel = true;
                    break;
            }
        }

        return new LaunchOptions(language, measurementPath, openPanel);
    }
}
