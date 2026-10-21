using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Vacuo.Core.Battery;

namespace Vacuo.Windows;

/// <summary>
/// Asks each battery's driver directly (<c>IOCTL_BATTERY_QUERY_INFORMATION</c>), the source
/// <c>powercfg /batteryreport</c> also uses; WMI does not give the design capacity on every machine.
/// Read access only, no administrator rights; names and serial numbers are never asked for.
/// </summary>
public static class BatteryReader
{
    // GUID_DEVCLASS_BATTERY (Batclass.h).
    private static readonly Guid BatteryClass = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x3;
    private const uint OpenExisting = 3;

    // CTL_CODE(FILE_DEVICE_BATTERY, 0x10 / 0x11 / 0x13, METHOD_BUFFERED, FILE_READ_ACCESS).
    private const uint IoctlBatteryQueryTag = 0x294040;
    private const uint IoctlBatteryQueryInformation = 0x294044;
    private const uint IoctlBatteryQueryStatus = 0x29404C;

    private const int BatteryInformationLevel = 0;
    private const uint CapacityRelative = 0x40000000;
    private const uint PowerOnLine = 0x1;
    private const uint PowerCharging = 0x4;
    private const uint UnknownCapacity = 0xFFFFFFFF;
    private const int UnknownRate = int.MinValue;

    /// <summary>Whether Windows itself knows of a system battery; a reading that fails is only worth showing then.</summary>
    public static bool WindowsReportsBattery() =>
        GetSystemPowerStatus(out SystemPowerStatus status) && BatteryKind.WindowsReportsBattery(status.BatteryFlag);

    /// <summary>The laptop batteries that power the machine; empty on a desktop, a UPS included.</summary>
    public static IReadOnlyList<BatteryReading> Read()
    {
        var readings = new List<BatteryReading>();
        Guid batteryClass = BatteryClass;
        nint devices = SetupDiGetClassDevs(ref batteryClass, 0, 0, DigcfPresent | DigcfDeviceInterface);
        if (devices == -1)
        {
            return readings;
        }

        try
        {
            var interfaceData = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(devices, 0, ref batteryClass, index, ref interfaceData); index++)
            {
                if (DevicePath(devices, ref interfaceData) is { } path && ReadOne(path) is { } reading)
                {
                    readings.Add(reading);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devices);
        }

        return readings;
    }

    private static BatteryReading? ReadOne(string devicePath)
    {
        using SafeFileHandle battery = CreateFile(devicePath, GenericRead, FileShareReadWrite, 0, OpenExisting, 0, 0);
        if (battery.IsInvalid)
        {
            return null;
        }

        uint wait = 0;
        if (!DeviceIoControl(battery, IoctlBatteryQueryTag, ref wait, sizeof(uint), out uint tag, sizeof(uint), out _, 0) || tag == 0)
        {
            return null;
        }

        var query = new BatteryQueryInformation { BatteryTag = tag, InformationLevel = BatteryInformationLevel };
        if (!DeviceIoControl(battery, IoctlBatteryQueryInformation, ref query, Marshal.SizeOf<BatteryQueryInformation>(),
                out BatteryInformation information, Marshal.SizeOf<BatteryInformation>(), out _, 0)
            || !BatteryKind.IsLaptopBattery(information.Capabilities))
        {
            return null;
        }

        var waitStatus = new BatteryWaitStatus { BatteryTag = tag };
        bool hasStatus = DeviceIoControl(battery, IoctlBatteryQueryStatus, ref waitStatus, Marshal.SizeOf<BatteryWaitStatus>(),
            out BatteryStatus status, Marshal.SizeOf<BatteryStatus>(), out _, 0);

        return new BatteryReading(
            DesignCapacity: Known(information.DesignedCapacity),
            FullChargeCapacity: Known(information.FullChargedCapacity),
            RemainingCapacity: hasStatus ? Known(status.Capacity) : null,
            // Drivers that do not count cycles report 0; a truly new battery is rarer than that.
            CycleCount: information.CycleCount > 0 ? (int)information.CycleCount : null,
            RateMilliwatts: hasStatus && status.Rate != UnknownRate ? status.Rate : null,
            OnAcPower: hasStatus && (status.PowerState & PowerOnLine) != 0,
            Charging: hasStatus && (status.PowerState & PowerCharging) != 0,
            RelativeUnits: (information.Capabilities & CapacityRelative) != 0);
    }

    private static long? Known(uint capacity) => capacity is 0 or UnknownCapacity ? null : capacity;

    private static string? DevicePath(nint devices, ref DeviceInterfaceData interfaceData)
    {
        SetupDiGetDeviceInterfaceDetail(devices, ref interfaceData, 0, 0, out uint required, 0);
        if (required == 0)
        {
            return null;
        }

        nint detail = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize is 8 on x64 (a DWORD and one WCHAR, padded), 6 on x86.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            return SetupDiGetDeviceInterfaceDetail(devices, ref interfaceData, detail, required, out _, 0)
                ? Marshal.PtrToStringUni(detail + 4)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public int InformationLevel;
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public uint Capabilities;
        public byte Technology;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
        public uint Chemistry;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryWaitStatus
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatus
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, nint enumerator, nint parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        nint devices, nint deviceInfo, ref Guid interfaceClass, uint index, ref DeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint devices, ref DeviceInterfaceData interfaceData, nint detail, uint detailSize, out uint requiredSize, nint deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint devices);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint code, ref uint input, int inputSize, out uint output, int outputSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint code, ref BatteryQueryInformation input, int inputSize,
        out BatteryInformation output, int outputSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint code, ref BatteryWaitStatus input, int inputSize,
        out BatteryStatus output, int outputSize, out int returned, nint overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
