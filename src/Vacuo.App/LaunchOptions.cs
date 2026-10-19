namespace Vacuo.App;

/// <summary>
/// Command-line switches used during Stage 0 measurement:
/// <c>--lang en-US</c>, <c>--measure &lt;file.json&gt;</c>, <c>--open</c>,
/// <c>--script idle|coldstart|full</c>, <c>--reduced-motion</c>.
/// </summary>
internal sealed record LaunchOptions(
    string? Language,
    string? MeasurementPath,
    bool OpenPanelAtStart,
    string? Script,
    bool ForceReducedMotion)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? language = null;
        string? measurementPath = null;
        bool openPanel = false;
        string? script = null;
        bool reducedMotion = false;

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
                case "--script" when i + 1 < args.Count:
                    script = args[++i];
                    break;
                case "--reduced-motion":
                    reducedMotion = true;
                    break;
            }
        }

        return new LaunchOptions(language, measurementPath, openPanel, script, reducedMotion);
    }
}
