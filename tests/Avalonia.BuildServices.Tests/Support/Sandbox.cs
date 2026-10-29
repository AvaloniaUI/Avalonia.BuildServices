using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

// The task keeps its folders, its notice timer and its caches in statics, and
// some tests change environment variables. So one test runs at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Avalonia.Telemetry.Tests;

// Where the task's files go while the tests run. Never the real profile: a
// test that wrote there would change this machine's identifier, and a record
// left in the real spool would be sent to the production server by the next
// real build.
internal static class Sandbox
{
    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "Avalonia.BuildServices.Tests", Guid.NewGuid().ToString("N"));

    // Runs when this assembly loads, before any test: a test that forgets to
    // take a DataFolderScope still lands under Root.
    [ModuleInitializer]
    internal static void Initialize()
    {
        Reset();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Delete(Root);
    }

    // Points the task at a folder no test looks in.
    public static void Reset() =>
        Common.UseFolders(Path.Combine(Root, "idle", "data"), Path.Combine(Root, "idle", "legacy"));

    public static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Left for the system to clear with the rest of the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
