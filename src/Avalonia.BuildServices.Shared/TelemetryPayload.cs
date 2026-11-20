using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Avalonia.Telemetry;

// A build as the task records it (Initialise), and the binary spool records of
// packages before version 3, which the collector still forwards
// (FromByteArray). Version 3 spools and sends it as a TelemetryEvent (ToEvent).
public class TelemetryPayload
{
    private TelemetryPayload()
    {
    }

    public Guid RecordId { get; private set; }
    
    public DateTimeOffset TimeStamp { get; private set; }
    
    public Guid Machine { get; private set; }
    
    public string ProjectRootHash { get; private set; } = string.Empty;
    
    public string ProjectHash { get; private  set; } = string.Empty;
    
    public Ide Ide { get; private set; }
    
    public CiProvider CiProvider { get; private set; }
    
    public string? OutputType { get; private set; }
    
    public string? Tfm { get; private set; }
    
    public string? Rid { get; private set; }
    
    public string? AvaloniaMainPackageVersion { get; private set; }
    
    public string? OSDescription { get; private set; }
    
    public Architecture ProcessorArchitecture { get; private set; }
    
    //Additions for V2. Null for a version 1 record, which cannot carry them.
    public ProductTier? ProductTier { get; private set; }

    public string? OperatingSystem { get; private set; }

    //Additions for V3. Null when the record was spooled by an older package.
    public IdeTerminal? Terminal { get; private set; }

    public bool? Container { get; private set; }

    public bool? ContinuousIntegrationBuild { get; private set; }

    public string? ProjectLicenseTier { get; private set; }

    public string[]? LicensedProducts { get; private set; }

    public string? XpfVersion { get; private set; }

    // 0 (physical hardware) to 100 (virtual machine); see MachineKindHelper.
    public byte? VmConfidence { get; private set; }

    // 0 (someone's desktop or laptop) to 100 (a server or cloud machine).
    public byte? ServerConfidence { get; private set; }

    // The identifier's first build was started from a logged-in desktop
    // session; see SessionHelper.
    public bool? InteractiveSession { get; private set; }

    public AiAgent? Agent { get; private set; }

    public bool? PublishAot { get; private set; }

    public bool? PublishTrimmed { get; private set; }

    // Later version 3 additions. Null when the record was spooled by a package
    // without them; see BuildSettings and HostMachine for what each means.
    public ProjectLanguage? Language { get; private set; }

    public BuildConfiguration? Configuration { get; private set; }

    public bool? Publishing { get; private set; }

    public bool? CompiledBindingsByDefault { get; private set; }

    public bool? XamlCompilation { get; private set; }

    public bool? NameGenerator { get; private set; }

    public LinuxDesktop? Desktop { get; private set; }

    public DisplayServer? DisplayServer { get; private set; }

    public bool? Wsl { get; private set; }

    public int? ProcessorCount { get; private set; }

    public int? MemoryGb { get; private set; }

    // The version of this package that wrote the record.
    public string? PackageVersion { get; private set; }

    public static TelemetryPayload FromBinaryReader(BinaryReader reader)
    {
        var result = new TelemetryPayload();
        var version = reader.ReadInt16();

        if (version is 1 or 2)
        {
            result.RecordId = new Guid(reader.ReadBytes(16));
            result.TimeStamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            result.Machine = new Guid(reader.ReadBytes(16));
            result.ProjectRootHash = reader.ReadString();
            result.ProjectHash = reader.ReadString();

            // Three strings the first packages wrote, which nothing keeps.
            reader.ReadString();
            reader.ReadString();
            reader.ReadString();

            result.Ide = (Ide)reader.ReadByte();
            result.CiProvider = (CiProvider)reader.ReadByte();
            result.OutputType = reader.ReadString();
            result.Tfm = reader.ReadString();
            result.Rid = reader.ReadString();
            result.AvaloniaMainPackageVersion = reader.ReadString();
            result.OSDescription = reader.ReadString();
            result.ProcessorArchitecture = (Architecture)reader.ReadByte();

            if (version is 2)
            {
                // The device id (a hash of the machine and user names) is no
                // longer sent.
                reader.ReadString();
                result.ProductTier = (ProductTier)reader.ReadByte();
                result.OperatingSystem = reader.ReadString();
            }
        }
        else
        {
            // Version 3 and later spool JSON instead. Reading on would send
            // garbage that fails the whole batch; the caller skips the file.
            throw new InvalidDataException($"Unknown telemetry record version {version}.");
        }

        return result;
    }

    public static TelemetryPayload FromByteArray(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        return FromBinaryReader(reader);
    }

