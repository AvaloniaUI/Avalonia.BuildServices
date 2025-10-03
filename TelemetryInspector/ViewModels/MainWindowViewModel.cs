using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TelemetryInspector.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private AvaloniaTelemetryViewModel _avaloniaTelemetryViewModel;

    public MainWindowViewModel()
    {
        AvaloniaTelemetryViewModel = new AvaloniaTelemetryViewModel();
    }


}