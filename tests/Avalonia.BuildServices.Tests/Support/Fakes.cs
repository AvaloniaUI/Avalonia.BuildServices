using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Avalonia.Telemetry.Tests;

internal sealed class FakeTaskItem(string itemSpec) : ITaskItem
{
    public string ItemSpec { get; set; } = itemSpec;

    public ICollection MetadataNames => Array.Empty<string>();

    public int MetadataCount => 0;

    public string GetMetadata(string metadataName) => string.Empty;

    public void SetMetadata(string metadataName, string metadataValue)
    {
    }

    public void RemoveMetadata(string metadataName)
    {
    }

    public void CopyMetadataTo(ITaskItem destinationItem)
    {
    }

    public IDictionary CloneCustomMetadata() => new Hashtable();
}

// An item whose every member fails, as one from a broken host might.
internal sealed class ThrowingTaskItem : ITaskItem
{
    public string ItemSpec
    {
        get => throw new InvalidOperationException("ItemSpec");
        set => throw new InvalidOperationException("ItemSpec");
    }

    public ICollection MetadataNames => throw new InvalidOperationException("MetadataNames");

    public int MetadataCount => throw new InvalidOperationException("MetadataCount");

    public string GetMetadata(string metadataName) => throw new InvalidOperationException("GetMetadata");

    public void SetMetadata(string metadataName, string metadataValue) => throw new InvalidOperationException("SetMetadata");

    public void RemoveMetadata(string metadataName) => throw new InvalidOperationException("RemoveMetadata");

    public void CopyMetadataTo(ITaskItem destinationItem) => throw new InvalidOperationException("CopyMetadataTo");

    public IDictionary CloneCustomMetadata() => throw new InvalidOperationException("CloneCustomMetadata");
}

// Keeps what the task logs to the build.
internal sealed class RecordingBuildEngine : IBuildEngine
{
    public List<BuildMessageEventArgs> Messages { get; } = [];

    public List<BuildWarningEventArgs> Warnings { get; } = [];

    public List<BuildErrorEventArgs> Errors { get; } = [];

    public bool ContinueOnError => false;

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => "Test.csproj";

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

    public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
}

// A build engine that fails whatever it is asked: a logger that throws, or a
// host that has already shut the build down.
internal sealed class ThrowingBuildEngine : IBuildEngine
{
    // How many times the task called into it.
    public int Calls { get; private set; }

    public bool ContinueOnError => throw Failure();

    public int LineNumberOfTaskNode => throw Failure();

    public int ColumnNumberOfTaskNode => throw Failure();

    public string ProjectFileOfTaskNode => throw Failure();

    public void LogErrorEvent(BuildErrorEventArgs e) => throw Failure();

    public void LogWarningEvent(BuildWarningEventArgs e) => throw Failure();

    public void LogMessageEvent(BuildMessageEventArgs e) => throw Failure();

    public void LogCustomEvent(CustomBuildEventArgs e) => throw Failure();

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw Failure();

    private InvalidOperationException Failure()
    {
        Calls++;
        return new InvalidOperationException("The build engine failed.");
    }
}
