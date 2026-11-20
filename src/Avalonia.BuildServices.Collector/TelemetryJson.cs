using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Avalonia.Telemetry;

// Build telemetry v3: the JSON envelope the collector accepts at POST /api/usage.
// Contract and shared fixtures: TelemetryCollector/docs/BUILD_TELEMETRY_V3_CONTRACT.md.
// The events are the spooled TelemetryEvents as they are.
internal static class TelemetryJson
{
    public const int SchemaVersion = 3;

    private static readonly DataContractJsonSerializer Serializer = new(typeof(Envelope));

    // The package version (the build stamps it on this assembly as
    // "<version>+<commit>"), so the collector can tell which release produced
    // every row.
    private static readonly string SenderVersion =
        (typeof(TelemetryJson).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(TelemetryJson).Assembly.GetName().Version?.ToString()
         ?? "unknown").Split('+')[0];

    // The request body: the envelope as UTF-8 JSON.
    public static byte[] Encode(IList<TelemetryEvent> events)
    {
        var envelope = new Envelope(new Sender("Avalonia.BuildServices", SenderVersion), events);

        using var stream = new MemoryStream();
        Serializer.WriteObject(stream, envelope);
        return stream.ToArray();
    }

    // The serializer wants a setter on every member it writes; a private one
    // will do.
    [DataContract]
    private sealed class Envelope(Sender sender, IList<TelemetryEvent> events)
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; private set; } = TelemetryJson.SchemaVersion;

        [DataMember(Name = "sender")] public Sender Sender { get; private set; } = sender;

        [DataMember(Name = "events")] public IList<TelemetryEvent> Events { get; private set; } = events;
    }

    [DataContract]
    private sealed class Sender(string name, string version)
    {
        [DataMember(Name = "name")] public string Name { get; private set; } = name;

        [DataMember(Name = "version")] public string Version { get; private set; } = version;
    }
}
