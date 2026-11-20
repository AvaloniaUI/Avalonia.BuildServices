namespace Avalonia.Telemetry;

// The IDE whose integrated terminal a command-line build ran in. VsCode is
// kept for the contract and for spool files from earlier version 3 builds: a
// VS Code terminal build is now Ide.VsCode (see DetectIde).
public enum IdeTerminal
{
    None,
    Vs,
    Rider,
    VsCode
}