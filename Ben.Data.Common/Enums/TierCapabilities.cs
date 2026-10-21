namespace Ben.Data.Common.Enums;

/// <summary>Which <see cref="TierCapability"/> values a plan can actually be sold with.</summary>
/// <remarks>
/// <para><b>Ben, 10/21/2026:</b> "Events are not part of subscriptions." So the three event values —
/// hosting, buying credits and selling tickets — are never part of a plan, whatever rows a tier still
/// carries from before. The tier editor offers only these, and the price list reports only these.</para>
///
/// <para>The enum values stay: they are numbers stored in rows that outlive the deployment that wrote
/// them, and renumbering is forbidden.</para>
/// </remarks>
public static class TierCapabilities
{
    /// <summary>The capabilities a plan may include or leave out, in their declared order.</summary>
    public static IReadOnlyList<TierCapability> Sellable { get; } =
    [
        TierCapability.CaseTransfers,
        TierCapability.MediaMetadataStripping,
        TierCapability.PrivateResidenceCases,
    ];

    /// <summary>True for a capability that is about events, which no plan includes.</summary>
    public static bool IsAboutEvents(TierCapability capability) =>
        capability is TierCapability.HostEvents or TierCapability.EventCredits or TierCapability.EventTicketing;
}
