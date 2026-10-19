namespace Vacuo.Core.Scanning;

/// <summary>Finds candidates for one bucket. Scanning only reads; it never changes a file.</summary>
public interface IScanner
{
    ScanResult Scan();
}
