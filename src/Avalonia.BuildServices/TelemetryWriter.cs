using System.IO;
using static Avalonia.Telemetry.Common;

namespace Avalonia.Telemetry;

// Spools one build: a JSON file in the data folder, which the collector sends
// and then deletes.
internal static class TelemetryWriter
{
    public static void WriteTelemetry(TelemetryPayload telemetryPayload)
    {
        var dataPath = Path.Combine(AppDataFolder, SpoolFilePrefix + telemetryPayload.RecordId + ".json");
        var telemetryEvent = telemetryPayload.ToEvent();

        // A collector may be sweeping the folder at this moment, so the record
        // takes its own name only once it is whole.
        AtomicFile.TryCreate(dataPath, telemetryEvent.WriteTo);
    }
}
