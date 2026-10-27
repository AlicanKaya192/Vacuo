using Vacuo.Core.Organize;

namespace Vacuo.Core.Tests.Organize;

/// <summary>
/// Every kind and topic folder has its icon in the app's assets (Aşama 3h), at every size Windows asks
/// for, each frame a PNG; <c>tools\make-folder-icons.ps1</c> writes them.
/// </summary>
public class FolderIconAssetTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();
        foreach (string name in Enum.GetNames<FileKind>().Select(kind => new OrganizeTarget(OrganizeRule.ByType, kind))
            .Concat(Enum.GetNames<Topic>().Select(topic => new OrganizeTarget(OrganizeRule.ByTopic, topic)))
            .Select(target => FolderIcons.NameOf(target)!))
        {
            names.Add(name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void IconFile_HasEverySizeAsAPng(string name)
    {
        string path = Path.Combine(RepositoryRoot(), "src", "Vacuo.App", "Assets", "FolderIcons", name + ".ico");
        Assert.True(File.Exists(path), $"{path} is missing; run tools\\make-folder-icons.ps1.");

        byte[] icon = File.ReadAllBytes(path);
        Assert.Equal(1, BitConverter.ToUInt16(icon, 2)); // an icon, not a cursor
        int count = BitConverter.ToUInt16(icon, 4);
        var sizes = new List<int>();
        for (int index = 0; index < count; index++)
        {
            int entry = 6 + index * 16;
            sizes.Add(icon[entry] == 0 ? 256 : icon[entry]);
            int offset = BitConverter.ToInt32(icon, entry + 12);
            Assert.Equal(PngSignature, icon.Skip(offset).Take(PngSignature.Length));
        }

        Assert.Equal(Sizes, sizes);
    }

    private static string RepositoryRoot()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Vacuo.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("The repository root was not found.");
        }

        return root;
    }
}
