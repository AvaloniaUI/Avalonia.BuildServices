using Xunit;

namespace Avalonia.Telemetry.Tests;

// The collector's command line is built from paths the customer's machine
// supplies: a profile folder with a space in it must stay one argument.
public class CollectorLauncherTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\a\b", @"""C:\a\b""")]
    [InlineData(@"C:\Users\Jo Bloggs\.nuget\x.dll", @"""C:\Users\Jo Bloggs\.nuget\x.dll""")]
    // A closing backslash would otherwise escape the closing quote.
    [InlineData(@"C:\dir\", @"""C:\dir\\""")]
    [InlineData(@"C:\dir\\", @"""C:\dir\\\\""")]
    // A quote, and backslashes before a quote.
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    public void Quote_keeps_an_argument_whole(string argument, string expected) =>
        Assert.Equal(expected, CollectorLauncher.Quote(argument));
}
