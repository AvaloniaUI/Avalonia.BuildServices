using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using static Avalonia.Telemetry.Common;

namespace Avalonia.Telemetry;

// The MSBuild task Avalonia.BuildServices.targets runs once per project build,
// before CoreCompile. It is the collection point, not the wire format.
//
// The properties below are MSBuild inputs: whatever the build knows that the
// detectors need. Most of them never leave this process. Each group is read,
// reduced to the one or two values the record keeps, and dropped:
//
//  - ProjectName is hashed. TargetFramework, RuntimeIdentifier, OutputType,
//    AvaloniaPackageVersion and XpfVersion are kept as they are.
//  - ProjectLicenseTier and ProjectLicenseFeatures, with the machine's licence
//    tickets, become one tier name and one decision: may this build opt out.
//    LicensedProducts is kept as a list of package names.
//  - PublishAot, PublishTrimmed, TrimMode, RunAOTCompilation,
//    AndroidEnableProfiledAot and TargetPlatformIdentifier become two booleans,
//    publishAot and publishTrimmed. They exist only because each platform
//    spells those two facts differently (see PublishHelper).
//  - BuildingInsideVisualStudio and ContinuousIntegrationBuild are two of the
//    signals behind the terminal and CI fields.
//  - ProjectExtension, Configuration and IsPublishing become a language, a
//    configuration from a fixed list, and a flag. UseCompiledBindingsByDefault,
//    EnableXamlCompilation and NameGeneratorEnabled are Avalonia's own
//    switches, kept as three flags.
//
// What is kept is written by TelemetryPayload.Initialise, which is also only an
// in-process record. TelemetryWriter turns it into a TelemetryEvent, the JSON
// schema the backend accepts, and spools that to disk. The collector, started
// as a separate process at the end of Execute, is what sends spool files, and
// it never sees this task's inputs. The README lists what the event contains.
//
// Execute never fails the build. Each step catches and logs its own failures,
// and Execute catches whatever escapes them: a type in the merged licensing
// code failing to load, say, which the runtime raises at the call into the
// method that uses it, outside that method's own try. None of the parameters
// is [Required]: MSBuild fails the build (MSB4044) when a required parameter
// is empty, and the task copes with empty values.
//
// The parameters are a contract with the targets file. MSBuild fails the build
// (MSB4064) when the targets file passes one this class does not have, and on
// .NET Framework it can be running an older copy of this class: it keeps one
// assembly per identity for the life of the process. So a parameter is added,
// renamed or removed only in a build with a new assembly version, which the
// project file gives every CI build.
public class AvaloniaStatsTask : ITask
{
    public string? ProjectName { get; set; }

    public string? TargetFramework { get; set; }

    public string? RuntimeIdentifier { get; set; }
    
    public string? AvaloniaPackageVersion { get; set; }

    public string? OutputType { get; set; }

    // Strings, not bools: MSBuild fails the build when a bool parameter gets a
    // value it cannot parse, and these are user-settable properties.
    public string? BuildingInsideVisualStudio { get; set; }

    public string? ContinuousIntegrationBuild { get; set; }

    // What AvaloniaUI.Licensing resolved from this project's own licence keys:
    // the tier, $(AvaloniaUIResolvedSubscription), and the feature flags,
    // @(AvaloniaUIResolvedLicenseFeature). The tier is reported and folded into
    // the machine-wide tier; the flags decide whether telemetry is required,
    // with the tier standing in for a Community or trial key, whose flags are
    // always empty (see LicenseHelper). Both are empty when the project uses no
    // licensed package or is a library; the flags are also empty on licensing
    // packages too old to emit them.
    public string? ProjectLicenseTier { get; set; }

    public ITaskItem[]? ProjectLicenseFeatures { get; set; }

    // Every licensed Avalonia package adds its product name to
    // @(AvaloniaUILicenseKeyProduct), referenced directly or through a library.
    public ITaskItem[]? LicensedProducts { get; set; }

