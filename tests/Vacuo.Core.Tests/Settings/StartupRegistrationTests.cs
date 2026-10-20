using Vacuo.Core.Settings;

namespace Vacuo.Core.Tests.Settings;

public class StartupRegistrationTests
{
    private const string TrayPath = @"C:\Program Files\Vacuo\Vacuo.Tray.exe";

    [Fact]
    public void NoValue_IsOff()
    {
        var values = new FakeRunValues();

        Assert.False(new StartupRegistration(values, TrayPath).IsOn);
    }

    [Fact]
    public void TurnOn_WritesTheQuotedTrayPath()
    {
        var values = new FakeRunValues();
        var startup = new StartupRegistration(values, TrayPath);

        startup.TurnOn();

        Assert.True(startup.IsOn);
        Assert.Equal($"\"{TrayPath}\"", values.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void ValueFromAnotherCopy_CountsAsOn_AndTurnOnPointsItHere()
    {
        var values = new FakeRunValues();
        values.Values[StartupRegistration.ValueName] = "\"D:\\Old\\Vacuo.Tray.exe\"";
        var startup = new StartupRegistration(values, TrayPath);

        Assert.True(startup.IsOn);
        startup.TurnOn();
        Assert.Equal($"\"{TrayPath}\"", values.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void TurnOff_RemovesOnlyVacuosValue()
    {
        var values = new FakeRunValues();
        values.Values["OneDrive"] = "\"C:\\OneDrive.exe\" /background";
        var startup = new StartupRegistration(values, TrayPath);
        startup.TurnOn();

        startup.TurnOff();

        Assert.False(startup.IsOn);
        Assert.Equal(["OneDrive"], values.Values.Keys);
    }

    [Fact]
    public void TurnOff_WhenAlreadyOff_DeletesNothing()
    {
        var values = new FakeRunValues();

        new StartupRegistration(values, TrayPath).TurnOff();

        Assert.Equal(0, values.Deletes);
    }

    private sealed class FakeRunValues : IRunValues
    {
        public Dictionary<string, string> Values { get; } = [];

        public int Deletes { get; private set; }

        public string? Get(string name) => Values.GetValueOrDefault(name);

        public void Set(string name, string value) => Values[name] = value;

        public void Delete(string name)
        {
            Deletes++;
            Values.Remove(name);
        }
    }
}
