using Vacuo.App.Localization;
using Vacuo.Core.Disk;

namespace Vacuo.App.Screens;

/// <summary>The line beside a removal's progress bar: how many are done and how far, in the user's language.</summary>
internal static class RemovalTexts
{
    private const int Percent = 100;

    public static string Progress(Localizer localizer, MoveProgress report) =>
        localizer.Format("RemovalProgressFormat", report.Done, report.Total, (int)Math.Round(report.Fraction * Percent));
}
