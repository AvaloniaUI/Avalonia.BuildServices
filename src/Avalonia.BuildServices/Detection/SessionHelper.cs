using System;
using System.Runtime.InteropServices;

namespace Avalonia.Telemetry;

// Whether the build was started from a logged-in desktop session: someone at
// the machine, or a tool they run there, such as an IDE or a coding agent in
// their terminal. Build agents running as services, over SSH or in containers
// have no such session. Asked once per machine identifier, with the machine
// kind, so it describes how the machine was first used. Only the answer is sent.
internal static class SessionHelper
{
    public static bool IsInteractive(BuildEnvironment environment)
    {
        try
        {
            // False for a service, or a scheduled task that runs whether or not
            // anyone is logged on: their window station is not visible.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Environment.UserInteractive;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return MacUtil.HasGraphicSession();

            // An X11 or Wayland session sets these for everything run in it, and
            // WSLg sets them in WSL. Only whether they are set is read.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return environment.IsSet("DISPLAY") || environment.IsSet("WAYLAND_DISPLAY");

            return false;
        }
        catch
        {
            return false;
        }
    }
}
