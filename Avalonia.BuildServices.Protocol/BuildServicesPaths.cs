using System;
using System.IO;

namespace Avalonia.BuildServices.Protocol;

public static class BuildServicesPaths
{
    private static readonly string LegacyAppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".avalonia-build-tasks");

    private static string? _appDataFolder;

    public const string RECORD_FILE_PREFIX = "avalonia_build";

    public static string IdPath => Path.Combine(AppDataFolder, "id");

    public static string AppDataFolder
    {
        get
        {
            if (_appDataFolder is null)
            {
                _appDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "AvaloniaUI", "BuildServices");

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
                        else if (field is not null && File.Exists(field) && !File.Exists(IdPath))
                        {
                            File.Copy(field, IdPath);
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
    } = Path.Combine(LegacyAppDataFolder, "id");
}
