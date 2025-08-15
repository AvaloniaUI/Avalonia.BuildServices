using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Build.Framework;

namespace Avalonia.Telemetry;

public class AvaloniaStatsTask : ITask
{
    private Guid? _uniqueIdentifier;

    internal static readonly string AppDataFolder = Common.AppDataFolder;

    internal static readonly string IdPath = Common.IdPath;

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

    private Guid UniqueIdentifier
    {
        get
        {
            if (_uniqueIdentifier == null)
            {
                if (!File.Exists(IdPath))
                {
                    File.WriteAllBytes(IdPath, Guid.NewGuid().ToByteArray());
                }

                _uniqueIdentifier = new Guid(File.ReadAllBytes(IdPath));
            }

            return _uniqueIdentifier.Value;
        }
    }

    private TelemetryPayload RunStats()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }
        
        return TelemetryPayload.Initialise(UniqueIdentifier, ProjectName, TargetFramework, RuntimeIdentifier, AvaloniaPackageVersion, OutputType);
    }

    public IBuildEngine BuildEngine { get; set; }
    
    public ITaskHost HostObject { get; set; }
}
