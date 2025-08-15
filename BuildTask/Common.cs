using System;
using System.IO;

namespace Avalonia.Telemetry;

static class Common
{
    public const string RECORD_FILE_PREFIX = "avalonia_build";
    internal static readonly string AppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".avalonia-build-tasks");

    internal static readonly string IdPath = Path.Combine(AppDataFolder, "id");
}