    internal static TelemetryPayload Initialise(BuildEnvironment environment, BuildDetails build)
    {
        var result = new TelemetryPayload();

        result.RecordId = Guid.NewGuid();
        result.TimeStamp = DateTimeOffset.UtcNow;
        result.Machine = build.Machine;
        result.Ide = DetectIde(environment, build.BuildingInsideVisualStudio);
        result.CiProvider = ContinuousIntegrationHelper.DetectCiProvider(environment);
        result.OutputType = build.OutputType;
        result.Tfm = build.Tfm;
        result.Rid = build.Rid;
        result.AvaloniaMainPackageVersion = build.AvaloniaVersion;
        result.OSDescription = build.OsDescription;
        result.ProcessorArchitecture = RuntimeInformation.ProcessArchitecture;
        result.ProjectRootHash = HashProperty(build.ProjectName?.Split('.').FirstOrDefault() ?? "");
        result.ProjectHash = HashProperty(build.ProjectName);

        // New for V2
        result.ProductTier = build.ProductTier;
        result.OperatingSystem = GetOperatingSystem();

        // New for V3. All for product improvement (purpose (a)): where developers
        // build, telling automated builds from developer builds, and which paid
        // components and products are in use.
        result.Terminal = result.Ide == Ide.Cli ? DetectTerminal(environment) : IdeTerminal.None;
        result.Container = IsContainer(environment);
        result.ContinuousIntegrationBuild = build.ContinuousIntegrationBuild;
        result.ProjectLicenseTier = build.ProjectLicenseTier ?? string.Empty;
        result.LicensedProducts = (build.LicensedProducts ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        result.XpfVersion = build.XpfVersion ?? string.Empty;

        // Also purpose (a): whether the build ran on someone's desktop or
        // laptop or on a server, physical or virtual, how recently the machine
        // started (build agents are often booted for one job), and whether
        // anyone's desktop session started the machine's first build. With
        // Container and the CI fields they tell automated builds from
        // developer ones. All are numbers or flags; nothing the detection
        // read is kept.
        result.VmConfidence = build.VmConfidence;
        result.ServerConfidence = build.ServerConfidence;
        result.InteractiveSession = build.InteractiveSession;

        // Also purpose (a): how much building is now done by AI coding agents
        // rather than by people, on developer machines and in CI.
        result.Agent = build.Agent;

        // Also purpose (a): how many apps ship trimmed or compiled ahead of
        // time, on any platform (see PublishHelper), which decides how much
        // work goes into keeping Avalonia safe for the trimmer and for running
        // without a JIT.
        result.PublishAot = build.PublishAot;
        result.PublishTrimmed = build.PublishTrimmed;

        // Also purpose (a): the language and configuration the project builds
        // in, whether this was a publish rather than a build, and which of
        // Avalonia's own build switches are on, which decide what Avalonia's
        // defaults should be and how many apps take its slower paths.
        result.Language = build.Settings.Language;
        result.Configuration = build.Settings.Configuration;
        result.Publishing = build.Settings.Publishing;
        result.CompiledBindingsByDefault = build.Settings.CompiledBindingsByDefault;
        result.XamlCompilation = build.Settings.XamlCompilation;
        result.NameGenerator = build.Settings.NameGenerator;

        // Also purpose (a): the Linux desktops and display servers developers
        // build under, which set the Linux backend's priorities; whether that
        // Linux runs inside Windows; and the class of machine, as a processor
        // count and a memory size, which set performance targets for the
        // previewer and for build times.
        result.Desktop = build.Host.Desktop;
        result.DisplayServer = build.Host.DisplayServer;
        result.Wsl = build.Host.Wsl;
        result.ProcessorCount = build.Host.ProcessorCount;
        result.MemoryGb = build.Host.MemoryGb;

        // So a field only newer packages fill can be read with its version.
        result.PackageVersion = build.PackageVersion;
        return result;
    }

    public TelemetryEvent ToEvent() => new()
    {
        RecordId = RecordId.ToString(),
        Timestamp = TimeStamp.ToString("o"),
        MachineId = Machine.ToString(),
        ProjectRootHash = ProjectRootHash ?? string.Empty,
        ProjectHash = ProjectHash ?? string.Empty,
        Ide = Ide.ToString(),
        CiProvider = CiProvider.ToString(),
        OutputType = OutputType ?? string.Empty,
        Tfm = Tfm ?? string.Empty,
        Rid = Rid ?? string.Empty,
        AvaloniaVersion = AvaloniaMainPackageVersion ?? string.Empty,
        OsDescription = OSDescription ?? string.Empty,
        OperatingSystem = OperatingSystem,
        Arch = ProcessorArchitecture.ToString(),
        ProductTier = ProductTier?.ToString(),
        Terminal = Terminal?.ToString(),
        Container = Container,
        ContinuousIntegrationBuild = ContinuousIntegrationBuild,
        ProjectLicenseTier = ProjectLicenseTier,
        Products = LicensedProducts,
        XpfVersion = XpfVersion,
        VmConfidence = VmConfidence,
        ServerConfidence = ServerConfidence,
        InteractiveSession = InteractiveSession,
        AiAgent = Agent?.ToString(),
        PublishAot = PublishAot,
        PublishTrimmed = PublishTrimmed,
        Language = Language?.ToString(),
        Configuration = Configuration?.ToString(),
        Publishing = Publishing,
        CompiledBindingsByDefault = CompiledBindingsByDefault,
        XamlCompilation = XamlCompilation,
        NameGenerator = NameGenerator,
        LinuxDesktop = Desktop?.ToString(),
        DisplayServer = DisplayServer?.ToString(),
        Wsl = Wsl,
        ProcessorCount = ProcessorCount,
        MemoryGb = MemoryGb,
        PackageVersion = PackageVersion,
    };

    internal static string HashProperty(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Upper-case hex with no separators either way: the server matches
        // these against the hashes older packages sent.
        byte[] textData = Encoding.UTF8.GetBytes(value);
#if NET5_0_OR_GREATER
        return Convert.ToHexString(SHA256.HashData(textData));
#else
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(textData)).Replace("-", string.Empty);
#endif
    }
    
