using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Admin;

/// <summary>One ledger line as read back — admin view carries the org and referrer names.</summary>
public record BillingLedgerEntryRecord(
    Guid Id,
    BillingLedgerKind Kind,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? ReferrerAppUserId,
    string? ReferrerName,
    decimal Amount,
    bool AdjustmentIsCredit,
    decimal TaxRatePercent,
    decimal TaxAmount,
    string Description,
    string? PaymentReference,
    int? ReceiptNumber,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    DateTime DateCreated,
    string CreatedByName);

/// <summary>Recording a charge or a payment against a group. Tax is computed server-side from
/// the group's state and frozen — the caller sends the pre-tax amount only.</summary>
public record RecordBillingEntryRequest(
    decimal Amount,
    string Description,
    string? PaymentReference,
    DateTime? PeriodStart,
    DateTime? PeriodEnd);

/// <summary>Recording an adjustment: direction is explicit, and the description is the audit.</summary>
public record RecordAdjustmentRequest(
    decimal Amount,
    bool IsCredit,
    string Description);

/// <summary>Recording money paid out to a referrer.</summary>
public record RecordReferralPayoutRequest(
    Guid ReferrerAppUserId,
    decimal Amount,
    string Description,
    string? PaymentReference);

public record TaxRateRuleRecord(
    Guid Id,
    string State,
    decimal RatePercent,
    string? Notes,
    DateTime DateCreated,
    DateTime? DateUpdated);

public record SaveTaxRateRuleRequest(string State, decimal RatePercent, string? Notes);

/// <summary>
/// One referrer's standing: what their coupons brought in, what has been paid out to them.
/// The platform does not compute what is OWED — that rule (percentage? flat per redemption?)
/// is a decision Ben has not made — it shows both sides so a human can.
/// </summary>
public record ReferrerSummaryRecord(
    Guid ReferrerAppUserId,
    string ReferrerName,
    int CouponCount,
    int RedemptionCount,
    decimal RevenueAttributed,
    decimal DiscountGiven,
    decimal PaidOut,
    // Ben's rule: percent of revenue, per campaign. Owed sums only campaigns WITH a percent;
    // the flag says when some lack one, so a low figure reads as incomplete rather than final.
    decimal Owed = 0m,
    bool EveryCampaignHasAPercent = true)
{
    public decimal Balance => Owed - PaidOut;
}

/// <summary>One overflow seat (item 144) as the admin sees it.</summary>
public record MemberSeatAdminRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    Guid AppUserId,
    string MemberName,
    SubscriptionStatus Status,
    BillingInterval Interval,
    decimal PriceAtStart,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    DateTime DateCreated);

/// <summary>Setting a seat's standing — the manual billing flow, same as group subscriptions.</summary>
public record SetMemberSeatRequest(
    SubscriptionStatus Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd);

/// <summary>A seat as its holder sees it on the Pricing page.</summary>
public record MyMemberSeatRecord(
    Guid OrganizationId,
    string OrganizationName,
    SubscriptionStatus Status,
    BillingInterval Interval,
    decimal PriceAtStart,
    DateTime? CurrentPeriodEnd);

/// <summary>The org-side view of the same ledger: no referrer rows, no admin names.</summary>
public record OrgBillingHistoryRecord(
    Guid Id,
    BillingLedgerKind Kind,
    decimal Amount,
    bool AdjustmentIsCredit,
    decimal TaxRatePercent,
    decimal TaxAmount,
    string Description,
    string? PaymentReference,
    int? ReceiptNumber,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    DateTime DateCreated);
