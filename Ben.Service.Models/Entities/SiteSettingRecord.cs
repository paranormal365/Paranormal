namespace Ben.Service.Models.Entities;

/// <summary>
/// One sitewide setting as shown in the admin page.
/// </summary>
/// <param name="Key">Stable identifier. Never shown as the primary label — it's for the API.</param>
/// <param name="Label">Human-readable name, stated by the server rather than derived from the key.</param>
/// <param name="Value">Current value, or null when unset.</param>
/// <param name="Description">Plain-language explanation of what changing this does.</param>
/// <param name="DateUpdated">When it last changed; creation time for a row never edited.</param>
/// <param name="IsMultiLine">
/// True when the value runs to several lines — a postal address, an announcement — so the editor
/// gives it a textarea. Decided by the server because the seed that declares settings lives there
/// and the Blazor library cannot reference it.
/// </param>
/// <param name="IsBoolean">
/// True when the value is on/off, so the editor gives it a switch. Decided by the server for the
/// same reason as <paramref name="IsMultiLine"/>. Before this existed the only boolean setting
/// had to carry "Accepts true or false" in its description — an instruction that only existed
/// because the control was a text box.
/// </param>
public sealed record SiteSettingRecord(
    string Key,
    string Label,
    string? Value,
    string? Description,
    DateTime DateUpdated,
    bool IsMultiLine = false,
    bool IsBoolean = false);

/// <param name="Value">Empty or whitespace clears the setting.</param>
public sealed record SetSiteSettingRequest(string? Value);
