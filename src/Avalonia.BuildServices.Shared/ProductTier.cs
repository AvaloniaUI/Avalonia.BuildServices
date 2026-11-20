namespace Avalonia.Telemetry;

// An Avalonia licence tier, as the portal names them. The order is fixed:
// version 1 and 2 binary records store the tier as a byte value, which is why
// Trial sits between Indie and Business. Never compare tiers with < or >:
// LicenseHelper names the paid tiers one by one and ranks them in Rank.
public enum ProductTier
{
    None,
    Community,
    Indie,
    Trial,
    Business,
    Enterprise,
}
