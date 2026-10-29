using System;
using System.IO;
using System.Threading;

namespace Avalonia.Telemetry.Tests;

// One test's folders, in place of the profile's for as long as the test runs.
internal sealed class DataFolderScope : IDisposable
{
    private int _collectorStarts;

    private DataFolderScope(bool hasProfile)
    {
        Root = Path.Combine(Sandbox.Root, Guid.NewGuid().ToString("N"));
        Data = Path.Combine(Root, "data");
        Legacy = Path.Combine(Root, "legacy");
        Tickets = Path.Combine(Root, "tickets");

        Directory.CreateDirectory(Tickets);

        Common.UseFolders(hasProfile ? Data : null, hasProfile ? Legacy : null);
    }

    public string Root { get; }

    // Not created: the first build on a machine finds no folder either.
    public string Data { get; }

    public string Legacy { get; }

    // An empty licence store, so that a test never reads this machine's own.
    public string Tickets { get; }

    // How many times a task from NewTask asked for the collector.
    public int CollectorStarts => Volatile.Read(ref _collectorStarts);

    public static DataFolderScope Create() => new(hasProfile: true);

    // An account with nowhere to keep per-user data.
    public static DataFolderScope WithoutProfile() => new(hasProfile: false);

    // The data folder's path is taken by a plain file.
    public static DataFolderScope WithFileInPlaceOfDataFolder()
    {
        var scope = new DataFolderScope(hasProfile: true);
        File.WriteAllText(scope.Data, "not a folder");
        return scope;
    }

    // A task that reads no real licence and starts no real collector: the real
    // one would send what it finds to the production server.
    public AvaloniaStatsTask NewTask() => new()
    {
        StartCollector = () => Interlocked.Increment(ref _collectorStarts),
        TicketsPath = Tickets,
    };

    public string[] SpoolFiles() => Files(Common.SpoolFilePrefix + "*");

    public string[] TemporaryFiles() => Files("*" + Common.TemporaryFileExtension);

    public string[] Files(string pattern = "*") =>
        Directory.Exists(Data) ? Directory.GetFiles(Data, pattern) : [];

    // What the task logged, or nothing.
    public string Log() =>
        File.Exists(Path.Combine(Data, "buildtasks.log")) ? File.ReadAllText(Path.Combine(Data, "buildtasks.log")) : string.Empty;

    public void Dispose()
    {
        Sandbox.Reset();
        Sandbox.Delete(Root);
    }
}
