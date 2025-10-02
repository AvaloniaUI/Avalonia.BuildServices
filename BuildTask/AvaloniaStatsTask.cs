using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using static Avalonia.Telemetry.Common;

namespace Avalonia.Telemetry;

public class AvaloniaStatsTask : ITask
{
    private Guid? _uniqueIdentifier;

    [Required] public string ProjectName { get; set; }

    [Required] public string TargetFramework { get; set; }

    public string RuntimeIdentifier { get; set; }
    
    public string AvaloniaPackageVersion { get; set; }
    
    [Required] public string OutputType { get; set; }

    public bool Execute()
    {
        if (Environment.GetEnvironmentVariables().Contains("AVALONIA_TELEMETRY_OPTOUT") || Environment.GetEnvironmentVariables().Contains("NCrunch"))
        {
            return true;
        }

        TelemetryPayload? telemetryData = null;
        
        try
        {
            telemetryData = RunStats();
        }
        catch (Exception)
        {
        }

        if (telemetryData == null)
        {
            // failed to calculate telemetry data.
            return true;
        }

        WriteTelemetry(telemetryData);

        StartCollector();

        return true;
    }

    private void WriteTelemetry(TelemetryPayload telemetryData)
    {
        try
        {
            TelemetryWriter.WriteTelemetry(telemetryData);
        }
        catch (Exception)
        {
            // All hope is lost!
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

    private Guid UniqueIdentifier => _uniqueIdentifier ??= GetOrCreateUniqueIdentifier();

    private TelemetryPayload RunStats()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }
        
        return TelemetryPayload.Initialise(UniqueIdentifier, ProjectName, TargetFramework, RuntimeIdentifier, AvaloniaPackageVersion, OutputType);
    }

    private Guid GetOrCreateUniqueIdentifier()
    {
        // Migrate legacy data if exists.
        if (Directory.Exists(LegacyAppDataFolder))
        {
            try
            {
                // If we have no new folder - just move it.
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.Move(LegacyAppDataFolder, AppDataFolder);
                }
                // If we have both - copy id and delete old folder.
                else if (File.Exists(LegacyIdPath) && !File.Exists(IdPath))
                {
                    File.Copy(LegacyIdPath, IdPath);
                    Directory.Delete(LegacyAppDataFolder, true);
                }
                // If we have both and both have id - just delete old folder.
                else
                {
                    Directory.Delete(LegacyAppDataFolder, true);
                }
            }
            catch
            {
                // Ignore any issues with migration.
                // If we are lucky - it will succeed next time.
            }
        }

        // Create new id if we have none.
        if (!File.Exists(IdPath))
        {
            var idDirectory = Path.GetDirectoryName(IdPath);
            if (!string.IsNullOrEmpty(idDirectory) && !Directory.Exists(idDirectory))
            {
                Directory.CreateDirectory(idDirectory);
            }
            File.WriteAllBytes(IdPath, Guid.NewGuid().ToByteArray());
        }

        return new Guid(File.ReadAllBytes(IdPath));
    }

    public IBuildEngine BuildEngine { get; set; }
    
    public ITaskHost HostObject { get; set; }
}
