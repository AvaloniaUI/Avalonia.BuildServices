using System;
using System.IO;

namespace Avalonia.Telemetry;

internal static class Common
{
    private static readonly string LegacyAppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".avalonia-build-tasks");
    private static readonly string LegacyIdPath = Path.Combine(LegacyAppDataFolder, "id");

    private static string? _appDataFolder;

    public const string RECORD_FILE_PREFIX = "avalonia_build";

    internal static string IdPath => Path.Combine(AppDataFolder, "id");

    internal static string AppDataFolder
    {
        get
        {
            if (_appDataFolder is null)
            {
                _appDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvaloniaUI", "BuildServices");

                // Migrate legacy data if exists.
                if (Directory.Exists(LegacyAppDataFolder))
                {
                    try
                    {
                        // If we have no new folder - just move it.
                        if (!Directory.Exists(_appDataFolder))
                        {
                            // Ensure parent directory exists before moving
                            var appDataParent = Path.GetDirectoryName(_appDataFolder);
                            if (!string.IsNullOrEmpty(appDataParent) && !Directory.Exists(appDataParent))
                            {
                                Directory.CreateDirectory(appDataParent);
                            }
                            Directory.Move(LegacyAppDataFolder, _appDataFolder);
                        }
                        // If we have both - copy id and delete old folder.
                        else if (File.Exists(LegacyIdPath) && !File.Exists(IdPath))
                        {
                            File.Copy(LegacyIdPath, IdPath);
                            Directory.Delete(LegacyAppDataFolder, true);
                        }
                        // If we have both and both have id - just delete old folder.
                        else
                        {
                            Directory.Delete(LegacyAppDataFolder, true);
                        }
                    }
                    catch
                    {
                        // Ignore any issues with migration.
                        // If we are lucky - it will succeed next time.
                    }
                }
            }

            return _appDataFolder;
        }
    }
}