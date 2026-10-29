using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.Telemetry;

internal static class MacUtil
{
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";

    // The names passed to these two are plain ASCII. Best-fit mapping is off
    // so that a character with no ANSI form is never swapped for a look-alike.
    [DllImport("libc", BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int sysctlbyname(string name, byte[]? oldp, ref UIntPtr oldlenp, IntPtr newp, UIntPtr newlen);

    [DllImport(IOKit, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKit)]
    private static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

    [DllImport(IOKit)]
    private static extern int IOObjectRelease(uint service);

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";

    // callerSecuritySession and sessionHasGraphicAccess from AuthSession.h.
    private const uint CallerSecuritySession = 0xFFFFFFFF;
    private const uint SessionHasGraphicAccess = 0x0010;

    [DllImport(Security)]
    private static extern int SessionGetInfo(uint session, out uint sessionId, out uint attributes);

    // A kernel value by name (sysctl(3)). Null when the name does not exist.
    public static byte[]? Read(string name)
    {
        var length = UIntPtr.Zero;
        if (sysctlbyname(name, null, ref length, IntPtr.Zero, UIntPtr.Zero) != 0)
            return null;

        var buffer = new byte[(int)length];
        if (sysctlbyname(name, buffer, ref length, IntPtr.Zero, UIntPtr.Zero) != 0)
            return null;

        return buffer;
    }

    public static int? ReadInt32(string name) =>
        Read(name) is { Length: >= 4 } value ? BitConverter.ToInt32(value, 0) : null;

    public static long? ReadInt64(string name) =>
        Read(name) is { Length: >= 8 } value ? BitConverter.ToInt64(value, 0) : null;

    public static string? ReadString(string name) =>
        Read(name) is { } value ? Encoding.UTF8.GetString(value).TrimEnd('\0') : null;

    // Whether the I/O registry has a service of this class, such as
    // AppleSmartBattery on a MacBook.
    public static bool HasService(string className)
    {
        // The lookup takes ownership of the matching dictionary.
        var service = IOServiceGetMatchingService(0, IOServiceMatching(className));
        if (service == 0)
            return false;

        // A release that fails leaks one handle in a process about to move on.
        _ = IOObjectRelease(service);
        return true;
    }

    // Whether this process's login session can reach the window server: true
    // for Terminal and anything started from the Dock or Finder, false for SSH
    // sessions and launch daemons.
    public static bool HasGraphicSession() =>
        SessionGetInfo(CallerSecuritySession, out _, out var attributes) == 0
        && (attributes & SessionHasGraphicAccess) != 0;
}
