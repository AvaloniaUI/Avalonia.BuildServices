namespace Avalonia.Telemetry;

// The development environment a build ran in. The order is fixed: version 1
// and 2 binary records store it as a byte value, so a new entry goes last.
public enum Ide
{
    Unknown,
    Vs,
    Vs4Mac,
    Rider,
    Cli,

    // VS Code or an editor built on it (Cursor, Windsurf, VSCodium, Codespaces),
    // whether it ran a build task or a command was typed into its terminal.
    VsCode,
}
