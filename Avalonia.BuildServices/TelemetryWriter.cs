using System.IO;
using Avalonia.BuildServices.Protocol;
using static Avalonia.BuildServices.Protocol.BuildServicesPaths;

namespace Avalonia.BuildServices;

public class TelemetryWriter
{
    internal static void WriteTelemetry(TelemetryPayload telemetryPayload)
    {
        var dataPath = Path.Combine(AppDataFolder, RECORD_FILE_PREFIX + telemetryPayload.RecordId);

        if (!File.Exists(dataPath))
        {
            var data = telemetryPayload.Encode();

            File.WriteAllBytes(dataPath, data);
        }
    }
}