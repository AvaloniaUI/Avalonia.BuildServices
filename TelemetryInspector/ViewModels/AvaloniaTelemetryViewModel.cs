using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TelemetryInspector.ViewModels;

public partial class AvaloniaTelemetryViewModel : ViewModelBase
{
    public ObservableCollection<TelemetryPayload> BuildTelemetryPayloads { get; } = new();
    
    [ObservableProperty]
    private TelemetryPayload? _selectedPayload;

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
            var tempPayloads = new List<TelemetryPayload>();
            
            foreach (var dataFile in Directory.EnumerateFiles(Common.AppDataFolder))
            {
                if (Path.GetFileName(dataFile).StartsWith(Common.RECORD_FILE_PREFIX))
                {
                    try
                    {
                        var data = File.ReadAllBytes(dataFile);

                        var payload = TelemetryPayload.FromByteArray(data);
                        tempPayloads.Add(payload);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            
            // Sort by timestamp descending (newest first) and add to ObservableCollection
            foreach (var payload in tempPayloads.OrderByDescending(p => p.TimeStamp))
            {
                BuildTelemetryPayloads.Add(payload);
            }
        }
    }
}