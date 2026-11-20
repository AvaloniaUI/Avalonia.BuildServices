using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Avalonia.Telemetry.Tests;

// The opt-out rule on its own, against a made-up environment. (A process
// cannot reliably give itself a variable with an empty value, so that case
// can only be checked here.)
public class OptOutTests
{
    private const string OptOut = "AVALONIA_TELEMETRY_OPTOUT";

    private static bool HasOptedOut(params (string Name, string Value)[] variables) =>
        AvaloniaStatsTask.HasOptedOut(new BuildEnvironment(
            variables.Select(v => new KeyValuePair<string, string>(v.Name, v.Value))));

    [Fact]
    public void A_build_without_the_variable_has_not_opted_out() =>
        Assert.False(HasOptedOut());

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("anything at all")]
    public void Setting_the_variable_opts_out(string value) =>
        Assert.True(HasOptedOut((OptOut, value)));

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("False")]
    [InlineData(" 0 ")]
    [InlineData("\tfalse ")]
    public void Zero_and_false_do_not_opt_out(string value) =>
        Assert.False(HasOptedOut((OptOut, value)));

    [Theory]
    [InlineData("avalonia_telemetry_optout")]
    [InlineData("Avalonia_Telemetry_OptOut")]
    public void The_name_matches_whatever_its_case(string name) =>
        Assert.True(HasOptedOut((name, "1")));

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    public void A_build_under_NCrunch_has_opted_out(string value) =>
        Assert.True(HasOptedOut(("NCrunch", value)));

    [Fact]
    public void NCrunch_opts_out_even_when_the_variable_says_no() =>
        Assert.True(HasOptedOut(("NCrunch", "1"), (OptOut, "0")));
}