    // The IDE that ran the build itself. A build typed into a terminal is Cli,
    // and DetectTerminal says whose terminal. Visual Studio and Rider run their
    // builds on a different process from their terminals, so the two never mix:
    // - Rider sets RESHARPER_FUS_BUILD on its .NET backend, which runs Rider's
    //   builds; its terminal is started by the front end and does not have it.
    //   Checked first because Rider can pass Visual Studio's build properties on.
    // - Visual Studio sets the BuildingInsideVisualStudio property on its builds.
    //   (VisualStudioVersion is also set by the Developer Command Prompt and by
    //   CI agents with Build Tools, so it does not identify the IDE.)
    // VS Code has no build engine: a tasks.json build, F5's pre-launch task and
    // C# Dev Kit's Build command are all tasks its terminal host runs, with the
    // same environment as a command typed into its terminal. Nothing tells the
    // two apart, and both mean the developer works in VS Code, so both are VsCode:
    // - The terminal host removes every VSCODE_* variable and sets
    //   TERM_PROGRAM=vscode. Editors built on VS Code set it too; AiAgentHelper
    //   tells the coding agents among them apart.
    // - C# Dev Kit spawns some builds from the extension host instead, which
    //   keeps VSCODE_PID and VSCODE_CWD and has no TERM_PROGRAM.
    private static Ide DetectIde(BuildEnvironment environment, bool buildingInsideVisualStudio)
    {
        if (environment.Contains("VSMAC_MSBUILD_BUILDER_SETTINGS_FILE"))
            return Ide.Vs4Mac;
        if (environment.Contains("RESHARPER_FUS_BUILD"))
            return Ide.Rider;
        if (buildingInsideVisualStudio)
            return Ide.Vs;
        if (environment.HasValue("TERM_PROGRAM", "vscode")
            || environment.Contains("VSCODE_PID") || environment.Contains("VSCODE_CWD"))
        {
            return Ide.VsCode;
        }
        return Ide.Cli;
    }

    // For a Cli build, the IDE whose integrated terminal it ran in. VS Code's
    // terminal is DetectIde's: a build there is a VS Code build. Only the result
    // is sent, never the variables.
    private static IdeTerminal DetectTerminal(BuildEnvironment environment)
    {
        if (environment.HasValue("TERMINAL_EMULATOR", "JetBrains-JediTerm"))
            return IdeTerminal.Rider;
        if (environment.HasValue("VSAPPIDNAME", "devenv.exe"))
            return IdeTerminal.Vs;
        return IdeTerminal.None;
    }

    // The signs systemd's detect_container (src/basic/virt.c) reads without
    // privileges. Microsoft's .NET SDK images set DOTNET_RUNNING_IN_CONTAINER;
    // Docker creates /.dockerenv, Podman /run/.containerenv, and systemd-nspawn
    // and LXC record their name in /run/systemd/container. Failing those, a
    // process outside the root PID namespace is in some container. Flatpak and
    // snap sandboxes use PID namespaces too and are a developer's desktop, so
    // they are told apart first.
    private static bool IsContainer(BuildEnvironment environment)
    {
        if (environment.HasValue("DOTNET_RUNNING_IN_CONTAINER", "true")
            || File.Exists("/.dockerenv")
            || File.Exists("/run/.containerenv")
            || File.Exists("/run/systemd/container"))
        {
            return true;
        }

        if (environment.Contains("FLATPAK_ID") || environment.Contains("SNAP") || File.Exists("/.flatpak-info"))
            return false;

        return IsOutsideRootPidNamespace();
    }

    // The root PID namespace's inode has been fixed at 0xEFFFFFFC since Linux
    // 3.8, so /proc/self/ns/pid reads "pid:[4026531836]" there and something
    // else in any namespace a container manager made. A container sharing the
    // host's PID namespace (--pid=host) is not told; nothing read is kept.
    private static bool IsOutsideRootPidNamespace()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        try
        {
            var buffer = new byte[64];
            var length = (int)readlink("/proc/self/ns/pid", buffer, (IntPtr)buffer.Length);
            return length > 0 && Encoding.ASCII.GetString(buffer, 0, length) != "pid:[4026531836]";
        }
        catch
        {
            // No libc to call, or no /proc: no evidence.
            return false;
        }
    }

    // The path is plain ASCII. Best-fit mapping is off so that a character
    // with no ANSI form is never swapped for one that looks like it.
    [DllImport("libc", SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr readlink([MarshalAs(UnmanagedType.LPStr)] string path, byte[] buffer, IntPtr size);

    private static string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        return "Unknown";
    }
}
