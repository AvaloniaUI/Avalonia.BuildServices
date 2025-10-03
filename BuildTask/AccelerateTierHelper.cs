using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaUI.Licensing;
using AvaloniaUI.Licensing.Ticket;

namespace Avalonia.Telemetry;

public static class AccelerateTierHelper
{
    public static AccelerateTier ResolveAccelerateTierFromLicenseTickets(IEnumerable<string> sdkLicenseKeys)
    {
        var ticketStore = new GenericTicketStore(null);

        // Include licenses from app session tickets (tool sign-in)
        var appLicenses = ticketStore
            .LoadAllAppTickets()
            .Select(t => t.LicenseKey);
        
        // And from library tickets (online key cache)
        var libraryLicenses = ticketStore
            .LoadAllLibraryTickets()
            .Select(t => t.RuntimeKey);

        // And lastly from offline keys provided directly. Filter online keys, because these are handled above.
        var offlineLicenses = sdkLicenseKeys
            .Where(k => k.StartsWith("avln_off_key:v1:", StringComparison.Ordinal))
            .Select(k => k.Split(':').Last());

        var highestValidTier = AccelerateTier.None;
        var currentTime = DateTimeOffset.UtcNow;

        foreach (var licenseKey in appLicenses.Concat(libraryLicenses).Concat(offlineLicenses))
        {
            try
            {
                var licenseInformation = AvaloniaLicenseInformation.Load(licenseKey);

                var tierStr = licenseInformation.Products
                    .Select(p => p.Name)
                    .FirstOrDefault(p => p?.StartsWith("product:") == true)?
                    .Split(':')
                    .Last();

                var tier = tierStr switch
                {
                    "indie" => AccelerateTier.Indie,
                    "business" => AccelerateTier.Business,
                    "enterprise" => AccelerateTier.Enterprise,
                    "community" => AccelerateTier.Community,
                    "trial" => AccelerateTier.Trial,
                    _ => AccelerateTier.None
                };

                // Skip expired tickets
                if (licenseInformation.ExpiresAt is { } expiresAt)
                {
                    if (expiresAt <= currentTime)
                    {
                        continue;
                    }
                }

                // Update the highest valid tier found
                if (tier > highestValidTier)
                {
                    highestValidTier = tier;
                }
            }
            catch
            {
                // Skip invalid ticket files and continue checking others
            }
        }

        return highestValidTier;
    }
}