using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Avalonia.Telemetry;

// The process the build task leaves behind. It sends the spooled builds and
// deletes the ones the server took. One runs per machine at a time (the Signal
// mutex), and it exits once nothing has been sent for IdleLifetime.
//
// The build does not wait for this process, so nothing here can fail a build.
// What it must not do is die holding the mutex, go round for ever on a file it
// can never send, or keep asking a server that is down.
internal sealed class Collector : IDisposable
{
    private const string DestinationUrl = "https://av-build-tel-api-v1.avaloniaui.net/api/usage";

    // Collectors since 0.0.28 take this same mutex, so two never send the same
    // records at once.
    private const string MutexName = "Global\\Avalonia.BuildServices.Signal";

    // The contract's limit on the events in one request.
    private const int MaxBatchSize = 50;

    private static readonly TimeSpan IdleLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(16);
    private static readonly TimeSpan UnreadableFileLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan SpoolFileLifetime = TimeSpan.FromDays(30);

    private readonly HttpClient _httpClient =
        new(new SocketsHttpHandler { ConnectCallback = ConnectAsync }) { Timeout = RequestTimeout };

    // Files this run leaves alone: ones it could not read or could not delete.
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);

    private int _batchLimit = MaxBatchSize;
    private bool _sendFailureLogged;
    private bool _listFailureLogged;

    private enum SendResult
    {
        Sent,
        TooLarge,
        Failed,
    }

    public void Execute()
    {
        DetachConsole();

        // No profile, so no spool: the build task wrote nothing either.
        if (!Common.HasDataFolder)
        {
            return;
        }

        using var mutex = new Mutex(false, MutexName);

        bool owned;

        try
        {
            owned = mutex.WaitOne(50);
        }
        catch (AbandonedMutexException)
        {
            // The last collector died holding it. It is this one's all the same.
            owned = true;
        }

        if (!owned)
        {
            return;
        }

        try
        {
            // Blocks this thread, which is the one that has to release the mutex.
            SweepAndSendAsync().GetAwaiter().GetResult();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void Dispose() => _httpClient.Dispose();

    // The build task has closed its end of these already. Nothing may block or
    // throw by writing to them.
    private static void DetachConsole()
    {
        try
        {
            Console.In.Close();
            Console.Out.Close();
            Console.Error.Close();
            Console.SetIn(TextReader.Null);
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to detach the console.");
        }
    }

    private async Task SweepAndSendAsync()
    {
        DeleteExpiredFiles();

        var idle = Stopwatch.StartNew();
        var retryDelay = TimeSpan.Zero;

        while (idle.Elapsed < IdleLifetime)
        {
            var (events, files) = ReadBatch();
            var delay = SweepInterval;

            if (events.Count > 0)
            {
                switch (await SendAsync(events).ConfigureAwait(false))
                {
                    case SendResult.Sent:
                        Delete(files);
                        idle.Restart();
                        retryDelay = TimeSpan.Zero;
                        _batchLimit = Math.Min(MaxBatchSize, _batchLimit * 2);
                        break;

                    // Over the server's size limit, which no resend can fix. The
                    // batch is halved until the record that is too large stands
                    // alone, and that one is given up.
                    case SendResult.TooLarge when files.Count > 1:
                        _batchLimit = files.Count / 2;
                        break;

                    case SendResult.TooLarge:
                        Logger.LogException(new InvalidDataException(files[0]),
                            "Dropped a telemetry record the server refused as too large.");
                        Delete(files);
                        break;

                    // Anything else may pass: the files stay, and the server is
                    // asked less and less often until this process gives up.
                    default:
                        retryDelay = retryDelay == TimeSpan.Zero
                            ? FirstRetryDelay
                            : TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                        delay = retryDelay;
                        break;
                }
            }

            await Task.Delay(delay).ConfigureAwait(false);
        }
    }

    // This package's folder, then the binary records in the legacy folder.
    // Packages before 11.3 (Avalonia 11.0.0 to 11.3.6) spool there and only
    // their own collector sent those records, so a machine that moved on to a
    // newer Avalonia kept them for good.
    private (List<TelemetryEvent> Events, List<string> Files) ReadBatch()
    {
        var events = new List<TelemetryEvent>();
        var files = new List<string>();

        AddSpoolFiles(Common.AppDataFolder, "*", events, files);

        if (Common.LegacyAppDataFolder is { } legacy && Directory.Exists(legacy))
        {
            AddSpoolFiles(legacy, Common.RecordFilePrefix + "*", events, files);
        }

        return (events, files);
    }

    // Only spool files are read, and so only spool files are ever deleted: the
    // folder also holds the machine id and the logs.
    private void AddSpoolFiles(string folder, string pattern, List<TelemetryEvent> events, List<string> files)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, pattern))
            {
                if (events.Count >= _batchLimit)
                {
                    return;
                }

                if (!Common.IsSpoolFile(path) || _skipped.Contains(path))
                {
                    continue;
                }

                if (TryRead(path) is { } telemetryEvent)
                {
                    events.Add(telemetryEvent);
                    files.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            if (!_listFailureLogged)
            {
                _listFailureLogged = true;
                Logger.LogException(ex, "Failed to list the telemetry spool folder.");
            }
        }
    }

    // A file that cannot be read is left alone for the rest of this run, so it
    // is not read and logged again every sweep. Another package may still be
    // writing it, so it is deleted only once it is too old for that.
    private TelemetryEvent? TryRead(string path)
    {
        try
        {
            return TelemetryEvent.ReadSpoolFile(path);
        }
        catch (Exception ex)
        {
            _skipped.Add(path);

            // Gone already: a collector too old to take the mutex sent it.
            if (ex is not (FileNotFoundException or DirectoryNotFoundException))
            {
                Logger.LogException(ex, "Failed to read telemetry data.");
            }

            if (IsOlderThan(path, UnreadableFileLifetime))
            {
                Delete(path);
            }

            return null;
        }
    }

    // The server keeps no dead letter: the spool holds a build until it is
    // stored. On a machine that can never reach the server that would be every
    // build it ever ran, so a record this package wrote is given up after
    // SpoolFileLifetime. The binary records stay: older packages wrote them,
    // some long ago, and they are still wanted.
    private void DeleteExpiredFiles()
    {
        DeleteOlderThan(Common.SpoolFilePrefix + "*", SpoolFileLifetime);

        // What a build that was killed part-way through a write left behind.
        DeleteOlderThan("*" + Common.TemporaryFileExtension, UnreadableFileLifetime);
    }

    private void DeleteOlderThan(string pattern, TimeSpan age)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(Common.AppDataFolder, pattern))
            {
                if (IsOlderThan(path, age))
                {
                    Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to clear old telemetry data.");
        }
    }

    private static bool IsOlderThan(string path, TimeSpan age)
    {
        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > age;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Delete(List<string> files)
    {
        foreach (var file in files)
        {
            Delete(file);
        }
    }

    // A file that cannot be deleted is not sent again by this run.
    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _skipped.Add(path);
            Logger.LogException(ex, "Failed to delete telemetry data.");
        }
    }

    private async Task<SendResult> SendAsync(IList<TelemetryEvent> events)
    {
        try
        {
            // JSON (build telemetry v3). Reverting this means reverting the package:
            // the binary wire cannot carry the version 3 spool records.
            using var content = new ByteArrayContent(TelemetryJson.Encode(events));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            using var response = await _httpClient.PostAsync(DestinationUrl, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return SendResult.Sent;
            }

            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return SendResult.TooLarge;
            }

            LogSendFailure(new HttpRequestException($"The server answered {(int)response.StatusCode}."));
        }
        catch (Exception ex)
        {
            // No connection or a timeout: the same to the spool as a refusal.
            LogSendFailure(ex);
        }

        return SendResult.Failed;
    }

    // Once per run: a machine that is offline would otherwise fill the log.
    private void LogSendFailure(Exception ex)
    {
        if (_sendFailureLogged)
        {
            return;
        }

        _sendFailureLogged = true;
        Logger.LogException(ex, "Failed to send telemetry data.");
    }

    // IPv4 first, then IPv6. .NET tries a host's addresses one after another,
    // so where IPv6 is advertised but leads nowhere a request can use up its
    // whole timeout before IPv4 gets a turn. A network with no IPv4 at all still
    // has to be reached, so IPv6 is the fallback, and every attempt gets
    // ConnectTimeout so that one dead route cannot use up the request.
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host).ConfigureAwait(false);

        Exception? failure = null;

        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Socket? socket = null;

            try
            {
                // Nagle's algorithm only delays a request this small.
                socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(ConnectTimeout);

                await socket.ConnectAsync(address, endPoint.Port, attempt.Token).ConfigureAwait(false);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket?.Dispose();
                failure = ex;
            }
        }

        throw failure ?? new SocketException((int)SocketError.HostNotFound);
    }
}
