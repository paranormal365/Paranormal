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
}
