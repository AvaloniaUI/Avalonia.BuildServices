namespace Avalonia.Telemetry;

// The machine as the build found it.
public readonly struct HostMachine
{
    public HostMachine(LinuxDesktop? desktop, DisplayServer? displayServer, bool wsl, int processorCount, int? memoryGb)
    {
        Desktop = desktop;
        DisplayServer = displayServer;
        Wsl = wsl;
        ProcessorCount = processorCount;
        MemoryGb = memoryGb;
    }

    // Both null on anything but Linux.
    public LinuxDesktop? Desktop { get; }

    public DisplayServer? DisplayServer { get; }

    // Linux under the Windows Subsystem for Linux.
    public bool Wsl { get; }

    // Logical processors the build may use.
    public int ProcessorCount { get; }

    // Installed memory in gigabytes, as a standard module size; null when it
    // could not be read.
    public int? MemoryGb { get; }
}