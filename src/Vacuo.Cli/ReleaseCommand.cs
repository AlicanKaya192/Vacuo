using System.Globalization;
using System.Text;
using Vacuo.Core.Formatting;
using Vacuo.Core.Updates;

namespace Vacuo.Cli;

/// <summary>
/// Release tooling for tools/build-installer.ps1 (K4): <c>vacuo dosya-kaydi</c> writes a published folder's
/// file list, <c>vacuo yama-listesi</c> compares two lists and writes the Inno Setup sections of a patch
/// setup. It reads only the files it is given and writes only the one output file.
/// </summary>
internal static class ReleaseCommand
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int Failed = 2;

    private const string ManifestVerb = "dosya-kaydi";
    private const string PatchVerb = "yama-listesi";
    private const string Usage = """
        Kullanım: vacuo dosya-kaydi <yayın klasörü> <sürüm> <çıktı.json>
          Klasördeki her dosyanın SHA-256 özetini ve boyutunu yazar.
        Kullanım: vacuo yama-listesi <eski.json> <yeni.json> <çıktı.iss>
          İki kaydı karşılaştırır; yama kurulumunun yazacağı ve sileceği dosyaları Inno Setup bölümleri olarak yazar.
        """;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static bool Handles(string verb) => verb is ManifestVerb or PatchVerb;

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        try
        {
            switch (args)
            {
                case [ManifestVerb, string folder, string version, string target] when ReleaseVersion.TryParse(version, out _):
                    WriteManifest(folder, version, target, output);
                    return Success;
                case [PatchVerb, string oldList, string newList, string target]:
                    WritePatchList(oldList, newList, target, output);
                    return Success;
                default:
                    error.WriteLine(Usage);
                    return UsageError;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            error.WriteLine(exception.Message);
            return Failed;
        }
    }

    private static void WriteManifest(string folder, string version, string target, TextWriter output)
    {
        FileManifest manifest = FileManifest.Of(version, folder);
        File.WriteAllText(target, manifest.ToJson());
        output.WriteLine($"{manifest.Files.Count} dosya: {target}");
    }

    // Both lists are read before anything is written, so a damaged one leaves no half-made patch list.
    private static void WritePatchList(string oldList, string newList, string target, TextWriter output)
    {
        FileManifest old = FileManifest.Parse(File.ReadAllText(oldList));
        FileManifest current = FileManifest.Parse(File.ReadAllText(newList));
        PatchPlan plan = PatchPlan.Between(old, current);
        // Inno Setup reads a file with a byte order mark as UTF-8.
        File.WriteAllText(target, plan.ToInnoSections(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        long bytes = plan.Write.Sum(path => current.Files[path].Size);
        output.WriteLine(
            $"{old.Version} → {current.Version}: {plan.Write.Count} dosya yazılacak ({ByteSizeFormatter.Format(bytes, Turkish)}), "
            + $"{plan.Remove.Count} dosya silinecek.");
    }
}
