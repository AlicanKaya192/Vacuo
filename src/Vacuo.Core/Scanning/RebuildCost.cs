namespace Vacuo.Core.Scanning;

/// <summary>What it takes to get a removed item back.</summary>
public enum RebuildCost
{
    /// <summary>The tool recreates it on its next run, e.g. Python bytecode.</summary>
    RegeneratedAutomatically,
    /// <summary>A command such as <c>npm install</c> or <c>cargo build</c> rebuilds it.</summary>
    ReinstalledByCommand,
    /// <summary>The tool downloads it again; the item's size is the download.</summary>
    Redownloaded,
    /// <summary>The user's own file: once removed, only the Recycle Bin can bring it back.</summary>
    CannotBeRebuilt,
}
