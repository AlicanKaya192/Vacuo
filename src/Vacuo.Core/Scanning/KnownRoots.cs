namespace Vacuo.Core.Scanning;

public enum KnownRoot
{
    LocalAppData,
    UserProfile,
}

/// <summary>Where the user folders that known caches live under are; tests point them at a temp tree.</summary>
public sealed record KnownRoots(string LocalAppData, string UserProfile)
{
    public static KnownRoots ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string PathOf(KnownRoot root) => root switch
    {
        KnownRoot.LocalAppData => LocalAppData,
        KnownRoot.UserProfile => UserProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, null),
    };
}
