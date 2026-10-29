using System;
using System.IO;
using AvaloniaUI.Licensing;
using Xunit;

namespace Avalonia.Telemetry.Tests;

// Who may opt out, decided from the project's own licence and the machine's
// tickets. Every case names an empty ticket store, or spells out its own
// tickets, so the licences on the machine running the tests never change an
// answer.
public sealed class LicenseHelperTests : IDisposable
{
    private const string OptOut = AvaloniaLicenseProduct.TelemetryOptOut;

    private readonly DataFolderScope _folders = DataFolderScope.Create();

    public void Dispose() => _folders.Dispose();

    private (ProductTier Tier, bool TelemetryRequired) Resolve(string? tier, params string[] features) =>
        LicenseHelper.Resolve(tier, features, _folders.Tickets);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_project_without_a_licence_may_opt_out(string? tier) =>
        Assert.Equal((ProductTier.None, false), Resolve(tier));

    [Theory]
    [InlineData("Community", ProductTier.Community)]
    [InlineData("community", ProductTier.Community)]
    [InlineData("Trial", ProductTier.Trial)]
    [InlineData("TRIAL", ProductTier.Trial)]
    public void Community_and_trial_licences_require_telemetry(string tier, ProductTier expected) =>
        Assert.Equal((expected, true), Resolve(tier));

    // An older licensing package reports the tier and no features. Its paid
    // keys are given the benefit of the doubt.
    [Theory]
    [InlineData("Indie", ProductTier.Indie)]
    [InlineData("Business", ProductTier.Business)]
    [InlineData("Enterprise", ProductTier.Enterprise)]
    public void A_paid_tier_without_features_may_opt_out(string tier, ProductTier expected) =>
        Assert.Equal((expected, false), Resolve(tier));

    [Fact]
    public void A_licence_with_the_opt_out_feature_may_opt_out() =>
        Assert.Equal((ProductTier.Business, false), Resolve("Business", "feature:something-else", OptOut));

    [Fact]
    public void A_licence_whose_features_lack_the_opt_out_requires_telemetry() =>
        Assert.Equal((ProductTier.Indie, true), Resolve("Indie", "feature:something-else"));

    // "3" would otherwise parse as the fourth tier, Trial, and force telemetry
    // on a build that never named a licence.
    [Theory]
    [InlineData("3")]
    [InlineData("Platinum")]
    [InlineData("xpf-business")]
    [InlineData("  ")]
    public void A_tier_nobody_issues_is_no_tier(string tier) =>
        Assert.Equal((ProductTier.None, false), Resolve(tier));

    // A ticket, as the product names it carries. A signed ticket cannot be made
    // here, so these go through the decision alone.
    private static (ProductTier Tier, bool TelemetryRequired) ResolveWithTickets(string? tier, params string[][] tickets) =>
        LicenseHelper.Resolve(tier, [], tickets);

    // The machine's tickets count for every project built on it, this one with
    // no licence of its own.
    [Theory]
    [InlineData("product:community", ProductTier.Community)]
    [InlineData("product:trial", ProductTier.Trial)]
    public void A_community_or_trial_ticket_requires_telemetry_of_every_project(string product, ProductTier expected) =>
        Assert.Equal((expected, true), ResolveWithTickets(null, [product, "avalonia-developer-tools"]));

    // A library ticket names its tier and its libraries, and no flags.
    [Fact]
    public void A_paid_ticket_without_flags_may_opt_out() =>
        Assert.Equal((ProductTier.Indie, false), ResolveWithTickets(null, ["product:indie", "Avalonia.Controls.Media", "usage:runtime"]));

    [Fact]
    public void A_paid_ticket_whose_flags_lack_the_opt_out_requires_telemetry() =>
        Assert.Equal((ProductTier.Business, true), ResolveWithTickets(null, ["product:business", "feature:paid-tooling"]));

    [Fact]
    public void A_ticket_with_the_opt_out_frees_a_build_with_a_community_ticket_too() =>
        Assert.Equal((ProductTier.Enterprise, false), ResolveWithTickets(null,
            ["product:community", "avalonia-developer-tools"],
            ["product:enterprise", "feature:paid-tooling", OptOut]));

    [Fact]
    public void A_paid_ticket_without_flags_frees_a_build_with_a_community_ticket_too() =>
        Assert.Equal((ProductTier.Indie, false), ResolveWithTickets(null, ["product:community"], ["product:indie"]));

    [Fact]
    public void A_paid_key_without_features_frees_a_build_with_a_community_ticket_too() =>
        Assert.Equal((ProductTier.Indie, false), ResolveWithTickets("Indie", ["product:community"]));

    [Fact]
    public void A_community_key_with_a_paid_ticket_may_opt_out() =>
        Assert.Equal((ProductTier.Enterprise, false), ResolveWithTickets("Community", ["product:enterprise", OptOut]));

    // XPF's product:xpf-<tier> entries name no tier this package knows.
    [Fact]
    public void A_ticket_of_a_tier_nobody_here_can_name_requires_telemetry() =>
        Assert.Equal((ProductTier.None, true), ResolveWithTickets(null, ["product:xpf-business"]));

    [Fact]
    public void A_ticket_store_that_is_not_there_is_no_licence()
    {
        var missing = Path.Combine(_folders.Root, "no-such-folder");

        Assert.Equal((ProductTier.Community, true), LicenseHelper.Resolve("Community", [], missing));
        Assert.Equal((ProductTier.None, false), LicenseHelper.Resolve(null, [], missing));
    }

    [Fact]
    public void A_ticket_store_full_of_rubbish_is_no_licence()
    {
        File.WriteAllText(Path.Combine(_folders.Tickets, "ticket.json"), "{ not a ticket");
        File.WriteAllBytes(Path.Combine(_folders.Tickets, "ticket.bin"), [0, 1, 2, 3]);

        Assert.Equal((ProductTier.None, false), Resolve(null));
    }
}
