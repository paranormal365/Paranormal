using Ben.Data.Common.Enums;
using Ben.Service.Models;

namespace Ben.Web.Website.Library.Billing;

/// <summary>
/// How the pricing page words a plan's caps and areas, shared by the bands and the comparison
/// below them so the two can't say the same cap differently.
/// </summary>
public static class PricingWords
{
    /// <summary>
    /// Whether the site actually enforces this cap, and so whether a plan may be described by it.
    /// </summary>
    /// <remarks>
    /// Storage, custom roles and the per-event session, staff and file caps can be set on a plan
    /// but nothing checks them yet (per-event storage comes from a site setting instead). Listing
    /// one would describe a plan by a rule it doesn't have.
    /// </remarks>
    public static bool IsShown(SubscriptionLimit limit) => limit is not (
        SubscriptionLimit.StorageMegabytes or SubscriptionLimit.CustomRoles
        or SubscriptionLimit.EventSessions or SubscriptionLimit.EventStaff
        or SubscriptionLimit.EventFilesMegabytes);

    /// <summary>
    /// A cap as a sentence: "Up to 25 pieces of equipment", "Unlimited open cases", "No public pages".
    /// </summary>
    /// <remarks>
    /// Null and zero get words, not numbers — "∞" and "0" on a pricing card make the reader do
    /// the interpreting, and zero-means-off is exactly the case that must not look like a typo.
    /// </remarks>
    public static string LimitSentence(PublicTierLimit limit)
    {
        // The ones that are a length of time or a size, not a count of things.
        switch (limit.Limit)
        {
            case SubscriptionLimit.PhotoRetentionDays:
                return limit.MaxValue is { } pd and > 0 ? $"Photos kept for {pd:N0} days" : "Photos kept with no time limit";
            case SubscriptionLimit.RecordingRetentionDays:
                return limit.MaxValue is { } rd and > 0 ? $"Recordings kept for {rd:N0} days" : "Recordings kept with no time limit";
            case SubscriptionLimit.RecordingMinutes:
                return limit.MaxValue is { } m and > 0 ? $"Recordings up to {m:N0} minutes long" : "Recordings of any length";
        }

        var (thing, things) = limit.Limit switch
        {
            SubscriptionLimit.OpenCases            => ("open case", "open cases"),
            SubscriptionLimit.EquipmentItems       => ("piece of equipment", "pieces of equipment"),
            SubscriptionLimit.ActiveEquipmentLoans => ("equipment loan at a time", "equipment loans at a time"),
            SubscriptionLimit.OpenInvestigations   => ("open investigation", "open investigations"),
            SubscriptionLimit.PendingInvites       => ("pending invite", "pending invites"),
            SubscriptionLimit.StorageMegabytes     => ("MB of storage", "MB of storage"),
            SubscriptionLimit.PublishedPages       => ("public page", "public pages"),
            SubscriptionLimit.CustomRoles          => ("custom role", "custom roles"),
            SubscriptionLimit.CasesPerPeriod       => ("new case each billing period", "new cases each billing period"),
            SubscriptionLimit.TourGalleryImages    => ("picture per tour page", "pictures per tour page"),
            SubscriptionLimit.ActiveHostedEvents   => ("hosted event live at a time", "hosted events live at a time"),
            SubscriptionLimit.EventSessions        => ("session per event", "sessions per event"),
            SubscriptionLimit.EventStaff           => ("staff member per event", "staff per event"),
            SubscriptionLimit.EventFilesMegabytes  => ("MB of files per event", "MB of files per event"),
            _                                      => (limit.Limit.ToString(), limit.Limit.ToString()),
        };

        return limit.MaxValue switch
        {
            null => $"Unlimited {things}",
            0    => $"No {things}",
            1    => $"1 {thing}",
            { } n when limit.Limit == SubscriptionLimit.StorageMegabytes && n >= 1024
                 => $"{n / 1024m:0.#} GB of storage",
            { } n => $"Up to {n:N0} {things}",
        };
    }

    public static string AreaLabel(OrganizationPermissionArea area) => area switch
    {
        OrganizationPermissionArea.OrganizationProfile => "Group profile",
        OrganizationPermissionArea.Membership          => "Membership",
        OrganizationPermissionArea.Cases               => "Cases",
        OrganizationPermissionArea.Investigations      => "Investigations",
        OrganizationPermissionArea.Equipment           => "Equipment",
        OrganizationPermissionArea.PublicPages         => "Public pages",
        OrganizationPermissionArea.Files               => "Files",
        OrganizationPermissionArea.Clients             => "Client requests",
        OrganizationPermissionArea.Calendar            => "Calendar",
        OrganizationPermissionArea.Events              => "Events",
        _                                              => area.ToString(),
    };

    public static string CapabilityLabel(TierCapability capability) => capability switch
    {
        TierCapability.CaseTransfers          => "Case transfers",
        TierCapability.MediaMetadataStripping => "Audio/video location stripping",
        TierCapability.PrivateResidenceCases  => "Private-residence cases",
        TierCapability.HostEvents             => "Hosted events",
        TierCapability.EventCredits           => "Buying events",
        TierCapability.EventTicketing         => "Ticket sales",
        _                                     => capability.ToString(),
    };
}
