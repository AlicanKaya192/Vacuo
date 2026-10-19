using System.Diagnostics;
using Vacuo.Core.Scanning;

namespace Vacuo.Cli;

internal sealed record ScanContext(KnownRoots KnownRoots, string DefaultProjectRoot);

/// <summary>
/// <c>vacuo tara</c>: scans and prints what could be cleaned. It only reads; it never moves
/// or deletes anything, so it calls no action code at all.
/// </summary>
internal static class ScanCommand
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int RootRefused = 2;

    private const string ScanVerb = "tara";
    private const string RootOption = "--kok";
    private const string Usage = """
        Kullanım: vacuo tara [--kok <klasör>]...
          Bilinen önbellekleri ve proje kalıntılarını tarar, kovaları ve toplamları yazar.
          Hiçbir dosyayı taşımaz ya da silmez.
          --kok <klasör>  Projeler yalnızca bu klasörde aranır (birden çok verilebilir).
                          Verilmezse kullanıcı klasörü aranır.
        """;

    public static int Run(IReadOnlyList<string> args, ScanContext context, TextWriter output, TextWriter error)
    {
        if (!TryReadProjectRoots(args, out List<string> projectRoots))
        {
            error.WriteLine(Usage);
            return UsageError;
        }

        if (projectRoots.Count == 0)
        {
            projectRoots.Add(context.DefaultProjectRoot);
        }

        ProjectArtifactScanner projectScanner;
        try
        {
            projectScanner = new ProjectArtifactScanner(projectRoots);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            return RootRefused;
        }

        var stopwatch = Stopwatch.StartNew();
        ScanSummary summary = ScanSummary.Combine([new KnownLocationScanner(context.KnownRoots).Scan(), projectScanner.Scan()]);
        ScanReport.Write(summary, output);
        output.WriteLine(ScanReport.Duration(stopwatch.Elapsed));
        return Success;
    }

    private static bool TryReadProjectRoots(IReadOnlyList<string> args, out List<string> projectRoots)
    {
        projectRoots = [];
        if (args.Count == 0 || args[0] != ScanVerb)
        {
            return false;
        }

        for (int index = 1; index < args.Count; index++)
        {
            if (args[index] != RootOption || index + 1 >= args.Count)
            {
                return false;
            }

            projectRoots.Add(args[++index]);
        }

        return true;
    }
}
