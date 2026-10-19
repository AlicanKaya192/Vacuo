namespace Vacuo.Core.Scanning;

/// <summary>The four disk buckets of the Disk screen (development plan, "Ne yapıyor").</summary>
public enum Bucket
{
    /// <summary>Caches and build output that the owning tool recreates.</summary>
    RebuildsItself,
    /// <summary>The user's own files; only ever suggested.</summary>
    Yours,
    /// <summary>Files with identical content.</summary>
    SameFiles,
    /// <summary>System files; shown, never offered for removal.</summary>
    Keep,
}
