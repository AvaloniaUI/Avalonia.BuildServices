using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Xunit;

namespace Avalonia.Telemetry.Tests;

// The task runs inside every customer's build. Whatever it is given and
// whatever state it finds, Execute must come back with true and must not throw:
// either would fail the build.
//
// Execute swallows every failure, so "true" alone proves nothing. Each test
// also checks how far the run got: a record that reads back, a line in the
// task's log, or a folder left untouched.
public class ExecuteNeverFailsTests
{
    [Fact]
    public void Runs_with_no_parameters_and_no_build_engine()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();

        Assert.True(folders.NewTask().Execute());

        var record = ReadOnlyRecord(folders);
        Assert.True(Guid.TryParse(record.RecordId, out _));
        Assert.Equal(string.Empty, folders.Log());
    }

    [Fact]
    public void Runs_with_values_no_build_would_send()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();

        var odd = "\0 \r\n\t \"quoted\" <xml> {json} \\ / : * ? | 😀 \ud800 " + new string('x', 70_000);

        var task = folders.NewTask();
        task.BuildEngine = new RecordingBuildEngine();
        task.ProjectName = odd;
        task.TargetFramework = odd;
        task.RuntimeIdentifier = odd;
        task.AvaloniaPackageVersion = odd;
        task.OutputType = odd;
        task.BuildingInsideVisualStudio = odd;
        task.ContinuousIntegrationBuild = odd;
        task.ProjectLicenseTier = odd;
        task.XpfVersion = odd;
        task.PublishAot = odd;
        task.PublishTrimmed = odd;
        task.TrimMode = odd;
        task.RunAOTCompilation = odd;
        task.AndroidEnableProfiledAot = odd;
        task.TargetPlatformIdentifier = odd;
        task.ProjectExtension = odd;
        task.Configuration = odd;
        task.IsPublishing = odd;
        task.UseCompiledBindingsByDefault = odd;
        task.EnableXamlCompilation = odd;
        task.NameGeneratorEnabled = odd;
        task.ProjectLicenseFeatures = [new FakeTaskItem(odd), new FakeTaskItem(string.Empty)];
        task.LicensedProducts = [new FakeTaskItem(odd), new FakeTaskItem(string.Empty)];

        Assert.True(task.Execute());

        // The build went on and the record was still kept.
        ReadOnlyRecord(folders);
    }

    [Fact]
    public void Runs_when_the_build_engine_throws()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();

        var engine = new ThrowingBuildEngine();
        var task = folders.NewTask();
        task.BuildEngine = engine;

        // A Community licence has the task say that telemetry is required, so
        // it does call the engine.
        task.ProjectLicenseTier = "Community";

        Assert.True(task.Execute());

        Assert.True(engine.Calls > 0);
        ReadOnlyRecord(folders);
    }

    [Fact]
    public void Runs_when_a_licence_feature_item_throws()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        Directory.CreateDirectory(folders.Data);

        var task = folders.NewTask();
        task.ProjectLicenseFeatures = [new ThrowingTaskItem()];

        Assert.True(task.Execute());

        Assert.Contains("Telemetry task failed.", folders.Log());
        Assert.Empty(folders.SpoolFiles());
        Assert.Equal(0, folders.CollectorStarts);
    }

    [Fact]
    public void Runs_when_a_licensed_product_item_throws()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        Directory.CreateDirectory(folders.Data);

        var task = folders.NewTask();
        task.LicensedProducts = [new ThrowingTaskItem()];

        Assert.True(task.Execute());

        Assert.Contains("Failed to collect telemetry data.", folders.Log());
        Assert.Empty(folders.SpoolFiles());
        Assert.Equal(0, folders.CollectorStarts);
    }

    [Fact]
    public void Runs_when_the_collector_cannot_be_started()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();

        var task = folders.NewTask();
        task.StartCollector = () => throw new InvalidOperationException("No collector.");

        Assert.True(task.Execute());

        // The record waits for the next build's collector.
        ReadOnlyRecord(folders);
        Assert.Contains("Failed to start telemetry collector.", folders.Log());
    }

    [Fact]
    public void Runs_when_the_account_has_no_profile()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.WithoutProfile();

        Assert.True(folders.NewTask().Execute());

        Assert.False(Directory.Exists(folders.Data));
        Assert.Equal(0, folders.CollectorStarts);
    }

    [Fact]
    public void Runs_when_a_file_stands_where_the_data_folder_should_be()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.WithFileInPlaceOfDataFolder();

        Assert.True(folders.NewTask().Execute());

        Assert.Equal("not a folder", File.ReadAllText(folders.Data));
        Assert.Equal(0, folders.CollectorStarts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(4096)]
    public void Replaces_a_machine_identifier_of_the_wrong_length(int length)
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        Directory.CreateDirectory(folders.Data);
        File.WriteAllBytes(Path.Combine(folders.Data, "id"), Enumerable.Repeat((byte)7, length).ToArray());

        Assert.True(folders.NewTask().Execute());

        // Exactly 16 bytes: older packages fail on anything else.
        Assert.Equal(16, new FileInfo(Path.Combine(folders.Data, "id")).Length);
        ReadOnlyRecord(folders);
    }

    [Fact]
    public void Replaces_an_all_zero_machine_identifier()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        Directory.CreateDirectory(folders.Data);
        File.WriteAllBytes(Path.Combine(folders.Data, "id"), new byte[16]);

        Assert.True(folders.NewTask().Execute());

        Assert.NotEqual(Guid.Empty, new Guid(File.ReadAllBytes(Path.Combine(folders.Data, "id"))));
        ReadOnlyRecord(folders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("10 20 1 0000000000000000")]
    [InlineData("999 20 1 x")]
    [InlineData("10 20 1 x extra parts here")]
    public void Replaces_a_machine_kind_file_it_cannot_trust(string content)
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        Directory.CreateDirectory(folders.Data);
        var path = Path.Combine(folders.Data, "machine");
        File.WriteAllText(path, content);

        Assert.True(folders.NewTask().Execute());

        var stored = File.ReadAllText(path);
        Assert.NotEqual(content, stored);
        Assert.Equal(4, stored.Split(' ').Length);
        ReadOnlyRecord(folders);
    }

    [Fact]
    public void Keeps_the_same_machine_identifier_from_build_to_build()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();

        Assert.True(folders.NewTask().Execute());
        Assert.True(folders.NewTask().Execute());

        var machines = folders.SpoolFiles().Select(f => TelemetryEvent.ReadSpoolFile(f).MachineId).Distinct();
        Assert.Single(machines);
    }

    [Fact]
    public void Runs_from_several_threads_at_once()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();

        const int builds = 16;
        var results = new bool[builds];

        Parallel.For(0, builds, i => results[i] = folders.NewTask().Execute());

        Assert.All(results, Assert.True);

        // One record each, every one whole, all from one machine.
        var records = folders.SpoolFiles().Select(TelemetryEvent.ReadSpoolFile).ToList();
        Assert.Equal(builds, records.Count);
        Assert.Single(records.Select(r => r.MachineId).Distinct());
        Assert.Empty(folders.TemporaryFiles());
        Assert.Equal(builds, folders.CollectorStarts);
    }

    private static TelemetryEvent ReadOnlyRecord(DataFolderScope folders) =>
        TelemetryEvent.ReadSpoolFile(Assert.Single(folders.SpoolFiles()));
}
