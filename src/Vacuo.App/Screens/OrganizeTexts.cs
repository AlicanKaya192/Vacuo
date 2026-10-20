using Vacuo.App.Localization;
using Vacuo.Core.Organize;

namespace Vacuo.App.Screens;

/// <summary>Düzen's words in the user's language, including the names of the folders it makes.</summary>
internal static class OrganizeTexts
{
    public static string RuleName(Localizer localizer, OrganizeRule rule) => localizer.Get($"OrganizeRule_{rule}");

    /// <summary>Kinds and topics get a translated name; a month or a website is its own name.</summary>
    public static string FolderName(Localizer localizer, OrganizeTarget target) => target.Rule switch
    {
        OrganizeRule.ByType => localizer.Get($"Organize_Kind_{target.Key}"),
        OrganizeRule.ByTopic => localizer.Get($"Organize_Topic_{target.Key}"),
        _ => target.Key,
    };
}
