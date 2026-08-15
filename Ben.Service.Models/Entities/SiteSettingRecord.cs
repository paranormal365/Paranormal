namespace Ben.Service.Models.Entities;

/// <summary>
/// One sitewide setting as shown in the admin page.
/// </summary>
/// <param name="Key">Stable identifier. Never shown as the primary label — it's for the API.</param>
/// <param name="Label">Human-readable name, stated by the server rather than derived from the key.</param>
/// <param name="Value">Current value, or null when unset.</param>
/// <param name="Description">Plain-language explanation of what changing this does.</param>
/// <param name="DateUpdated">When it last changed; creation time for a row never edited.</param>
public sealed record SiteSettingRecord(
    string Key,
    string Label,
    string? Value,
    string? Description,
    DateTime DateUpdated);

/// <param name="Value">Empty or whitespace clears the setting.</param>
public sealed record SetSiteSettingRequest(string? Value);
