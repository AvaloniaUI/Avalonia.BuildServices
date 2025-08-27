using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
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
        var accelerateTier = GetAccelerateTier();
        var hasOptedOut = HasOptedOut();

        switch (accelerateTier)
        {
            case AccelerateTier.Community:
                Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  Avalonia Accelerate Community requires telemetry.         ║");
                Console.WriteLine("║  To opt out, please upgrade to a paid tier.                ║");
                Console.WriteLine("║  Learn more: https://avaloniaui.net/accelerate/            ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
                // Override opt-out
                break;
            
            case AccelerateTier.Trial:
                Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  Avalonia Accelerate Trial requires telemetry.             ║");
                Console.WriteLine("║  To opt out, please purchase a license.                    ║");
                Console.WriteLine("║  Learn more: https://avaloniaui.net/accelerate/            ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
                // Override opt-out
                break;
            
            case AccelerateTier.None:
                // No license - respect opt-out for FOSS users
                return true;
            
            default:
                // Paid tiers (Pro, Enterprise, etc.) - respect opt-out
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

    private bool HasOptedOut()
    {
        return Environment.GetEnvironmentVariables().Contains("AVALONIA_TELEMETRY_OPTOUT") || Environment.GetEnvironmentVariables().Contains("NCrunch");
    }

    private AccelerateTier GetAccelerateTier()
    {
        try
        {
            // License Tickets location 
            var ticketFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvaloniaUI", "Licensing", "Tickets", "v1");
            
            // If the directory doesn't exist, no licenses are present
            if (!Directory.Exists(ticketFolder))
            {
                return AccelerateTier.None;
            }
            
            // Get all XML files in the tickets directory
            var ticketFiles = Directory.GetFiles(ticketFolder);
            
            if (ticketFiles.Length == 0)
            {
                return AccelerateTier.None;
            }
            
            var highestValidTier = AccelerateTier.None;
            var currentTime = DateTimeOffset.UtcNow;
            
            // Check each ticket file
            foreach (var ticketFile in ticketFiles)
            {
                try
                {
                    var doc = XDocument.Load(ticketFile);
                    var ticketElement = doc.Root;
                    
                    if (ticketElement == null || ticketElement.Name.LocalName != "Ticket")
                    {
                        continue;
                    }
                    
                    // Extract tier
                    var tierElement = ticketElement.Element("Tier");
                    if (tierElement == null)
                    {
                        continue;
                    }
                    
                    // Parse the tier enum value
                    if (!Enum.TryParse<AccelerateTier>(tierElement.Value, true, out var tier))
                    {
                        continue;
                    }
                    
                    // Check if ticket has an expiration date
                    var expiresAtElement = ticketElement.Element("ExpiresAt");
                    if (expiresAtElement != null)
                    {
                        // Try to parse the expiration date
                        if (DateTimeOffset.TryParse(expiresAtElement.Value, out var expiresAt))
                        {
                            // Skip expired tickets
                            if (expiresAt <= currentTime)
                            {
                                continue;
                            }
                        }
                    }
                    
                    // Update the highest valid tier found
                    if (tier > highestValidTier)
                    {
                        highestValidTier = tier;
                    }
                }
                catch
                {
                    // Skip invalid ticket files and continue checking others
                }
            }
            
            return highestValidTier;
        }
        catch
        {
            // If anything goes wrong with accessing the file system, default to None
            return AccelerateTier.None;
        }
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
        
        return TelemetryPayload.Initialise(UniqueIdentifier, ProjectName, TargetFramework, RuntimeIdentifier, AvaloniaPackageVersion, OutputType, GetAccelerateTier());
    }

    public IBuildEngine BuildEngine { get; set; }
    
    public ITaskHost HostObject { get; set; }
}
