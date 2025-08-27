using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Avalonia.Telemetry;

public enum Ide
{
    Unknown,
    Vs,
    Vs4Mac,
    Rider,
    Cli,
    VsCode
}

public enum CiProvider
{
    None,
    Bamboo,
    Bitrise,
    SpaceAutomation,
    Jenkins,
    AppVeyor,
    GitLab,
    BitBucket,
    Travis,
    TeamCity,
    GitHubActions,
    AzurePipelines
}


public class TelemetryPayload
{
    private TelemetryPayload()
    {
        
    }
    
    public static readonly ushort Version = 2;
    
    public Guid RecordId { get; private set; }
    
    public DateTimeOffset TimeStamp { get; private set; }
    
    public Guid Machine { get; private set; }
    
    public string ProjectRootHash { get; private set; }
    
    public string ProjectHash { get; private  set; }
    
    public Ide Ide { get; private set; }
    
    public CiProvider CiProvider { get; private set; }
    
    public string OutputType { get; set; }
    
    public string Tfm { get; private set; }
    
    public string Rid { get; private set; }
    
    public string AvaloniaMainPackageVersion { get; private set; }
    
    public string OSDescription { get; private set; }
    
    public Architecture ProcessorArchitecture { get; private set; }

    public static byte[] EncodeMany(IList<TelemetryPayload> payloads)
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

    public byte[] Encode()
    {
        using var m = new MemoryStream();
        using var writer = new BinaryWriter(m);
        writer.Write(Version);
        writer.Write(RecordId.ToByteArray());
        writer.Write(TimeStamp.ToUnixTimeMilliseconds());
        writer.Write(Machine.ToByteArray());
        writer.Write(ProjectRootHash);
        writer.Write(ProjectHash);
        writer.Write(string.Empty);
        writer.Write(string.Empty);
        writer.Write(string.Empty);
        writer.Write((byte)Ide);
        writer.Write((byte)CiProvider);
        writer.Write(OutputType ?? string.Empty);
        writer.Write(Tfm ?? string.Empty);
        writer.Write(Rid ?? string.Empty);
        writer.Write(AvaloniaMainPackageVersion  ?? string.Empty);
        writer.Write(OSDescription);
        writer.Write((byte)ProcessorArchitecture);
        return m.ToArray();
    }

    public static TelemetryPayload FromBinaryReader(BinaryReader reader)
    {
        var result = new TelemetryPayload();
        var version = reader.ReadInt16();

        if (version == Version)
        {
            result.RecordId = new Guid(reader.ReadBytes(16));
            result.TimeStamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            result.Machine = new Guid(reader.ReadBytes(16));
            result.ProjectRootHash = reader.ReadString();
            result.ProjectHash = reader.ReadString();
            var padding= reader.ReadString();
            padding = reader.ReadString();
            padding = reader.ReadString();
            result.Ide = (Ide)reader.ReadByte();
            result.CiProvider = (CiProvider)reader.ReadByte();
            result.OutputType = reader.ReadString();
            result.Tfm = reader.ReadString();
            result.Rid = reader.ReadString();
            result.AvaloniaMainPackageVersion = reader.ReadString();
            result.OSDescription = reader.ReadString();
            result.ProcessorArchitecture = (Architecture)reader.ReadByte();
        }
        else
        {
            // Unsupported.
        }

        return result;
    }

    public static TelemetryPayload FromByteArray(byte[] data) 
    {
        var result = new TelemetryPayload();
        using var m = new MemoryStream(data);
        using var reader = new BinaryReader(m);

        result = FromBinaryReader(reader);
        
        return result;
    }

    public static IList<TelemetryPayload> ManyFromByteArray(byte[] data)
    {
        using var m = new MemoryStream(data);
        using var reader = new BinaryReader(m);

        var result = new List<TelemetryPayload>();
        
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

    public static TelemetryPayload Initialise(Guid machine, string projectName, string tfm, string rid, string avaloniaVersion, string outputType)
    {
        var result = new TelemetryPayload();
        
        result.RecordId = Guid.NewGuid();
        result.TimeStamp = DateTimeOffset.UtcNow;
        result.Machine = machine;
        result.Ide = TryDetectIde();
        result.CiProvider = DetectCiProvider();
        result.OutputType = outputType;
        result.Tfm = tfm;
        result.Rid = rid;
        result.AvaloniaMainPackageVersion = avaloniaVersion;
        result.OSDescription = RuntimeInformation.OSDescription;
        result.ProcessorArchitecture = RuntimeInformation.ProcessArchitecture;
        result.ProjectRootHash = HashProperty(projectName?.Split('.').FirstOrDefault() ?? "");
        result.ProjectHash = HashProperty(projectName);

        return result;
    }
    
    internal static string HashProperty(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        using var sha = SHA256.Create();
        byte[] textData = Encoding.UTF8.GetBytes(value);
        byte[] hash = sha.ComputeHash(textData);
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }
    
    private static Ide TryDetectIde()
    {
        var environment = Environment.GetEnvironmentVariables();
        
        var ide = Ide.Unknown;

        if (environment.Contains("VSMAC_MSBUILD_BUILDER_SETTINGS_FILE"))
        {
            ide = Ide.Vs4Mac;
        }
        else if (environment.Contains("VisualStudioVersion"))
        {
            ide = Ide.Vs;
        }
        else if (environment.Contains("VSCODE_CWD") || 
                 environment.Contains("VSCODE_PID") ||
                 (environment.Contains("TERM_PROGRAM") && environment["TERM_PROGRAM"].ToString() == "vscode"))
        {
            ide = Ide.VsCode;
        }
        else if (environment.Contains("IDEA_INITIAL_DIRECTORY"))
        {
            ide = Ide.Rider;
        }
        else if (environment.Contains("RESHARPER_FUS_BUILD"))
        {
            ide = Ide.Rider;
        }
        else if (environment.Contains("PWD") && environment["PWD"].ToString().Contains("Rider"))
        {
            ide = Ide.Rider;
        }
        else
        {
            ide = Ide.Cli;
        }

        return ide;
    }

    public static CiProvider DetectCiProvider()
    {
        var environment = Environment.GetEnvironmentVariables();

        if (environment.Contains("bamboo_planKey"))
        {
            return CiProvider.Bamboo;
        }

        if (environment.Contains("BITRISE_BUILD_URL"))
        {
            return CiProvider.Bitrise;
        }
        
        if (environment.Contains("JB_SPACE_PROJECT_KEY"))
        {
            return CiProvider.SpaceAutomation;
        }
        
        if (environment.Contains("JENKINS_HOME"))
        {
            return CiProvider.Jenkins;
        }
        
        if (environment.Contains("APPVEYOR"))
        {
            return CiProvider.AppVeyor;
        }
        
        if (environment.Contains("GITLAB_CI"))
        {
            return CiProvider.GitLab;
        }
        
        if (environment.Contains("BITBUCKET_PIPELINE_UUID"))
        {
            return CiProvider.BitBucket;
        }
        
        if (environment.Contains("TRAVIS"))
        {
            return CiProvider.Travis;
        }
        
        if (environment.Contains("TEAMCITY_VERSION"))
        {
            return CiProvider.TeamCity;
        }
        
        if (environment.Contains("GITHUB_ACTIONS"))
        {
            return CiProvider.GitHubActions;
        }
        
        if (environment.Contains("TF_BUILD"))
        {
            return CiProvider.AzurePipelines;
        }
        

        return CiProvider.None;
    }
}
