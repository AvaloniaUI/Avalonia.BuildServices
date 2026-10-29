using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AvaloniaUI.Licensing;
using AvaloniaUI.Licensing.Ticket;

namespace Avalonia.Telemetry;

internal static class LicenseHelper
{
    // The portal decides who may opt out of telemetry, with the result provided in
    // the licence itself: every paid, non-trial key carries feature:telemetry-optout,
    // and Community and trial keys do not. This reads that flag where it can.
    //
    // The project's flags are AvaloniaUI.Licensing's ResolvedFeatures, which lists
    // only feature:* entries, so a Community or trial key yields none, and packages
    // before the one that emits them yield none for any key. For those two tiers
    // the tier name stands in for the flag: the portal never gives them one. A
    // paid tier that carries no flags at all is not guessed at: an old package
    // cannot tell a key that carries the flag from one that does not, a library
    // key carries no flags either, and forcing telemetry on a paid licence would
    // be the worse mistake. Such a licence may opt out.
    //
    // The machine's tickets count for every project built on it, whichever key
    // the project itself names. That is meant: a ticket is a licence this user
    // holds, and the opt-out goes with the user. A ticket is read by the same
    // rule as the project's key, from its product:<tier> entry and its feature:*
    // entries. So one licence that may opt out, the project's or a ticket's,
    // frees the build, and the opt-out is refused only when every licence found
    // requires telemetry (README, "Opting Out").
    public static (ProductTier Tier, bool TelemetryRequired) Resolve(string? projectTier, IEnumerable<string> projectFeatures, string? ticketsPath = null) =>
        Resolve(projectTier, projectFeatures, TicketProducts(ticketsPath));

    // The decision apart from the ticket store, for the tests: each ticket is
    // the product names it carries.
    internal static (ProductTier Tier, bool TelemetryRequired) Resolve(string? projectTier, IEnumerable<string> projectFeatures, IEnumerable<string[]> tickets)
    {
        var tier = ParseTier(projectTier);
        var verdict = Judge(tier, projectFeatures);

        foreach (var products in tickets)
        {
            var ticketTier = ProductTier.None;
            foreach (var product in products)
            {
                if (product.StartsWith("product:", StringComparison.Ordinal))
                {
                    var productTier = ParseTier(product.Substring("product:".Length));
                    if (productTier > ticketTier)
                    {
                        ticketTier = productTier;
                    }
                }
            }

            var ticketVerdict = Judge(ticketTier, products);

            // A ticket is a licence even when this package cannot name its tier
            // (XPF's product:xpf-<tier> entries): then one without the flag.
            if (ticketVerdict == Verdict.None)
            {
                ticketVerdict = Verdict.Required;
            }

            if (ticketVerdict > verdict)
            {
                verdict = ticketVerdict;
            }

            if (ticketTier > tier)
            {
                tier = ticketTier;
            }
        }

        return (tier, verdict == Verdict.Required);
    }

    // What one licence says about the opt-out, in the order they override each
    // other: one licence that may opt out frees the build.
    private enum Verdict
    {
        // No licence: a project that names neither a tier nor a flag.
        None,

        // Telemetry stays on, whatever the variable says.
        Required,

        // The variable is honoured.
        MayOptOut,
    }

    private static Verdict Judge(ProductTier tier, IEnumerable<string> products)
    {
        var flagged = false;
        foreach (var product in products)
        {
            if (product == AvaloniaLicenseProduct.TelemetryOptOut)
            {
                return Verdict.MayOptOut;
            }

            flagged |= product.StartsWith("feature:", StringComparison.Ordinal);
        }

        if (flagged)
        {
            // The portal wrote flags into this key and left this one out.
            return Verdict.Required;
        }

        return tier switch
        {
            ProductTier.Community or ProductTier.Trial => Verdict.Required,
            ProductTier.None => Verdict.None,
            // A paid tier whose flags are unknown: the benefit of the doubt.
            _ => Verdict.MayOptOut,
        };
    }

    private static readonly long TicketCacheLifetime = Stopwatch.Frequency * 30;

    private static volatile MachineTickets? _machineTickets;

    // The machine's tickets, read at most once per TicketCacheLifetime. Reading
    // them opens every ticket file and checks its signature, and an MSBuild node
    // asks once per project and target framework for an answer that is the same
    // for all of them. The cost: a node that is still running sees a licence
    // activated a moment ago that much later.
    //
    // A static rather than MSBuild's registered task objects: older task hosts
    // answer that callback with an error, and an error fails the build.
    private static string[][] TicketProducts(string? ticketsPath)
    {
        // A store named by the caller is a test's, and is read every time.
        if (ticketsPath is not null)
        {
            return LoadTicketProducts(ticketsPath).ToArray();
        }

        var tickets = _machineTickets;

        if (tickets is null || Stopwatch.GetTimestamp() - tickets.ReadAt > TicketCacheLifetime)
        {
            // Builds running side by side may both read here. Each gets a whole
            // answer and the later one stays.
            tickets = new MachineTickets(LoadTicketProducts(null).ToArray());
            _machineTickets = tickets;
        }

        return tickets.Products;
    }

    // Never changed once built, so it can be handed between threads as it is.
    private sealed class MachineTickets(string[][] products)
    {
        public string[][] Products { get; } = products;

        public long ReadAt { get; } = Stopwatch.GetTimestamp();
    }

    // The product names of every unexpired, correctly signed ticket on this
    // machine. A ticket that cannot be read is no licence, never a build failure.
    private static IEnumerable<string[]> LoadTicketProducts(string? ticketsPath)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in LoadTicketKeys(ticketsPath))
        {
            string[]? products = null;
            try
            {
                var licence = AvaloniaLicenseInformation.Load(key);
                if (licence.ExpiresAt is not { } expiresAt || expiresAt > now)
                {
                    products = licence.Products.Select(p => p.Name).OfType<string>().ToArray();
                }
            }
            catch
            {
                // Corrupt, tampered with, or signed for another key
            }

            if (products != null)
            {
                yield return products;
            }
        }
    }

    private static string[] LoadTicketKeys(string? ticketsPath)
    {
        try
        {
            var store = new GenericTicketStore(ticketsPath);
            return store.LoadAllAppTickets().Select(t => t.LicenseKey)
                .Concat(store.LoadAllLibraryTickets().Select(t => t.RuntimeKey))
                .OfType<string>()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    // "indie", "Business", ... as the portal writes them into a key's
    // product:<tier> entry and as AvaloniaLicenseSubscription.ToString() emits
    // them for $(AvaloniaUIResolvedSubscription). Anything else, including XPF's
    // product:xpf-<tier> names and numeric strings, is no tier.
    private static ProductTier ParseTier(string? name) =>
        !string.IsNullOrEmpty(name)
        && !char.IsDigit(name![0])
        && Enum.TryParse<ProductTier>(name, ignoreCase: true, out var tier)
            ? tier
            : ProductTier.None;
}
