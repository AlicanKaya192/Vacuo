using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Vacuo.App.Localization;

/// <summary>
/// Reads UI strings from Strings/&lt;language&gt;/Resources.resw. Without an override,
/// Windows picks the language from the user's language list.
/// </summary>
internal sealed class Localizer
{
    private const string ResourceSubtree = "Resources";

    private readonly ResourceMap _strings;
    private readonly ResourceContext _context;

    public Localizer(string? languageOverride)
    {
        var manager = new ResourceManager();
        _context = manager.CreateResourceContext();
        if (languageOverride is not null)
        {
            _context.QualifierValues["Language"] = languageOverride;
        }

        _strings = manager.MainResourceMap.GetSubtree(ResourceSubtree);
        Culture = languageOverride is null
            ? CultureInfo.CurrentUICulture
            : CultureInfo.GetCultureInfo(languageOverride);
    }

    public CultureInfo Culture { get; }

    public string Get(string key) => _strings.GetValue(key, _context).ValueAsString;

    public string Format(string key, params object[] values) =>
        string.Format(Culture, Get(key), values);
}
