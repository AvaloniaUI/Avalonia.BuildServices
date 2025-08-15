using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Avalonia.Telemetry;

public class Collector
{
    private readonly HttpClient _httpClient;
    private const string DESTINATION_URL = "https://av-build-tel-api-v1.avaloniaui.net/api/usage";

    private static Stopwatch _stopwatch = new();
    
    public Collector()
    {
        AppContext.SetSwitch("System.Net.DisableIPv6", true);
        _httpClient = new HttpClient (new SocketsHttpHandler()
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                // Use DNS to look up the IP addresses of the target host:
                // - IP v4: AddressFamily.InterNetwork
                // - IP v6: AddressFamily.InterNetworkV6
                // - IP v4 or IP v6: AddressFamily.Unspecified
                // note: this method throws a SocketException when there is no IP address for the host
                var entry = await Dns.GetHostEntryAsync(context.DnsEndPoint.Host);

                // Open the connection to the target host/port
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                // Turn off Nagle's algorithm since it degrades performance in most HttpClient scenarios.
                socket.NoDelay = true;

                try
                {
                    await socket.ConnectAsync(entry.AddressList.Where(x=>x.AddressFamily == AddressFamily.InterNetwork).ToArray(), context.DnsEndPoint.Port, cancellationToken);

                    // If you want to choose a specific IP address to connect to the server
                    // await socket.ConnectAsync(
                    //    entry.AddressList[Random.Shared.Next(0, entry.AddressList.Length)],
                    //    context.DnsEndPoint.Port, cancellationToken);

                    // Return the NetworkStream to the caller
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        }) { Timeout = TimeSpan.FromMilliseconds(10000) };
    }

    class NullWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
    
    public void Execute()
    {
        Console.In.Close();
        Console.Out.Close();
        Console.Error.Close();
        Console.SetIn(new StringReader(""));
        Console.SetOut(new NullWriter());
        Console.SetError(new NullWriter());
        
        var mtx = new Mutex(false, "Global\\Avalonia.BuildServices.Signal");

        if (mtx.WaitOne(50))
        {
            SweepAndSendAsync().Wait();

            mtx.ReleaseMutex();
        }
    }

    private async Task SweepAndSendAsync()
    {
        _stopwatch.Restart();

        while (true)
        {
            var payloads = new List<TelemetryPayload>();

            foreach (var dataFile in Directory.EnumerateFiles(Common.AppDataFolder).ToList())
            {
                if (Path.GetFileName(dataFile).StartsWith(Common.RECORD_FILE_PREFIX))
                {
                    try
                    {
                        var data = File.ReadAllBytes(dataFile);

                        var payload = TelemetryPayload.FromByteArray(data);

                        payloads.Add(payload);
                    }
                    catch (Exception)
                    {
                    }

                    if (payloads.Count == 50)
                    {
                        break;
                    }
                }
            }
            
            if (payloads.Count > 0)
            {
                bool sent = false;
                        
                try
                {
                    sent = await SendAsync(payloads);
                }
                catch (Exception)
                {
                            
                }

                if(sent)
                {
                    _stopwatch.Restart();

                    foreach (var payload in payloads)
                    {
                        var file = Path.Combine(Common.AppDataFolder, Common.RECORD_FILE_PREFIX + payload.RecordId);
                        
                        File.Delete(file);
                    }
                }
            }

            if (_stopwatch.Elapsed > TimeSpan.FromSeconds(30))
            {
                return;
            }

            await Task.Delay(100);
        }
    }
    
    private async Task<bool> SendAsync(IList<TelemetryPayload> payloads)
    {
        if (payloads.Count < 1)
        {
            return true;
        }

        var content = new ByteArrayContent(TelemetryPayload.EncodeMany(payloads));

        try
        {
            var response = await _httpClient.PostAsync(DESTINATION_URL, content);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
        }

        return false;
    }
}