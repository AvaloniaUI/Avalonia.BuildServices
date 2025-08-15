using System.IO;

namespace Avalonia.Telemetry;

public class TelemetryWriter
{
    internal static void WriteTelemetry(TelemetryPayload telemetryPayload)
    {
        var dataPath = Path.Combine(AvaloniaStatsTask.AppDataFolder, Common.RECORD_FILE_PREFIX + telemetryPayload.RecordId);

        if (!File.Exists(dataPath))
        {
            var data = telemetryPayload.Encode();

            File.WriteAllBytes(dataPath, data);
        }
    }
}