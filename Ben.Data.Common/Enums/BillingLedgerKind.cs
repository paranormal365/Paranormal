namespace Ben.Data.Common.Enums;

/// <summary>
/// What a <c>BillingLedgerEntry</c> records (item 168). Append-only values — the ledger is a
/// financial record, so kinds are never renumbered or removed.
/// </summary>
public enum BillingLedgerKind
{
    /// <summary>Money the organization owes — a period billed, a seat added.</summary>
    Charge = 0,

    /// <summary>Money received from the organization. The only kind that carries a receipt number.</summary>
    Payment = 1,

    /// <summary>A correction, either direction. The ledger is append-only, so a mistake is
    /// answered with an adjustment naming it, never by editing the mistaken row.</summary>
    Adjustment = 2,

    /// <summary>Money paid OUT to a referrer for referrals their coupon brought in. Carries a
    /// referrer instead of an organization.</summary>
    ReferralPayout = 3,
}
