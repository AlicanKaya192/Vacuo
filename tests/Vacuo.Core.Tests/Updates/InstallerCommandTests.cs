using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class InstallerCommandTests
{
    [Fact]
    public void Arguments_RunSilentlyAndNameBothProcessesToWaitFor()
    {
        string arguments = InstallerCommand.Arguments(panelProcessId: 4120, trayProcessId: 980);

        // The names match installer/vacuo.iss: {param:OLDPID} and {param:OLDTRAYPID}.
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /OLDPID=4120 /OLDTRAYPID=980", arguments);
    }

    [Fact]
    public void NoTray_LeavesItsParameterOut()
    {
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /OLDPID=4120", InstallerCommand.Arguments(4120, trayProcessId: null));
    }
}
