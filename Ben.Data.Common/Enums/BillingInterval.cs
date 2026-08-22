namespace Ben.Data.Common.Enums;

/// <summary>How long one billing period lasts.</summary>
/// <remarks>
/// <para>The values are the <b>number of months</b> in the period, so a period end is
/// <c>start.AddMonths((int)interval)</c> and no lookup table is needed to do date arithmetic. That
/// also means adding a new cadence is adding a value, not touching the arithmetic.</para>
///
/// <para>Months rather than days on purpose: a yearly subscription starting on 31 January should
/// renew on 31 January, which day-counting gets wrong every leap year.</para>
/// </remarks>
public enum BillingInterval
{
    Monthly = 1,
    Quarterly = 3,
    HalfYearly = 6,
    Yearly = 12,
}
