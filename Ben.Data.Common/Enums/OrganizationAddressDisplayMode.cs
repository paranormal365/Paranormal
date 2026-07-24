namespace Ben.Data.Common.Enums;

/// <summary>
/// Controls how an OrganizationAddress is rendered for a given audience.
/// </summary>
public enum OrganizationAddressDisplayMode
{
    /// <summary>Show full address text plus an exact map pin.</summary>
    FullAddressAndMap = 0,
    /// <summary>Show full address text only, no map.</summary>
    FullAddressOnly = 1,
    /// <summary>Show an exact map pin only, no address text.</summary>
    MapPinOnly = 2,
    /// <summary>Show an obfuscated region circle on the map (uses OrganizationAddressMapConfig radius/color), no text and no exact pin.</summary>
    RegionOnly = 3,
    /// <summary>Show nothing to this audience.</summary>
    Hidden = 4
}
