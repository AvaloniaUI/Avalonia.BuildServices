using System;
using System.Collections.Generic;
using Xunit;

namespace Avalonia.Telemetry.Tests;

public class BuildEnvironmentTests
{
    private static BuildEnvironment Environment(params (string Name, string Value)[] variables)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in variables)
        {
            pairs.Add(new KeyValuePair<string, string>(name, value));
        }

        return new BuildEnvironment(pairs);
    }

    [Fact]
    public void An_empty_environment_has_nothing_set()
    {
        var environment = Environment();

        Assert.False(environment.Contains("CI"));
        Assert.Null(environment.Get("CI"));
        Assert.False(environment.IsSet("CI"));
        Assert.False(environment.HasValue("CI", "true"));
    }

    [Fact]
    public void Names_match_whatever_their_case()
    {
        var environment = Environment(("GitHub_Actions", "true"));

        Assert.True(environment.Contains("GITHUB_ACTIONS"));
        Assert.Equal("true", environment.Get("github_actions"));
    }

    // On Unix these are two variables. Neither may throw, and one of them wins.
    [Fact]
    public void Two_names_that_differ_only_by_case_do_not_throw()
    {
        var environment = Environment(("Path", "first"), ("PATH", "second"));

        Assert.Equal("second", environment.Get("path"));
    }

    [Fact]
    public void A_variable_set_to_nothing_is_there_but_not_set()
    {
        var environment = Environment(("TF_BUILD", ""));

        Assert.True(environment.Contains("TF_BUILD"));
        Assert.Equal(string.Empty, environment.Get("TF_BUILD"));
        Assert.False(environment.IsSet("TF_BUILD"));
    }

    [Fact]
    public void Values_match_whatever_their_case_unless_asked_otherwise()
    {
        var environment = Environment(("CI", "True"));

        Assert.True(environment.HasValue("CI", "true"));
        Assert.False(environment.HasValue("CI", "true", StringComparison.Ordinal));
        Assert.False(environment.HasValue("CI", "1"));
    }

    [Fact]
    public void Capture_reads_this_process()
    {
        using var scope = new EnvironmentScope(("AVALONIA_BUILDSERVICES_TEST_VARIABLE", "here"));

        Assert.Equal("here", BuildEnvironment.Capture().Get("avalonia_buildservices_test_variable"));
    }
}
