using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Avalonia.Telemetry;

internal static class Logger
{
    private static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private static string _logFilePath;

    /// <summary>
    /// Configures the file path that <see cref="LogException"/> writes to.
    /// Each consumer (build task, collector, ...) sets its own log file name
    /// before calling <see cref="LogException"/>.
    /// </summary>
    internal static void Configure(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    internal static void LogException(Exception exception, string message)
    {
        if (string.IsNullOrEmpty(_logFilePath))
        {
            return;
        }

        _ = AppendLine(
            _logFilePath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message} - {exception.GetType().Name}: {exception.Message}");
    }

    private static bool AppendLine(string path, string line)
    {
        // Reset log file if it exceeds the max size
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxFileSizeBytes)
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore
        }

        var written = false;
        var retryDelay = TimeSpan.FromMilliseconds(50);
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts && !written; attempt++)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs, Encoding))
                {
                    writer.WriteLine(line);
                }

                written = true;
            }
            catch (IOException)
            {
                // File is being used by another process; wait and retry
                if (attempt == maxAttempts)
                {
                    break;
                }
                Thread.Sleep(retryDelay);
            }
        }

        return written;
    }
}
