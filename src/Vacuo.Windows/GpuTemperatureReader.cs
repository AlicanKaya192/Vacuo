using System.Runtime.InteropServices;
using Vacuo.Core.Thermals;

namespace Vacuo.Windows;

/// <summary>
/// Asks Windows' display kernel (D3DKMT) for each adapter's performance data, the source Task Manager's
/// GPU temperature comes from. Works for any vendor, needs no administrator rights and no driver of
/// Vacuo's own; read only. An adapter that cannot answer is simply left out.
/// </summary>
public static class GpuTemperatureReader
{
    // KMTQUERYADAPTERINFOTYPE values (d3dkmthk.h).
    private const int AdapterRegistryInfo = 8;
    private const int AdapterPerfData = 62;
    private const int MaxPath = 260;

    /// <summary>Every adapter's name and temperature in tenths of a degree (0 when it has no sensor); never throws.</summary>
    public static IReadOnlyList<AdapterReading> Read()
    {
        try
        {
            return ReadAdapters();
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            // An older Windows without D3DKMTEnumAdapters2: the temperature is simply not reported.
            return [];
        }
    }

    private static List<AdapterReading> ReadAdapters()
    {
        var readings = new List<AdapterReading>();
        var enumeration = new EnumAdapters2();
        if (D3DKMTEnumAdapters2(ref enumeration) != 0 || enumeration.NumAdapters == 0)
        {
            return readings;
        }

        int infoSize = Marshal.SizeOf<AdapterInfo>();
        nint adapters = Marshal.AllocHGlobal(infoSize * (int)enumeration.NumAdapters);
        try
        {
            enumeration.Adapters = adapters;
            if (D3DKMTEnumAdapters2(ref enumeration) != 0)
            {
                return readings;
            }

            for (int i = 0; i < enumeration.NumAdapters; i++)
            {
                AdapterInfo adapter = Marshal.PtrToStructure<AdapterInfo>(adapters + i * infoSize);
                if (Temperature(adapter.Handle) is { } deciCelsius)
                {
                    readings.Add(new AdapterReading(Name(adapter.Handle), deciCelsius));
                }

                var close = new CloseAdapter { Handle = adapter.Handle };
                D3DKMTCloseAdapter(ref close);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adapters);
        }

        return readings;
    }

    private static uint? Temperature(uint adapter)
    {
        PerfData? data = Query<PerfData>(adapter, AdapterPerfData);
        return data?.Temperature;
    }

    private static string Name(uint adapter) =>
        Query<RegistryInfo>(adapter, AdapterRegistryInfo)?.AdapterString?.Trim() is { Length: > 0 } name ? name : "?";

    private static T? Query<T>(uint adapter, int type) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(default(T), buffer, false);
            var query = new QueryAdapterInfo { Adapter = adapter, Type = type, Data = buffer, DataSize = (uint)size };
            return D3DKMTQueryAdapterInfo(ref query) == 0 ? Marshal.PtrToStructure<T>(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterInfo
    {
        public uint Handle;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint NumOfSources;
        public int PrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumAdapters2
    {
        public uint NumAdapters;
        public nint Adapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public nint Data;
        public uint DataSize;
    }

    // D3DKMT_ADAPTER_PERFDATA: Temperature is in tenths of a degree Celsius.
    [StructLayout(LayoutKind.Sequential)]
    private struct PerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOverclocked;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    // D3DKMT_ADAPTERREGISTRYINFO.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RegistryInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string AdapterString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string BiosString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string DacType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string ChipType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Handle;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 enumeration);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter close);
}
