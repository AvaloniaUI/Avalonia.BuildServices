using System;
using System.Collections.Generic;
using System.IO;

namespace Avalonia.Telemetry;

/// <summary>
/// Avalonia IDE loaded event telemetry payload. 
/// </summary>
public class IdeLoadedAnalyticsPayload
{
    public static readonly ushort PayloadVersion = 1;

    private IdeLoadedAnalyticsPayload()
    {
    }

    public Guid RecordId { get; private set; }

    public DateTimeOffset TimeStamp { get; private set; }

    public Guid Machine { get; private set; }

    public Ide Ide { get; private set; }

    public string Edition { get; private set; }

    public string Version { get; private set; }

    public string ExtensionVersion { get; private set; }

    public int DesignerLaunchCount { get; private set; }

    public AccelerateTier AccelerateTier { get; private set; }

    public string Continent { get; set; }

    public string Country { get; set; }

    public string City { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string IpAddress { get; set; }

    public static IdeLoadedAnalyticsPayload Initialise(Guid machine, string vsEdition, string vsVersion)
    {
        var result = new IdeLoadedAnalyticsPayload();
        result.RecordId = Guid.NewGuid();
        result.TimeStamp = DateTimeOffset.UtcNow;
        result.Machine = machine;
        result.Ide = Ide.Vs;
        result.Version = vsVersion;
        result.Edition = vsEdition;            
        return result;
    }

    public byte[] Encode()
    {
        using var m = new MemoryStream();
        using var writer = new BinaryWriter(m);
        writer.Write(PayloadVersion);
        writer.Write(RecordId.ToByteArray());
        writer.Write(TimeStamp.ToUnixTimeMilliseconds());
        writer.Write(Machine.ToByteArray());
        writer.Write((byte)Ide);
        writer.Write(Version ?? string.Empty);
        writer.Write(Edition ?? string.Empty);
        return m.ToArray();
    }

    public static byte[] EncodeMany(IList<IdeLoadedAnalyticsPayload> payloads)
    {
        if (payloads.Count > 0)
        {
            if (payloads.Count > 50)
            {
                throw new Exception("No more than 50 in a single packet.");
            }

            using var m = new MemoryStream();
            using var writer = new BinaryWriter(m);

            writer.Write(payloads.Count);

            foreach (var payload in payloads)
            {
                writer.Write(payload.Encode());
            }

            return m.ToArray();
        }
        else
        {
            return Array.Empty<byte>();
        }
    }
    
    public static IdeLoadedAnalyticsPayload FromByteArray(byte[] data)
    {
        var result = new IdeLoadedAnalyticsPayload();
        using var m = new MemoryStream(data);
        using var reader = new BinaryReader(m);

        result = FromBinaryReader(reader);

        return result;
    }
    public static IdeLoadedAnalyticsPayload FromBinaryReader(BinaryReader reader)
    {
        var result = new IdeLoadedAnalyticsPayload();
        var version = reader.ReadInt16();
        if (version == PayloadVersion)
        {
            result.RecordId = new Guid(reader.ReadBytes(16));
            result.TimeStamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            result.Machine = new Guid(reader.ReadBytes(16));
            result.Ide = (Ide)reader.ReadByte();
            result.Version = reader.ReadString();
            result.Edition = reader.ReadString();
            result.ExtensionVersion = reader.ReadString();
            result.DesignerLaunchCount = reader.ReadInt32();
            result.AccelerateTier = (AccelerateTier)reader.ReadByte();
        }
        return result;
    }        
    public static IList<IdeLoadedAnalyticsPayload> ManyFromByteArray(byte[] data)
    {
        using var m = new MemoryStream(data);
        using var reader = new BinaryReader(m);

        var result = new List<IdeLoadedAnalyticsPayload>();
        
        var count = reader.ReadInt32();

        if (count > 50)
        {
            throw new Exception("Unexpected number of payloads, 50 is the maximum");
        }

        for (int i = 0; i < count; i++)
        {
            result.Add(FromBinaryReader(reader));
        }

        return result;
    }
}