    // Set by Xpf.Sdk. Empty for anything that is not an XPF project.
    public string? XpfVersion { get; set; }

    // The trimming and ahead-of-time switches of every platform, as the build
    // sees them, so a publish profile or -p: on the command line counts.
    // PublishHelper maps them onto two answers.
    public string? PublishAot { get; set; }

    public string? PublishTrimmed { get; set; }

    public string? TrimMode { get; set; }

    public string? RunAOTCompilation { get; set; }

    public string? AndroidEnableProfiledAot { get; set; }

    public string? TargetPlatformIdentifier { get; set; }

    // The project file's extension: .csproj, .fsproj or .vbproj says the language.
    public string? ProjectExtension { get; set; }

    public string? Configuration { get; set; }

    // $(_IsPublishing): set by dotnet publish, empty for a build.
    public string? IsPublishing { get; set; }

    // Avalonia's own build switches, as its props and targets resolved them by
    // the time this task runs. Strings for the same reason as above.
    public string? UseCompiledBindingsByDefault { get; set; }

    public string? EnableXamlCompilation { get; set; }

    public string? NameGeneratorEnabled { get; set; }

    // The licence notice stands out once per NoticeInterval on each MSBuild
    // node, and is a detail of the log the rest of the time. The task runs once
    // per project and target framework, so a solution build would otherwise
    // say it for every one of them.
    private static readonly long NoticeInterval = Stopwatch.Frequency * 30;

    private static long _lastNotice;

