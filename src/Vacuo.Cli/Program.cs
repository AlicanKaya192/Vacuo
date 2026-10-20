using System.Text;
using Vacuo.Cli;
using Vacuo.Core.Scanning;

// Without this, redirected output uses the old console code page and Turkish letters break.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// Release tooling for tools/build-installer.ps1; everything else is the scan.
if (args.Length > 0 && ReleaseCommand.Handles(args[0]))
{
    return ReleaseCommand.Run(args, Console.Out, Console.Error);
}

var context = new ScanContext(
    KnownRoots.ForCurrentUser(),
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

return ScanCommand.Run(args, context, Console.Out, Console.Error);
