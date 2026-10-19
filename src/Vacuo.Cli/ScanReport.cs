using System.Globalization;
using Vacuo.Core.Formatting;
using Vacuo.Core.Scanning;

namespace Vacuo.Cli;

/// <summary>The Turkish text report of <c>vacuo tara</c> (a development tool, not shipped).</summary>
internal static class ScanReport
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static void Write(ScanSummary summary, TextWriter output)
    {
        if (summary.Findings.Count == 0)
        {
            output.WriteLine("Yeniden kurulabilir bir şey bulunmadı.");
        }

        foreach (BucketTotal total in summary.BucketTotals)
        {
            output.WriteLine($"{BucketName(total.Bucket)}: {total.Count} öğe, {Size(total.Bytes)}");
            foreach (ScanFinding finding in summary.Findings.Where(finding => finding.Rule.Bucket == total.Bucket))
            {
                output.WriteLine(Line(finding));
            }

            output.WriteLine();
        }

        output.WriteLine($"Toplam: {Size(summary.TotalBytes)} · okunamayan klasör: {summary.SkippedEntries}");
        output.WriteLine("Hiçbir dosya taşınmadı ya da silinmedi.");
    }

    public static string Duration(TimeSpan elapsed) =>
        string.Create(Turkish, $"Tarama süresi: {elapsed.TotalSeconds:0.0} sn");

    private static string Line(ScanFinding finding) =>
        string.Create(Turkish, $"  {Size(finding.Size.Bytes),10}  {finding.Rule.Id,-22} {CostName(finding.Rule.Cost),-22} {finding.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd}  {finding.Path}");

    private static string Size(long bytes) => ByteSizeFormatter.Format(bytes, Turkish);

    private static string BucketName(Bucket bucket) => bucket switch
    {
        Bucket.RebuildsItself => "Kendini yeniden kurar",
        Bucket.Yours => "Senin",
        Bucket.SameFiles => "Aynı dosyalar",
        Bucket.Keep => "Kalsın",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };

    private static string CostName(RebuildCost cost) => cost switch
    {
        RebuildCost.RegeneratedAutomatically => "kendiliğinden oluşur",
        RebuildCost.ReinstalledByCommand => "komutla kurulur",
        RebuildCost.Redownloaded => "yeniden indirilir",
        RebuildCost.CannotBeRebuilt => "geri getirilemez",
        _ => throw new ArgumentOutOfRangeException(nameof(cost), cost, null),
    };
}
