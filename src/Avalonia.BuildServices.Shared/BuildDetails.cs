using System;

namespace Avalonia.Telemetry;

// What the task learned about one build: everything a record holds that
// Initialise does not work out for itself. The values are set by name, so two
// of the same type cannot change places on their way in.
internal sealed class BuildDetails
{
    public Guid Machine { get; set; }

    public string? ProjectName { get; set; }

    public string? Tfm { get; set; }

    public string? Rid { get; set; }

    public string? AvaloniaVersion { get; set; }

    public string? OutputType { get; set; }

    public ProductTier ProductTier { get; set; }

    public string OsDescription { get; set; } = string.Empty;

    public bool BuildingInsideVisualStudio { get; set; }

    public bool ContinuousIntegrationBuild { get; set; }

    public string? ProjectLicenseTier { get; set; }

    public string[]? LicensedProducts { get; set; }

    public string? XpfVersion { get; set; }

    public byte VmConfidence { get; set; }

    public byte ServerConfidence { get; set; }

    public bool InteractiveSession { get; set; }

    public AiAgent Agent { get; set; }

    public bool PublishAot { get; set; }

    public bool PublishTrimmed { get; set; }

    public BuildSettings Settings { get; set; }

    public HostMachine Host { get; set; }

    public string? PackageVersion { get; set; }
}