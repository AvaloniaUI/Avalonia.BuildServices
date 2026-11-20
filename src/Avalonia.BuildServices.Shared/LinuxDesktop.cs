namespace Avalonia.Telemetry;

// The desktop environment a Linux build ran under, from XDG_CURRENT_DESKTOP
// (see LinuxHostHelper). None is no desktop session at all: a server, a
// container, a console or an SSH login.
public enum LinuxDesktop
{
    None,
    Other,
    Gnome,
    Kde,
    Xfce,
    Cinnamon,
    Mate,
    Lxqt,
    Lxde,
    Budgie,
    Pantheon,
    Deepin,
    Cosmic,
    Unity,
    Ukui,
    Enlightenment,
    Hyprland,
    Sway,
    I3,
    Niri,
}