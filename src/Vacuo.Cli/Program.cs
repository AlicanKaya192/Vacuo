using System.Text;
using Vacuo.Cli;
using Vacuo.Core.Scanning;

// Without this, redirected output uses the old console code page and Turkish letters break.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var context = new ScanContext(
    KnownRoots.ForCurrentUser(),
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

return ScanCommand.Run(args, context, Console.Out, Console.Error);
