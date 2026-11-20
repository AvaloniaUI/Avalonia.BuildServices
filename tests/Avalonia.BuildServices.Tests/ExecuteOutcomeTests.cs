using System.IO;
using AvaloniaUI.Licensing;
using Microsoft.Build.Framework;
using Xunit;

namespace Avalonia.Telemetry.Tests;

// What a build leaves behind: one record, or nothing at all. Execute() answers
// "true" either way, so each case looks at the data folder.
public class ExecuteOutcomeTests
{
    private const string OptOut = "AVALONIA_TELEMETRY_OPTOUT";

    private static void AssertNothingWasRecorded(DataFolderScope folders)
    {
        // Not even the folder: an opted-out build leaves the profile alone.
        Assert.False(Directory.Exists(folders.Data));
        Assert.Equal(0, folders.CollectorStarts);
    }

    private static TelemetryEvent AssertOneRecord(DataFolderScope folders)
    {
        var file = Assert.Single(folders.SpoolFiles());
        var record = TelemetryEvent.ReadSpoolFile(file);

        Assert.Equal(Common.SpoolFilePrefix + record.RecordId + ".json", Path.GetFileName(file));
        Assert.Empty(folders.TemporaryFiles());
        Assert.Equal(1, folders.CollectorStarts);
        return record;
    }

    [Fact]
    public void A_build_leaves_one_record_and_starts_the_collector()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        var task = folders.NewTask();
        task.ProjectName = "MyCompany.App";
        task.TargetFramework = "net8.0";

        Assert.True(task.Execute());

        var record = AssertOneRecord(folders);
        Assert.Equal("net8.0", record.Tfm);
        Assert.Equal(TelemetryPayload.HashProperty("MyCompany.App"), record.ProjectHash);
        Assert.Equal(TelemetryPayload.HashProperty("MyCompany"), record.ProjectRootHash);
        Assert.False(string.IsNullOrEmpty(record.MachineId));
        Assert.Equal(string.Empty, folders.Log());
    }

    // The project's name leaves the machine only as a hash.
    [Fact]
    public void The_record_does_not_hold_the_project_name()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        var task = folders.NewTask();
        task.ProjectName = "MyCompany.SecretProduct";

        Assert.True(task.Execute());

        var text = File.ReadAllText(Assert.Single(folders.SpoolFiles()));
        Assert.DoesNotContain("MyCompany", text);
        Assert.DoesNotContain("SecretProduct", text);
    }

    [Theory]
    [InlineData("AVALONIA_TELEMETRY_OPTOUT", "1")]
    [InlineData("AVALONIA_TELEMETRY_OPTOUT", "true")]
    [InlineData("avalonia_telemetry_optout", "1")]
    [InlineData("NCrunch", "1")]
    public void An_opted_out_build_leaves_nothing(string name, string value)
    {
        using var environment = new EnvironmentScope((name, value));
        using var folders = DataFolderScope.Create();

        Assert.True(folders.NewTask().Execute());

        AssertNothingWasRecorded(folders);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    public void A_variable_that_says_no_does_not_opt_out(string value)
    {
        using var environment = new EnvironmentScope((OptOut, value));
        using var folders = DataFolderScope.Create();

        Assert.True(folders.NewTask().Execute());

        AssertOneRecord(folders);
    }

    [Theory]
    [InlineData("Community")]
    [InlineData("Trial")]
    public void A_licence_that_requires_telemetry_overrides_the_opt_out_and_says_so(string tier)
    {
        using var environment = new EnvironmentScope((OptOut, "1"));
        using var folders = DataFolderScope.Create();
        var engine = new RecordingBuildEngine();
        var task = folders.NewTask();
        task.BuildEngine = engine;
        task.ProjectLicenseTier = tier;

        Assert.True(task.Execute());

        var record = AssertOneRecord(folders);
        Assert.Equal(tier, record.ProjectLicenseTier);

        // A message, never a warning: warnings fail builds that treat them as errors.
        var notice = Assert.Single(engine.Messages);
        Assert.Contains($"Avalonia {tier} requires telemetry", notice.Message);
        Assert.Empty(engine.Warnings);
        Assert.Empty(engine.Errors);
    }

    [Fact]
    public void A_licence_whose_features_lack_the_opt_out_overrides_it_too()
    {
        using var environment = new EnvironmentScope((OptOut, "1"));
        using var folders = DataFolderScope.Create();
        var task = folders.NewTask();
        task.ProjectLicenseTier = "Indie";
        task.ProjectLicenseFeatures = [new FakeTaskItem("feature:something-else")];

        Assert.True(task.Execute());

        AssertOneRecord(folders);
    }

    [Fact]
    public void A_licence_with_the_opt_out_feature_may_opt_out()
    {
        using var environment = new EnvironmentScope((OptOut, "1"));
        using var folders = DataFolderScope.Create();
        var engine = new RecordingBuildEngine();
        var task = folders.NewTask();
        task.BuildEngine = engine;
        task.ProjectLicenseTier = "Business";
        task.ProjectLicenseFeatures = [new FakeTaskItem(AvaloniaLicenseProduct.TelemetryOptOut)];

        Assert.True(task.Execute());

        AssertNothingWasRecorded(folders);
        Assert.Empty(engine.Messages);
    }

    [Fact]
    public void A_licensed_build_that_did_not_opt_out_records_without_a_notice()
    {
        using var environment = new EnvironmentScope();
        using var folders = DataFolderScope.Create();
        var engine = new RecordingBuildEngine();
        var task = folders.NewTask();
        task.BuildEngine = engine;
        task.ProjectLicenseTier = "Business";
        task.ProjectLicenseFeatures = [new FakeTaskItem(AvaloniaLicenseProduct.TelemetryOptOut)];

        Assert.True(task.Execute());

        AssertOneRecord(folders);
        Assert.Empty(engine.Messages);
        Assert.Empty(engine.Warnings);
        Assert.Empty(engine.Errors);
    }
}
