using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace Avalonia.Telemetry;

// The class of machine the build runs on: how many processors it has and how
// much memory. Only the two numbers leave, and memory only as a module size.
internal static class HardwareHelper
{
    // Logical processors this process may use: on .NET Core 3 and later, a
    // container's CPU limit rather than the host's count.
    public static int ProcessorCount => Environment.ProcessorCount;

    // Memory is sold in these sizes: powers of two, the half-steps between
    // them, and Apple's 18 and 36. A kernel reports a little less than is
    // installed (what it keeps for itself), so the nearest size is the
    // installed one, and a machine's number never changes between builds.
    private static readonly int[] MemorySizes =
        { 1, 2, 3, 4, 6, 8, 12, 16, 18, 24, 32, 36, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048 };

    // Installed memory in gigabytes, as the nearest standard size; null when
    // it could not be read.
    public static int? MemoryGb()
    {
        try
        {
            var bytes = ReadMemoryBytes();
            return bytes is > 0 ? NearestSizeGb(bytes.Value) : null;
        }
        catch
        {
            return null;
        }
    }

    // The standard size nearest to what the kernel reports.
    public static int NearestSizeGb(long bytes)
    {
        var gb = bytes / (double)(1L << 30);
        if (gb > MemorySizes[MemorySizes.Length - 1])
            return (int)Math.Round(gb / 1024) * 1024;

        return MemorySizes.OrderBy(size => Math.Abs(size - gb)).First();
    }

    private static long? ReadMemoryBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ReadWindowsMemory();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacUtil.ReadInt64("hw.memsize");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ReadLinuxMemory();
        return null;
    }

    private static unsafe long? ReadWindowsMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        return PInvoke.GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : null;
    }

    private static readonly char[] FieldSeparators = [' ', '\t'];

    // "MemTotal:       16318412 kB"
    private static long? ReadLinuxMemory()
    {
        var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
        if (line == null)
            return null;

        var fields = line.Split(FieldSeparators, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && long.TryParse(fields[1], out var kb) ? kb * 1024 : null;
    }
}
