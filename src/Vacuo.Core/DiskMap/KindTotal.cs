using Vacuo.Core.Organize;

namespace Vacuo.Core.DiskMap;

/// <summary>How much of the scanned drive one type of file takes; a null kind is everything else.</summary>
public sealed record KindTotal(FileKind? Kind, long Bytes);
