using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Avalonia.Telemetry;

internal static class Logger
{
    private static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public static void LogException(Exception exception, string message)
    {
        _ = AppendLine(
            Common.ExceptionsLogPath,
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