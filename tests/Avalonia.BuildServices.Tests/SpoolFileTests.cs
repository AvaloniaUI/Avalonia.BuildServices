using System;
using System.IO;
using Xunit;

namespace Avalonia.Telemetry.Tests;

// Spool files written by an earlier build of this package may still be waiting
// when a newer one sweeps them.
public class SpoolFileTests
{
    [Fact]
    public void Reads_a_record_that_carries_a_field_this_version_dropped()
    {
        var folder = Path.Combine(Sandbox.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // Members in the order the serializer writes them: it skips one it
            // does not know, and must go on reading the ones after it.
            // uptimeMinutes was sent until the package stopped reading uptime.
            var path = Path.Combine(folder, Common.SpoolFilePrefix + "old.json");
            File.WriteAllText(path,
                "{\"recordId\":\"abc\",\"timestamp\":\"2026-11-10T09:00:00Z\",\"tfm\":\"net8.0\"," +
                "\"vmConfidence\":5,\"serverConfidence\":10,\"uptimeMinutes\":1440,\"interactiveSession\":true," +
                "\"packageVersion\":\"12.0.0\"}");

            var record = TelemetryEvent.ReadSpoolFile(path);

            Assert.Equal("abc", record.RecordId);
            Assert.Equal("net8.0", record.Tfm);
            Assert.Equal((byte?)5, record.VmConfidence);
            Assert.Equal((byte?)10, record.ServerConfidence);
            Assert.True(record.InteractiveSession);
            Assert.Equal("12.0.0", record.PackageVersion);
        }
        finally
        {
            Sandbox.Delete(folder);
        }
    }
}