    public bool Execute()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Telemetry task failed.");
        }

        return true;
    }

    private void Run()
    {
        // Read once: every check below sees the same variables.
        var environment = BuildEnvironment.Capture();

        var (productTier, telemetryRequired) = LicenseHelper.Resolve(ProjectLicenseTier,
            ProjectLicenseFeatures?.Select(f => f.ItemSpec) ?? [], TicketsPath);

        if (telemetryRequired)
        {
            AnnounceTelemetryRequired(productTier);
        }
        else if (HasOptedOut(environment))
        {
            return;
        }

        // An account without a profile has nowhere to keep the id or a record.
        if (!HasDataFolder)
        {
            return;
        }

        TelemetryPayload? telemetryData = null;

        try
        {
            telemetryData = RunStats(environment, productTier);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to collect telemetry data.");
        }

        if (telemetryData == null)
        {
            // failed to calculate telemetry data.
            return;
        }

        WriteTelemetry(telemetryData);

        try
        {
            StartCollector();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to start telemetry collector.");
        }
    }

    // Said whether or not an opt-out is set, so the obligation that comes with
    // the licence stays visible.
    private void AnnounceTelemetryRequired(ProductTier productTier)
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            var last = Interlocked.Read(ref _lastNotice);

            // Projects built side by side may get here together. Only the one
            // that moves the mark on speaks up.
            var prominent = (last == 0 || now - last >= NoticeInterval) &&
                            Interlocked.CompareExchange(ref _lastNotice, now, last) == last;

            var licence = productTier == ProductTier.None ? "Your Avalonia licence" : $"Avalonia {productTier}";
            BuildEngine?.LogMessageEvent(new BuildMessageEventArgs(
                $"{licence} requires telemetry. To opt out, please upgrade to a paid tier. Learn more: https://avaloniaui.net/pricing/",
                "", "AvaloniaStatsTask", prominent ? MessageImportance.High : MessageImportance.Low));
        }
        catch (Exception ex)
        {
            // A logger that fails must not cost the build its record.
            Logger.LogException(ex, "Failed to log the licence notice.");
        }
    }

    // AVALONIA_TELEMETRY_OPTOUT, however its name is cased. Setting it opts out,
    // as it always has, unless the value plainly says no: "0" or "false" used to
    // opt out as well, which nobody who wrote them meant. NCrunch builds every
    // project again and again in the background, so its builds never count.
    internal static bool HasOptedOut(BuildEnvironment environment)
    {
        if (environment.Contains("NCrunch"))
        {
            return true;
        }

        if (environment.Get("AVALONIA_TELEMETRY_OPTOUT") is not { } value)
        {
            return false;
        }

        value = value.Trim();

        return value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteTelemetry(TelemetryPayload telemetryData)
    {
        try
        {
            TelemetryWriter.WriteTelemetry(telemetryData);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to write telemetry data.");
        }
    }

    // The machine identifier: sixteen random bytes, minted by the first build
    // on the machine and kept from then on. Packages before version 3 read the
    // same file, so it stays exactly sixteen bytes.
    private static Guid GetMachineId()
    {
        if (TryReadMachineId(out var id))
        {
            return id;
        }

        // There, but not an identifier: a crash or a full disk cut the write
        // short, or a power cut left it full of zeros. Left alone it would fail
        // this read on every build from now on, so it goes.
        //
        // Only a file that is there, and only after reading it again. Parallel
        // builds on a new machine all arrive here together, and one that
        // deleted without looking would take away the identifier another had
        // just stored: each would then report a machine of its own.
        if (File.Exists(IdPath))
        {
            if (TryReadMachineId(out id))
            {
                return id;
            }

            AtomicFile.TryDelete(IdPath);
        }

        // Exactly one build's file takes the name (AtomicFile), and the read
        // below gives every one of them that identifier.
        AtomicFile.TryCreate(IdPath, Guid.NewGuid().ToByteArray());

        return TryReadMachineId(out id)
            ? id
            : throw new IOException("The machine identifier could not be stored.");
    }

    private static bool TryReadMachineId(out Guid id)
    {
        id = Guid.Empty;

        try
        {
            if (!File.Exists(IdPath))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(IdPath);
            if (bytes.Length != 16)
            {
                return false;
            }

            id = new Guid(bytes);
            return id != Guid.Empty;
        }
        catch (IOException)
        {
            // A package before version 3 is writing it in place at this moment.
            return false;
        }
    }

    // What kind of machine this is, detected once per machine identifier and
    // kept beside it, so a build only reads a file. Detection runs again only
    // when the file is missing, was written for an earlier identifier, or has
    // been edited: its checksum covers the identifier and the values. A newly
    // minted identifier is therefore detected on its first build, and so is
    // one minted by an older package.
    private static (byte VmConfidence, byte ServerConfidence, bool InteractiveSession) GetMachineKind(Guid machine, BuildEnvironment environment)
    {
        var stored = ReadMachineKind();
        if (stored.Length == 4
            && stored[3] == MachineKindChecksum(machine, $"{stored[0]} {stored[1]} {stored[2]}")
            && TryParseConfidence(stored[0], out var vm)
            && TryParseConfidence(stored[1], out var server)
            && stored[2] is "0" or "1")
        {
            return (vm, server, stored[2] == "1");
        }

        var (vmConfidence, serverConfidence) = MachineKindHelper.Detect();
        var interactiveSession = SessionHelper.IsInteractive(environment);
        var values = FormattableString.Invariant($"{vmConfidence} {serverConfidence} {(interactiveSession ? 1 : 0)}");
        try
        {
            // What was there failed the check above, so it goes. With nothing
            // there, nothing is deleted: a parallel build may be storing the
            // same values at this moment.
            if (stored.Length > 0)
            {
                AtomicFile.TryDelete(MachineKindPath);
            }

            AtomicFile.TryCreate(MachineKindPath, Encoding.ASCII.GetBytes($"{values} {MachineKindChecksum(machine, values)}"));
        }
        catch
        {
            // A parallel build got there first and detected the same values,
            // or the folder cannot be written. The values stand either way;
            // without the file, detection simply runs again next build.
        }

        return (vmConfidence, serverConfidence, interactiveSession);
    }

    // The parts of the stored machine kind, or none: no file, or one that
    // cannot be read. Either way detection runs again.
    private static string[] ReadMachineKind()
    {
        try
        {
            return File.Exists(MachineKindPath) ? File.ReadAllText(MachineKindPath).Split(' ') : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool TryParseConfidence(string value, out byte confidence) =>
        byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out confidence) && confidence <= 100;

    // Not a secret, and no obstacle to anyone determined: it tells a file this
    // package wrote for this identifier from one edited by hand or left over
    // from an earlier identifier.
    private static string MachineKindChecksum(Guid machine, string values) =>
        TelemetryPayload.HashProperty($"{machine} {values}").Substring(0, 16);

    private TelemetryPayload RunStats(BuildEnvironment environment, ProductTier productTier)
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }

        var machine = GetMachineId();
        var kind = GetMachineKind(machine, environment);
        var settings = new BuildSettings(ParseLanguage(ProjectExtension), ParseConfiguration(Configuration), IsTrue(IsPublishing),
            ParseFlag(UseCompiledBindingsByDefault), ParseFlag(EnableXamlCompilation), ParseFlag(NameGeneratorEnabled));
        var linux = LinuxHostHelper.Detect(environment);
        var host = new HostMachine(linux.Desktop, linux.DisplayServer, linux.Wsl, HardwareHelper.ProcessorCount, HardwareHelper.MemoryGb());

        return TelemetryPayload.Initialise(environment, new BuildDetails
        {
            Machine = machine,
            ProjectName = ProjectName,
            Tfm = TargetFramework,
            Rid = RuntimeIdentifier,
            AvaloniaVersion = AvaloniaPackageVersion,
            OutputType = OutputType,
            ProductTier = productTier,
            OsDescription = OsVersionHelper.Describe(),
            BuildingInsideVisualStudio = IsTrue(BuildingInsideVisualStudio),
            ContinuousIntegrationBuild = IsTrue(ContinuousIntegrationBuild),
            ProjectLicenseTier = ProjectLicenseTier,
            LicensedProducts = LicensedProducts?.Select(p => p.ItemSpec).ToArray(),
            XpfVersion = XpfVersion,
            VmConfidence = kind.VmConfidence,
            ServerConfidence = kind.ServerConfidence,
            InteractiveSession = kind.InteractiveSession,
            Agent = AiAgentHelper.Detect(environment),
            PublishAot = PublishHelper.IsAheadOfTime(PublishAot, RunAOTCompilation, AndroidEnableProfiledAot,
                TargetPlatformIdentifier, RuntimeIdentifier),
            PublishTrimmed = PublishHelper.IsTrimmed(PublishTrimmed, TrimMode),
            Settings = settings,
            Host = host,
            PackageVersion = PackageVersion,
        });
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    // A property that is true, false, or not set at all.
    private static bool? ParseFlag(string? value)
    {
        if (IsTrue(value))
            return true;
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static ProjectLanguage ParseLanguage(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".csproj" => ProjectLanguage.CSharp,
        ".fsproj" => ProjectLanguage.FSharp,
        ".vbproj" => ProjectLanguage.VisualBasic,
        _ => ProjectLanguage.Other,
    };

    // Debug and Release by name; anything else is the project's own name.
    private static BuildConfiguration ParseConfiguration(string? configuration)
    {
        if (string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase))
            return BuildConfiguration.Debug;
        if (string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
            return BuildConfiguration.Release;
        return BuildConfiguration.Other;
    }

    // The version the build stamped on this assembly, "<version>+<commit>"
    // with the commit from SourceLink. The version alone is kept, as the
    // collector does for its envelope.
    private static string PackageVersion =>
        (typeof(AvaloniaStatsTask).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(AvaloniaStatsTask).Assembly.GetName().Version?.ToString()
         ?? "unknown").Split('+')[0];

    public IBuildEngine? BuildEngine { get; set; }

    public ITaskHost? HostObject { get; set; }

    // For the tests. MSBuild sets public properties only, so no build can
    // reach these: a test runs the whole task without starting the real
    // collector, and without this machine's own licences deciding the result.
    internal Action StartCollector { get; set; } = CollectorLauncher.Start;

    internal string? TicketsPath { get; set; }
}