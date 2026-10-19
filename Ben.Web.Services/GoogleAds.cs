using Microsoft.Extensions.Configuration;

namespace Ben.Web.Services;

/// <summary>
/// The website's Google Ads measurement settings (10/19/2026), read from the <c>GoogleAds</c>
/// section of appsettings.json.
/// </summary>
/// <remarks>
/// <para>An empty <c>TagId</c> means no Google tag at all: no script, no consent banner, no
/// "Cookie settings" link. Nothing about Google is on the page unless a tag is configured.</para>
/// <para>The tag id (<c>AW-…</c>) and conversion label are not secrets — every page that carries
/// the tag shows them — but they live in config so turning measurement off is one empty value.</para>
/// <para>The website only. The iPhone and iPad apps send nothing to Google, and nothing here
/// changes that.</para>
/// </remarks>
public sealed record GoogleAdsSettings(string TagId, string SubscribeConversionLabel)
{
    public const string Section = "GoogleAds";

    /// <summary>The localStorage key ads-consent.js keeps the visitor's choice under.</summary>
    public const string ConsentStorageKey = "ih-ads-consent";

    public static GoogleAdsSettings From(IConfiguration configuration) => new(
        (configuration[$"{Section}:TagId"] ?? "").Trim(),
        (configuration[$"{Section}:SubscribeConversionLabel"] ?? "").Trim());

    /// <summary>True only when a tag id is configured.</summary>
    public bool Enabled => TagId.Length > 0;

    /// <summary>
    /// The conversion's <c>send_to</c>, <c>AW-…/label</c>; null when either half is missing, in
    /// which case no conversion is sent.
    /// </summary>
    public string? SubscribeConversionSendTo =>
        Enabled && SubscribeConversionLabel.Length > 0 ? $"{TagId}/{SubscribeConversionLabel}" : null;
}

/// <summary>Where <c>/subscribed</c> sends the visitor on.</summary>
public static class SubscribedLanding
{
    /// <summary>The fallback for an unknown, missing or foreign group.</summary>
    public const string Fallback = "/organizations";

    /// <summary>
    /// The group's billing page, with the outcome its banner reads — but only for a group the
    /// signed-in person belongs to. Anything else goes to <see cref="Fallback"/>, so the page can
    /// never be used to bounce someone to an address of a stranger's choosing.
    /// </summary>
    public static string Destination(string? org, string? how, IEnumerable<Guid> myOrganizationIds)
    {
        if (!Guid.TryParse(org, out var orgId) || orgId == Guid.Empty) return Fallback;
        if (!myOrganizationIds.Contains(orgId)) return Fallback;

        var outcome = how switch
        {
            "free" => "?checkout=free",
            "paid" => "?checkout=success",
            _ => "",
        };
        return $"/organizations/{orgId}/billing{outcome}";
    }
}
