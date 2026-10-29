using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Avalonia.Telemetry;

// The Linux desktop a build ran under, and whether that Linux is the Windows
// Subsystem for Linux. Everything comes from the session's environment and the
// kernel's name; only a name from the lists below leaves, never the values read.
internal static class LinuxHostHelper
{
    public static (LinuxDesktop? Desktop, DisplayServer? DisplayServer, bool Wsl) Detect(BuildEnvironment environment)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return (null, null, false);

        return (ParseDesktop(environment.Get("XDG_CURRENT_DESKTOP")), DetectDisplayServer(environment), IsWsl(environment));
    }

    private static readonly (string Name, LinuxDesktop Desktop)[] Desktops =
    {
        ("GNOME", LinuxDesktop.Gnome),
        ("GNOME-Flashback", LinuxDesktop.Gnome),
        ("GNOME-Classic", LinuxDesktop.Gnome),
        ("KDE", LinuxDesktop.Kde),
        ("XFCE", LinuxDesktop.Xfce),
        ("Cinnamon", LinuxDesktop.Cinnamon),
        ("MATE", LinuxDesktop.Mate),
        ("LXQt", LinuxDesktop.Lxqt),
        ("LXDE", LinuxDesktop.Lxde),
        ("Budgie", LinuxDesktop.Budgie),
        ("Pantheon", LinuxDesktop.Pantheon),
        ("Deepin", LinuxDesktop.Deepin),
        ("DDE", LinuxDesktop.Deepin),
        ("COSMIC", LinuxDesktop.Cosmic),
        ("Unity", LinuxDesktop.Unity),
        ("UKUI", LinuxDesktop.Ukui),
        ("Enlightenment", LinuxDesktop.Enlightenment),
        ("Hyprland", LinuxDesktop.Hyprland),
        ("sway", LinuxDesktop.Sway),
        ("i3", LinuxDesktop.I3),
        ("niri", LinuxDesktop.Niri),
    };

    // XDG_CURRENT_DESKTOP is a colon-separated list, most specific first:
    // "ubuntu:GNOME", "Budgie:GNOME", "X-Cinnamon". The first name we know
    // wins; a desktop we do not know is Other, and no desktop at all is None.
    public static LinuxDesktop ParseDesktop(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LinuxDesktop.None;

        foreach (var part in value!.Split(':'))
        {
            var name = part.Trim();
            if (name.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(2);

            var known = Desktops.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (known.Name != null)
                return known.Desktop;
        }

        return LinuxDesktop.Other;
    }

    // XDG_SESSION_TYPE is what the login manager started: wayland, x11, or tty
    // for a console or SSH login. Without it, or in a tty session, a display
    // the process can reach still counts: WSLg and SSH with X forwarding set
    // one without a session.
    public static DisplayServer DetectDisplayServer(BuildEnvironment environment)
    {
        var session = environment.Get("XDG_SESSION_TYPE")?.Trim().ToLowerInvariant();
        switch (session)
        {
            case "wayland":
                return DisplayServer.Wayland;
            case "x11":
                return DisplayServer.X11;
        }

        if (!string.IsNullOrEmpty(environment.Get("WAYLAND_DISPLAY")))
            return DisplayServer.Wayland;
        if (!string.IsNullOrEmpty(environment.Get("DISPLAY")))
            return DisplayServer.X11;
        return string.IsNullOrEmpty(session) || session is "tty" or "unspecified" ? DisplayServer.None : DisplayServer.Other;
    }

    // WSL sets WSL_DISTRO_NAME and WSL_INTEROP in every process; failing
    // those, the kernel says so.
    public static bool IsWsl(BuildEnvironment environment)
    {
        if (environment.Contains("WSL_DISTRO_NAME") || environment.Contains("WSL_INTEROP"))
            return true;

        try
        {
            return IsWslKernel(File.ReadAllText("/proc/sys/kernel/osrelease"));
        }
        catch
        {
            return false;
        }
    }

    // WSL's kernel is "…-microsoft-standard-WSL2" ("…-Microsoft" for WSL1),
    // and a custom WSL2 kernel keeps "WSL" when it drops the vendor.
    public static bool IsWslKernel(string release) =>
        release.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0
        || release.IndexOf("WSL", StringComparison.Ordinal) >= 0;
}
