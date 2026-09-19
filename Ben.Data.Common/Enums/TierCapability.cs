namespace Ben.Data.Common.Enums;

/// <summary>
/// One thing a subscription tier's groups may DO — a per-tier boolean capability (item 167).
/// </summary>
/// <remarks>
/// <para>The third keyed tier concept, deliberately distinct from the other two:
/// <see cref="SubscriptionLimit"/> is how MANY, <see cref="OrganizationPermissionArea"/> is
/// which role AREAS may carry custom grants, and this is a plain may-or-may-not switch.
/// Modeled as keyed rows for the same reason both siblings are — a future rule of this shape
/// ("publications", "API access") is a row, not a migration.</para>
///
/// <para><b>Append-only; never renumber.</b> Values end up in rows that outlive the
/// deployment that wrote them.</para>
/// </remarks>
public enum TierCapability
{
    /// <summary>Transferring a case to another group, and accepting one transferred in.
    /// Ben's rule (item 167): a free-plan group can do neither — and both ends are checked,
    /// so a paid group cannot hand a case TO a free group either.</summary>
    CaseTransfers = 1,

    /// <summary>
    /// Stripping embedded metadata — GPS above all — from AUDIO and VIDEO before they are served
    /// (item 181). Images are stripped for everyone regardless: a case photo is the commonest way
    /// a client's address escapes, and the re-encode costs nothing. A/V needs an ffmpeg remux per
    /// file, which is real compute, so it is a capability a tier can withhold.
    /// </summary>
    MediaMetadataStripping = 2,

    /// <summary>
    /// Taking on and publishing PRIVATE-ENGAGEMENT work — cases at private residences, cases
    /// born from a client's request (item 184, Ben's lane split 2026-08-24). The free lane is
    /// public-place work, fully free including publication; homes-and-clients work is the paid
    /// lane end to end: accepting the request, binding a residence place, and making any of it
    /// public. One capability for both taking and publishing, because under
    /// plan-governs-publication they are the same right.
    /// </summary>
    PrivateResidenceCases = 3,


    /// <summary>
    /// Running hosted events at all — multi-night events with rooms, a programme and a door
    /// (item 235).
    /// </summary>
    /// <remarks>
    /// A business kind pays per active event the way it pays per tour, so this capability is for
    /// the OTHER case: a group on the member ladder that wants to hold one. Withholding it is how
    /// the free lane stays the free lane.
    /// </remarks>
    HostEvents = 4,

    /// <summary>
    /// Buying event credits: one credit buys one event, for a group whose plan does not otherwise
    /// include hosting.
    /// </summary>
    /// <remarks>
    /// Ben's rule, 2026-09-11: a credit is a single event a member or group may schedule and host,
    /// it expires a year after it is bought, and three events need three credits. Declared now so
    /// the tier page lists it and the entitlement code has something real to point at; the purchase
    /// itself is recorded and deliberately unbuilt.
    /// </remarks>
    EventCredits = 5,

    /// <summary>
    /// Selling tickets through the site, rather than settling the money with the guest directly.
    /// </summary>
    /// <remarks>
    /// Reserved. The site takes no guest money today and will not until Ben decides it should:
    /// that decision brings Stripe Connect, refunds and tax with it. The value exists so the
    /// number is fixed before anything needs it.
    /// </remarks>
    EventTicketing = 6,
}
