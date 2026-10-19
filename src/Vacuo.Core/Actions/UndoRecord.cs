using System.Text.Json.Serialization;

namespace Vacuo.Core.Actions;

/// <summary>
/// What an action did, in enough detail to reverse it. Every action returns one, so an
/// action that cannot be undone cannot be written (development plan, "Mimari").
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MovedItem), "moved")]
[JsonDerivedType(typeof(RecycledItem), "recycled")]
public abstract record UndoRecord(string OriginalPath);

public sealed record MovedItem(string OriginalPath, string MovedTo) : UndoRecord(OriginalPath);

/// <summary>The Recycle Bin knows its items by original path and deletion time.</summary>
public sealed record RecycledItem(string OriginalPath, DateTimeOffset DeletedAt) : UndoRecord(OriginalPath);
