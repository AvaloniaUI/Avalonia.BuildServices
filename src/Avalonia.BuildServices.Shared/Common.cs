using System;
using System.IO;

namespace Avalonia.Telemetry;

internal static class Common
{
    // Not readonly: a test puts its own folders here (UseFolders).
    private static Lazy<string?> _dataFolder = new(ResolveDataFolder);

    private static Lazy<string?> _legacyDataFolder = new(() =>
        ProfileFolder(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.None) is { } roaming
            ? Path.Combine(roaming, ".avalonia-build-tasks")
            : null);

    // Puts a test's folders in place of the profile's, so that a test never
    // reads or writes the real ones. Null stands for an account without a
    // profile. Nothing in the package calls this.
    internal static void UseFolders(string? dataFolder, string? legacyDataFolder)
    {
        _dataFolder = new Lazy<string?>(() => dataFolder);
        _legacyDataFolder = new Lazy<string?>(() => legacyDataFolder);
    }

    // Binary spool records written by packages before version 3. Their
    // collectors sweep every file with this prefix, so a newer record must never
    // use it: they cannot read it and would fail the whole batch it is in.
    public const string RecordFilePrefix = "avalonia_build";

    // JSON spool records (TelemetryEvent), from version 3 on.
    public const string SpoolFilePrefix = "avalonia_telemetry_";

    // A file the task is still writing. It takes its real name only when it is
    // whole (AtomicFile), and until then its name is no spool file's.
    public const string TemporaryFileExtension = ".tmp";

    internal static bool IsSpoolFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(SpoolFilePrefix, StringComparison.Ordinal) ||
               name.StartsWith(RecordFilePrefix, StringComparison.Ordinal);
    }

    // False for an account with nowhere to keep per-user data: a service whose
    // profile is not loaded, a container user without a home directory. There
    // is then no id, no spool and no log, so no telemetry. Check this before
    // using any of the paths below.
    //
    // Decided, not an oversight: those builds go uncounted. GetFolderPath
    // answers "" for such an account, and before this check Path.Combine made
    // that a path relative to the build's current directory, the customer's
    // project, so every project folder got its own id and spool. The temp
    // folder was turned down as a fallback: it is shared between users,
    // cleaned at reboot, and for a Windows service it is C:\Windows\Temp, so
    // the id would churn and the records would count a stream of new machines
    // from files the customer never expected there. README, "Privacy & Security".
    internal static bool HasDataFolder => _dataFolder.Value is not null;

    // Where the id, the spool files and the logs live. Throws when
    // HasDataFolder is false, so that a caller who did not check fails inside
    // its own try block and writes nothing anywhere.
    internal static string AppDataFolder =>
        _dataFolder.Value ?? throw new DirectoryNotFoundException("This account has no folder for application data.");

    // The folder packages before 11.3 use, or null when the account has none.
    internal static string? LegacyAppDataFolder => _legacyDataFolder.Value;

    internal static string IdPath => Path.Combine(AppDataFolder, "id");

    // "<vm> <server> <interactive> <checksum>", detected once per id (see
    // AvaloniaStatsTask.GetMachineKind). Kept apart from the id: packages
    // before version 3 read the id file as exactly 16 bytes and fail on
    // anything longer.
    internal static string MachineKindPath => Path.Combine(AppDataFolder, "machine");
    internal static string ExceptionsLogPath => Path.Combine(AppDataFolder,
#if AVALONIAUI_COLLECTOR
        "collector.log");
#else
        "buildtasks.log");
#endif

    private static string? ResolveDataFolder()
    {
        var local = ProfileFolder(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (local is null)
            return null;

        var folder = Path.Combine(local, "AvaloniaUI", "BuildServices");

        // Packages before 11.3 (Avalonia 11.0.0 to 11.3.6 pin them) use only
        // the legacy folder, and mint a new id whenever its id file is
        // missing. So the legacy folder is never moved or deleted: doing so
        // gave their builds a new machine identifier every time a newer
        // package ran, and dropped records their collector had not sent.
        // A new folder without an id takes the legacy one, so both report
        // the same machine.
        try
        {
            var id = Path.Combine(folder, "id");
            if (LegacyAppDataFolder is { } legacy && !File.Exists(id))
            {
                var legacyId = Path.Combine(legacy, "id");
                if (File.Exists(legacyId))
                {
                    Directory.CreateDirectory(folder);

                    // Under another name first: a copy that is cut short must
                    // never stand as this machine's id.
                    var temporary = Path.Combine(folder, "id_" + Guid.NewGuid().ToString("N") + TemporaryFileExtension);
                    try
                    {
                        File.Copy(legacyId, temporary);
                        File.Move(temporary, id);
                    }
                    finally
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }
        catch
        {
            // A parallel build copied it first, or the folder cannot be
            // written; the task mints an id as before.
        }

        return folder;
    }

    // A profile folder as a full path, or null. GetFolderPath answers "" for an
    // account without one, and Path.Combine would turn that into a path
    // relative to the current directory, which during a build is the
    // customer's project.
    private static string? ProfileFolder(Environment.SpecialFolder folder, Environment.SpecialFolderOption option)
    {
        try
        {
            var path = Environment.GetFolderPath(folder, option);
            return path.Length > 0 && Path.IsPathRooted(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
