using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using static Avalonia.Telemetry.BuildServicesPaths;

namespace Avalonia.Telemetry;

public class AvaloniaStatsTask : ITask
{
    private Guid? _uniqueIdentifier;

    [Required] public string ProjectName { get; set; }

    [Required] public string TargetFramework { get; set; }

    public string RuntimeIdentifier { get; set; }
    
    public string AvaloniaPackageVersion { get; set; }

    public ITaskItem[] LicenseKeys { get; set; } = [];

    [Required] public string OutputType { get; set; }

    public bool Execute()
    {
        Logger.Configure(Path.Combine(AppDataFolder, "buildtasks.log"));

        var accelerateTier = AccelerateTierHelper.ResolveAccelerateTierFromLicenseTickets(LicenseKeys?.Select(k => k.ItemSpec));
        var hasOptedOut = HasOptedOut();

        switch (accelerateTier)
        {
            case AccelerateTier.Community:
                BuildEngine?.LogMessageEvent(new BuildMessageEventArgs(
                    "Avalonia Accelerate Community requires telemetry. " +
                    "To opt out, please upgrade to a paid tier. Learn more: https://avaloniaui.net/accelerate/", "", "AvaloniaStatsTask",
                    MessageImportance.High));
                // Override opt-out
                break;
            
            case AccelerateTier.Trial:
                BuildEngine?.LogMessageEvent(new BuildMessageEventArgs(
                    "Avalonia Accelerate Trial requires telemetry. " +
                    "To opt out, please purchase a license. Learn more: https://avaloniaui.net/accelerate/", "", "AvaloniaStatsTask",
                    MessageImportance.High));
                // Override opt-out
                break;
            
            default:
                // No license - respect opt-out for FOSS users
                if (hasOptedOut)
                    return true;
                break;
        }

        TelemetryPayload? telemetryData = null;
        
        try
        {
            telemetryData = RunStats(accelerateTier);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to collect telemetry data.");
        }

        if (telemetryData == null)
        {
            // failed to calculate telemetry data.
            return true;
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

        return true;
    }

    private bool HasOptedOut()
    {
        return Environment.GetEnvironmentVariables().Contains("AVALONIA_TELEMETRY_OPTOUT") || Environment.GetEnvironmentVariables().Contains("NCrunch");
    }

    private void WriteTelemetry(TelemetryPayload telemetryData)
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

    private void StartCollector()
    {
        var thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var dir = Path.GetDirectoryName(thisAssemblyPath);
        var assemblyPath = Path.Combine(dir, "Avalonia.BuildServices.Collector.dll");

        var runtimeConfig = Path.Combine(dir, "runtimeconfig.json");
        
        var cmdLine = $"exec --runtimeconfig {runtimeConfig} {assemblyPath}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WinProcUtil.StartBackground(null, "dotnet " + cmdLine);
        }
        else
        {
            var startInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                FileName = "dotnet",
                Arguments = cmdLine
            };

            var proc = Process.Start(startInfo);
            proc?.StandardError.Close();
            proc?.StandardInput.Close();
            proc?.StandardOutput.Close();
        }
    }

    private TelemetryPayload RunStats(AccelerateTier accelerateTier)
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }

        return TelemetryPayload.Initialise(UniqueIdentifierProvider.GetOrCreateIdentifier(), ProjectName,
            TargetFramework, RuntimeIdentifier, AvaloniaPackageVersion, OutputType, accelerateTier);
    }

    public IBuildEngine BuildEngine { get; set; }

    public ITaskHost HostObject { get; set; }
}