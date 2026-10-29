using System;
using Avalonia.Telemetry;

internal static class Program
{
    // Whatever goes wrong, this process ends quietly. Nobody is watching it,
    // and an unhandled exception would only leave a crash report behind.
    public static int Main()
    {
        try
        {
            using var collector = new Collector();
            collector.Execute();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "The telemetry collector stopped.");
        }

        return 0;
    }
}
