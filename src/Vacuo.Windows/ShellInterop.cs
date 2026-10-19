using System.Runtime.InteropServices;

namespace Vacuo.Windows;

// Declarations follow ShObjIdl_core.h, ShellAPI.h, ShlGuid.h and KnownFolders.h of the
// Windows SDK 10.0.26100.0. COM methods must stay in the header's vtable order.

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint bindContext, [In] ref Guid handlerId, [In] ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object handler);

    IShellItem GetParent();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetDisplayName(uint nameForm);

    uint GetAttributes(uint mask);

    int Compare(IShellItem other, uint hint);
}

[ComImport]
[Guid("70629033-e363-4a28-a567-0db78006e6d7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig]
    int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item, out uint fetched);

    void Skip(uint count);

    void Reset();

    IEnumShellItems Clone();
}

[ComImport]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IShellItem? newlyCreated);
    [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IShellItem? newlyCreated);
    [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IShellItem? newlyCreated);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newlyCreated);
    [PreserveSig] int PreNewItem(uint flags, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostNewItem(uint flags, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint fileAttributes, int result, IShellItem? newItem);
    [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComImport]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    uint Advise(IFileOperationProgressSink sink);
    void Unadvise(uint cookie);
    void SetOperationFlags(uint flags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    void SetProgressDialog(nint progressDialog);
    void SetProperties(nint propertyChanges);
    void SetOwnerWindow(nint ownerWindow);
    void ApplyPropertiesToItem(IShellItem item);
    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? itemSink);
    void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    void MoveItem(IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? itemSink);
    void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destinationFolder);
    void CopyItem(IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? copyName, IFileOperationProgressSink? itemSink);
    void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destinationFolder);
    void DeleteItem(IShellItem item, IFileOperationProgressSink? itemSink);
    void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    void NewItem(IShellItem destinationFolder, uint fileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IFileOperationProgressSink? itemSink);
    void PerformOperations();
    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetAnyOperationsAborted();
}

internal static class ShellInterop
{
    // ShellAPI.h operation flags.
    public const uint Silent = 0x0004;
    public const uint NoConfirmation = 0x0010;
    public const uint AllowUndo = 0x0040;
    public const uint NoErrorUi = 0x0400;
    public const uint WantNukeWarning = 0x4000;
    public const uint RecycleOnDelete = 0x0008_0000;
    public const uint EarlyFailure = 0x0010_0000;

    public const uint FileSystemPath = 0x8005_8000;
    public const int Abort = unchecked((int)0x8000_4004);
    public const int Ok = 0;

    private static readonly Guid FileOperationClass = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid RecycleBinFolder = new("b7534046-3ecb-4c18-be4e-64cd4cb7d6ac");
    private static readonly Guid EnumItemsHandler = new("94f60519-2850-4924-aa5a-d15e84868039");

    public static IFileOperation CreateFileOperation() =>
        (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(FileOperationClass, throwOnError: true)!)!;

    public static IShellItem ItemFromPath(string path)
    {
        Guid shellItem = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, 0, ref shellItem, out IShellItem item);
        return item;
    }

    public static IEnumerable<IShellItem> RecycleBinItems()
    {
        Guid folder = RecycleBinFolder;
        Guid shellItem = typeof(IShellItem).GUID;
        SHGetKnownFolderItem(ref folder, 0, 0, ref shellItem, out IShellItem bin);

        Guid handler = EnumItemsHandler;
        Guid enumerator = typeof(IEnumShellItems).GUID;
        bin.BindToHandler(0, ref handler, ref enumerator, out object items);
        var enumeration = (IEnumShellItems)items;
        while (enumeration.Next(1, out IShellItem? item, out uint fetched) == Ok && fetched == 1 && item is not null)
        {
            yield return item;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path, nint bindContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetKnownFolderItem(
        ref Guid folderId, uint flags, nint token, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
}
