namespace Vacuo.Core.Formatting;

public static class PathNames
{
    // "tools\__pycache__" says more than "__pycache__" alone, and still fits on a small label.
    public static string Short(string path) =>
        Path.Join(Path.GetFileName(Path.GetDirectoryName(path)), Path.GetFileName(path));
}
