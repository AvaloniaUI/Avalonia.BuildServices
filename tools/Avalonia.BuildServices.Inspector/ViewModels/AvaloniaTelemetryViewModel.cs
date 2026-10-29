using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TelemetryInspector.ViewModels;

public partial class AvaloniaTelemetryViewModel : ViewModelBase
{
    public ObservableCollection<TelemetryEvent> BuildTelemetryPayloads { get; } = new();
    
    [ObservableProperty]
    private TelemetryEvent? _selectedPayload;

    public AvaloniaTelemetryViewModel()
    {
        FetchBuildTelemetry();
    }
    
    /// <summary>
    /// Fetches the Avalonia Build Telemetry payloads. 
    /// </summary>
    private void FetchBuildTelemetry()
    {
        if (Directory.Exists(Common.AppDataFolder))
        {
            var tempPayloads = new List<TelemetryEvent>();
            
            foreach (var dataFile in Directory.EnumerateFiles(Common.AppDataFolder))
            {
                if (Common.IsSpoolFile(dataFile))
                {
                    try
                    {
                        tempPayloads.Add(TelemetryEvent.ReadSpoolFile(dataFile));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            
            // Sort by timestamp descending (newest first) and add to ObservableCollection
            foreach (var payload in tempPayloads.OrderByDescending(p => DateTimeOffset.Parse(p.Timestamp, CultureInfo.InvariantCulture)))
            {
                BuildTelemetryPayloads.Add(payload);
            }
        }
    }
}