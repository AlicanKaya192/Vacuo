using System.Text.Json.Serialization;

namespace Vacuo.Core.Actions;

/// <summary>
/// What an action did, in enough detail to reverse it. Every action returns one, so an
/// action that cannot be undone cannot be written (development plan, "Mimari"). The one
/// exception is <see cref="DeletedForGood"/>: an administrator cleanup the user chose (ADR-003).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MovedItem), "moved")]
[JsonDerivedType(typeof(RecycledItem), "recycled")]
[JsonDerivedType(typeof(DeletedForGood), "deleted")]
[JsonDerivedType(typeof(CreatedFolder), "created-folder")]
public abstract record UndoRecord(string OriginalPath);

public sealed record MovedItem(string OriginalPath, string MovedTo) : UndoRecord(OriginalPath);

/// <summary>
/// An item in the Recycle Bin. <paramref name="BinPath"/> is where the bin stores it; it tells
/// this item apart from others deleted from the same place, so restore picks the right one.
/// </summary>
public sealed record RecycledItem(string OriginalPath, DateTimeOffset DeletedAt, string BinPath) : UndoRecord(OriginalPath);

/// <summary>Removed for good by the administrator helper (ADR-003); it is in the journal only so it is not forgotten.</summary>
public sealed record DeletedForGood(string OriginalPath) : UndoRecord(OriginalPath);

/// <summary>A folder Düzen made for its moves; undo removes it only while it is empty (B-161).</summary>
public sealed record CreatedFolder(string OriginalPath) : UndoRecord(OriginalPath);
