extern alias collector;

using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using CollectorEvent = collector::Avalonia.Telemetry.TelemetryEvent;
using CollectorJson = collector::Avalonia.Telemetry.TelemetryJson;

namespace Avalonia.Telemetry.Tests;

// The request body, as the server's contract describes it. Only the encoder is
// used here: nothing in these tests sends anything anywhere.
public class TelemetryJsonTests
{
    private static JsonElement Encode(params CollectorEvent[] events)
    {
        var body = CollectorJson.Encode(new List<CollectorEvent>(events));

        // No byte order mark: the body starts with the object itself.
        Assert.Equal((byte)'{', body[0]);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Wraps_the_events_in_an_envelope()
    {
        var root = Encode(
            new CollectorEvent { RecordId = "one", MachineId = "machine" },
            new CollectorEvent { RecordId = "two", MachineId = "machine" });

        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Avalonia.BuildServices", root.GetProperty("sender").GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("sender").GetProperty("version").GetString()));

        var events = root.GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal("one", events[0].GetProperty("recordId").GetString());
        Assert.Equal("two", events[1].GetProperty("recordId").GetString());
        Assert.Equal("machine", events[1].GetProperty("machineId").GetString());
    }

    [Fact]
    public void Leaves_the_build_metadata_out_of_the_sender_version()
    {
        var version = Encode(new CollectorEvent()).GetProperty("sender").GetProperty("version").GetString();

        Assert.DoesNotContain("+", version);
    }

    [Fact]
    public void Keeps_text_that_needs_escaping_whole()
    {
        const string awkward = "quote \" backslash \\ newline \n café 中";

        var root = Encode(new CollectorEvent { RecordId = "one", Rid = awkward });

        Assert.Equal(awkward, root.GetProperty("events")[0].GetProperty("rid").GetString());
    }
}
