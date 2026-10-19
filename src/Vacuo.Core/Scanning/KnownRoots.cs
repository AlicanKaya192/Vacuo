namespace Vacuo.Core.Scanning;

public enum KnownRoot
{
    LocalAppData,
    UserProfile,
    Pictures,
}

/// <summary>
/// Where the user folders that known caches and user files live under are; tests point them at
/// a temp tree. <paramref name="Pictures"/> may be redirected (OneDrive); when not given, it is
/// the default folder under the user profile.
/// </summary>
public sealed record KnownRoots(string LocalAppData, string UserProfile, string? Pictures = null)
{
    public static KnownRoots ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    public string PathOf(KnownRoot root) => root switch
    {
        KnownRoot.LocalAppData => LocalAppData,
        KnownRoot.UserProfile => UserProfile,
        KnownRoot.Pictures => Pictures ?? Path.Combine(UserProfile, "Pictures"),
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, null),
    };
}
