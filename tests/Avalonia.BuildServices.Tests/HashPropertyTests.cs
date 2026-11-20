using Xunit;

namespace Avalonia.Telemetry.Tests;

// Project names leave the machine only as hashes, and the server matches them
// against the hashes older packages sent: the format must not drift.
public class HashPropertyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_hashes_to_nothing(string? value) =>
        Assert.Equal(string.Empty, TelemetryPayload.HashProperty(value));

    // The standard SHA-256 test vector, as upper-case hex without separators.
    [Fact]
    public void A_name_hashes_to_upper_case_SHA256() =>
        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            TelemetryPayload.HashProperty("abc"));

    [Fact]
    public void The_same_name_always_hashes_the_same() =>
        Assert.Equal(TelemetryPayload.HashProperty("MyCompany.App"), TelemetryPayload.HashProperty("MyCompany.App"));

    [Theory]
    [InlineData("MyCompany.App", "MyCompany.app")]
    [InlineData("MyCompany.App", "MyCompany.App ")]
    [InlineData("Café", "Cafe")]
    public void Different_names_hash_differently(string one, string other) =>
        Assert.NotEqual(TelemetryPayload.HashProperty(one), TelemetryPayload.HashProperty(other));
}
