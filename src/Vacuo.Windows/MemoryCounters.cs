using System.ComponentModel;
using System.Runtime.InteropServices;
using Vacuo.Core.Ram;

namespace Vacuo.Windows;

/// <summary>
/// Reads the machine's memory without administrator rights: commit and RAM from
/// GetPerformanceInfo, disk paging from the "Page Reads/sec" counter. The English counter name
/// is used, so it works on a Turkish Windows too. Only reads.
/// </summary>
public static class MemoryCounters
{
    private const string PageReadsCounter = @"\Memory\Page Reads/sec";
    private const uint PdhFormatDouble = 0x00000200;

    /// <summary>Takes one reading; paging is averaged over <paramref name="pagingWindow"/>.</summary>
    public static MemorySnapshot Read(TimeSpan pagingWindow)
    {
        double pageReads = PageReadsPerSecond(pagingWindow);
        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(ref info, info.Size))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetPerformanceInfo failed.");
        }

        long page = (long)info.PageSize;
        return new MemorySnapshot(
            TotalPhysicalBytes: (long)info.PhysicalTotal * page,
            AvailablePhysicalBytes: (long)info.PhysicalAvailable * page,
            CommittedBytes: (long)info.CommitTotal * page,
            CommitLimitBytes: (long)info.CommitLimit * page,
            PageReadsPerSecond: pageReads);
    }

    // A rate counter needs two samples; the value is the average between them.
    private static double PageReadsPerSecond(TimeSpan window)
    {
        Check(PdhOpenQuery(null, 0, out nint query), "PdhOpenQuery");
        try
        {
            Check(PdhAddEnglishCounter(query, PageReadsCounter, 0, out nint counter), "PdhAddEnglishCounter");
            Check(PdhCollectQueryData(query), "PdhCollectQueryData");
            Thread.Sleep(window);
            Check(PdhCollectQueryData(query), "PdhCollectQueryData");
            Check(PdhGetFormattedCounterValue(counter, PdhFormatDouble, out _, out PdhCounterValue value), "PdhGetFormattedCounterValue");
            return value.DoubleValue;
        }
        finally
        {
            _ = PdhCloseQuery(query);
        }
    }

    private static void Check(uint status, string call)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{call} failed with 0x{status:X8}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PdhCounterValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);
}
