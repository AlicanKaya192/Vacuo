using Vacuo.Core.Formatting;

namespace Vacuo.Core.Tests;

public class PathNamesTests
{
    [Theory]
    [InlineData(@"C:\Users\Someone\Downloads", @"Someone\Downloads")]
    [InlineData(@"C:\Program Files", "Program Files")]
    public void Short_KeepsTheFolderAndItsParent(string path, string name)
    {
        Assert.Equal(name, PathNames.Short(path));
    }
}
