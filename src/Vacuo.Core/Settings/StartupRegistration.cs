namespace Vacuo.Core.Settings;

/// <summary>The current user's Run key: the programs Windows starts at sign-in.</summary>
public interface IRunValues
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>
/// Starting Vacuo's tray with Windows (vacuo-kurallar 5): only Vacuo's own value in the current
/// user's Run key, no administrator rights. Other programs' values are never touched.
/// </summary>
public sealed class StartupRegistration(IRunValues values, string trayPath)
{
    public const string ValueName = "Vacuo";

    // A value left by another copy (an older install folder) still means "on"; turning on points it here.
    public bool IsOn => values.Get(ValueName) is not null;

    public void TurnOn() => values.Set(ValueName, $"\"{trayPath}\"");

    public void TurnOff()
    {
        if (IsOn)
        {
            values.Delete(ValueName);
        }
    }
}
