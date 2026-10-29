using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Avalonia.Telemetry;

// Starts the collector: the separate process, shipped beside this assembly,
// that sends what the task spooled. It is detached from the build in every
// way that could hold the build up or outlast it visibly: no inherited
// handles, no console, and a working directory that is not the project's.
internal static class CollectorLauncher
{
    private const string CollectorAssembly = "Avalonia.BuildServices.Collector.dll";
    private const string RuntimeConfig = "runtimeconfig.json";

    // A collector lives for at least 30 seconds and sweeps the spool folder
    // ten times a second, so one started this recently is still there to send
    // the record just written. Without this a solution build starts one per
    // project and target framework, each only to find the first one running.
    private static readonly long RelaunchInterval = Stopwatch.Frequency * 10;

    private static long _lastStart;

    public static void Start()
    {
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastStart);
        if (last != 0 && now - last < RelaunchInterval)
            return;

        var directory = Path.GetDirectoryName(typeof(CollectorLauncher).Assembly.Location);
        if (directory is null || directory.Length == 0)
            throw new FileNotFoundException("The task assembly has no location on disk.");

        var collector = Path.Combine(directory, CollectorAssembly);
        var runtimeConfig = Path.Combine(directory, RuntimeConfig);
        if (!File.Exists(collector) || !File.Exists(runtimeConfig))
            throw new FileNotFoundException("The collector is missing from the package.", collector);

        var host = DotnetHost();
        var arguments = $"exec --runtimeconfig {Quote(runtimeConfig)} {Quote(collector)}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WinProcUtil.StartBackground($"{Quote(host)} {arguments}", directory);
        }
        else
        {
            // Redirected and closed at once, so the collector holds neither the
            // build's terminal nor a pipe someone is waiting on.
            var startInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                FileName = host,
                Arguments = arguments,
                WorkingDirectory = directory
            };

            // Disposing releases this process's handle on the child. The child runs on.
            using var process = Process.Start(startInfo);
            process?.StandardError.Close();
            process?.StandardInput.Close();
            process?.StandardOutput.Close();
        }

        Interlocked.Exchange(ref _lastStart, now);
    }

    // The dotnet that is running this build, when the SDK says which one that
    // is, otherwise the first one on the PATH. The PATH one can be missing or
    // a different installation, in Visual Studio above all.
    private static string DotnetHost()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return host is { Length: > 0 } && File.Exists(host) ? host : "dotnet";
    }

    // Quotes one command line argument the way both CreateProcess callers and
    // .NET's own argument splitter read it back: a path with spaces stays one
    // argument, and a quote or a backslash before a quote survives.
    internal static string Quote(string argument)
    {
        var quoted = new StringBuilder(argument.Length + 2).Append('"');

        for (var i = 0; i < argument.Length; i++)
        {
            var c = argument[i];
            if (c == '\\')
            {
                var run = 1;
                while (i + 1 < argument.Length && argument[i + 1] == '\\')
                {
                    i++;
                    run++;
                }

                // Backslashes are literal unless a quote follows them, which
                // here is either a quote in the argument or the closing one.
                var beforeQuote = i + 1 == argument.Length || argument[i + 1] == '"';
                quoted.Append('\\', beforeQuote ? run * 2 : run);
            }
            else if (c == '"')
            {
                quoted.Append('\\').Append('"');
            }
            else
            {
                quoted.Append(c);
            }
        }

        return quoted.Append('"').ToString();
    }
}
