using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Avalonia.Telemetry;

// One build: the event of the build telemetry v3 JSON contract
// (TelemetryCollector/docs/BUILD_TELEMETRY_V3_CONTRACT.md). The build task
// spools it to disk as it is, and the collector sends the spooled events in one
// envelope. A member the spooling package could not know is null.
[DataContract]
public sealed class TelemetryEvent
{
    // DataContractJsonSerializer ships with .NET Framework and every .NET, so the
    // task needs no JSON library of its own inside Visual Studio's MSBuild. It
    // reads and writes UTF-8 streams, so spool files go straight through it.
    private static readonly DataContractJsonSerializer Serializer = new(typeof(TelemetryEvent));

    [DataMember(Name = "recordId")] 
    public string RecordId { get; set; } = string.Empty;

    [DataMember(Name = "timestamp")] 
    public string Timestamp { get; set; } = string.Empty;

    [DataMember(Name = "machineId")] 
    public string MachineId { get; set; } = string.Empty;

    [DataMember(Name = "projectRootHash")] 
    public string ProjectRootHash { get; set; } = string.Empty;

    [DataMember(Name = "projectHash")] 
    public string ProjectHash { get; set; } = string.Empty;

    [DataMember(Name = "ide")] 
    public string Ide { get; set; } = string.Empty;

    [DataMember(Name = "ciProvider")] 
    public string CiProvider { get; set; } = string.Empty;

    [DataMember(Name = "outputType")] 
    public string OutputType { get; set; } = string.Empty;

    [DataMember(Name = "tfm")] 
    public string Tfm { get; set; } = string.Empty;

    [DataMember(Name = "rid")] 
    public string Rid { get; set; } = string.Empty;

    [DataMember(Name = "avaloniaVersion")] 
    public string AvaloniaVersion { get; set; } = string.Empty;

    [DataMember(Name = "osDescription")] 
    public string OsDescription { get; set; } = string.Empty;

    // Null for a build a version 1 package recorded: it could not tell.
    [DataMember(Name = "operatingSystem")] 
    public string? OperatingSystem { get; set; }

    [DataMember(Name = "arch")] 
    public string Arch { get; set; } = string.Empty;

    // The contract's name for the product tier. Null like OperatingSystem.
    [DataMember(Name = "licenseTier")] 
    public string? ProductTier { get; set; }

    [DataMember(Name = "terminal")] 
    public string? Terminal { get; set; }

    [DataMember(Name = "container")] 
    public bool? Container { get; set; }

    [DataMember(Name = "continuousIntegrationBuild")] 
    public bool? ContinuousIntegrationBuild { get; set; }

    [DataMember(Name = "projectLicenseTier")] 
    public string? ProjectLicenseTier { get; set; }

    [DataMember(Name = "products")] 
    public string[]? Products { get; set; }

    [DataMember(Name = "xpfVersion")] 
    public string? XpfVersion { get; set; }

    [DataMember(Name = "vmConfidence")] 
    public byte? VmConfidence { get; set; }

    [DataMember(Name = "serverConfidence")] 
    public byte? ServerConfidence { get; set; }

    [DataMember(Name = "interactiveSession")] 
    public bool? InteractiveSession { get; set; }

    [DataMember(Name = "aiAgent")] 
    public string? AiAgent { get; set; }

    [DataMember(Name = "publishAot")] 
    public bool? PublishAot { get; set; }

    [DataMember(Name = "publishTrimmed")] 
    public bool? PublishTrimmed { get; set; }

    [DataMember(Name = "language")] 
    public string? Language { get; set; }

    [DataMember(Name = "configuration")] 
    public string? Configuration { get; set; }

    [DataMember(Name = "publishing")] 
    public bool? Publishing { get; set; }

    [DataMember(Name = "compiledBindingsByDefault")] 
    public bool? CompiledBindingsByDefault { get; set; }

    [DataMember(Name = "xamlCompilation")] 
    public bool? XamlCompilation { get; set; }

    [DataMember(Name = "nameGenerator")] 
    public bool? NameGenerator { get; set; }

    // Both null on anything but Linux.
    [DataMember(Name = "linuxDesktop")] 
    public string? LinuxDesktop { get; set; }

    [DataMember(Name = "displayServer")] 
    public string? DisplayServer { get; set; }

    [DataMember(Name = "wsl")] 
    public bool? Wsl { get; set; }

    [DataMember(Name = "processorCount")] 
    public int? ProcessorCount { get; set; }

    [DataMember(Name = "memoryGb")] 
    public int? MemoryGb { get; set; }

    [DataMember(Name = "packageVersion")] 
    public string? PackageVersion { get; set; }

    public void WriteTo(Stream stream) => Serializer.WriteObject(stream, this);

    // A spool file: JSON written by this package, or a binary record written by
    // a package before version 3 on the same machine (Common.IsSpoolFile).
    public static TelemetryEvent ReadSpoolFile(string path)
    {
        if (!Path.GetFileName(path).StartsWith(Common.SpoolFilePrefix, StringComparison.Ordinal))
            return TelemetryPayload.FromByteArray(File.ReadAllBytes(path)).ToEvent();

        using var file = File.OpenRead(path);

        // The serializer answers null for a file that holds the JSON "null".
        return Serializer.ReadObject(file) as TelemetryEvent
               ?? throw new InvalidDataException("The spool file holds no build.");
    }
}
