using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// What a tour added part-way through a paid period costs, and collecting it (item 233).
/// </summary>
/// <remarks>
/// <para><b>The rule, decided with Ben 2026-09-10:</b> a business pays per tour, so a second tour
/// bought on day ten of a month owes the twenty days it will actually be on sale for — charged
/// today, to the card already on file, and recorded in the ledger like any other money. The
/// alternative Ben turned down was counting it only at renewal, which on a yearly plan would hand
/// a business up to a year of a second tour for nothing.</para>
///
/// <para><b>A refusal never blocks the tour.</b> No card, a decline, no subscription at all: the
/// tour is created regardless and the renewal simply counts it, because a business locked out of
/// its own product over a payment problem cannot fix the payment problem. The lapse machinery
/// already owns the consequence of not paying, and this must not become a second engine for it.</para>
/// </remarks>
public sealed class TourAddOnService
{
    private readonly IStripeGateway _stripe;
    private readonly ILogger<TourAddOnService> _log;

    public TourAddOnService(IStripeGateway stripe, ILogger<TourAddOnService> log)
    { _stripe = stripe; _log = log; }

    /// <summary>What happened about the money, in a sentence the business can be shown.</summary>
    public sealed record Outcome(decimal Charged, string Note);

    /// <summary>
    /// Brings the subscription's paid-for tour count up to the live one, charging the remainder.
    /// </summary>
    /// <remarks>Saves its own changes. Never throws for a payment problem.</remarks>
    public async Task<Outcome> ChargeRemainderAsync(
        BenDataContext db, Organization org, Guid byUserId, CancellationToken ct)
    {
        try
        {
            return await ChargeInnerAsync(db, org, byUserId, ct);
        }
        catch (Exception ex)
        {
            // The class promises it never throws for a payment problem, and the promise has to
            // hold for the ledger write as well as for the card: an exception after the charge
            // left no rows, an unraised count, and the same tour charged for again tomorrow.
            _log.LogError(ex,
                "The tour add-on charge failed unexpectedly for organization {OrganizationId}; "
              + "the tour stands and the renewal will count it.", org.Id);
            return new Outcome(0m,
                "Something went wrong taking payment for this tour. It is still yours to run, and "
              + "your next renewal will include it.");
        }
    }

    private async Task<Outcome> ChargeInnerAsync(
        BenDataContext db, Organization org, Guid byUserId, CancellationToken ct)
    {
        if (!SubscriptionTierResolver.IsBusinessKind(org.Kind))
            return new Outcome(0m, "This group is priced by its members, not by its tours.");

        // One at a time per business. Two admins adding a tour in the same minute both read the
        // old paid-for count, compute different extras, send different idempotency keys, and the
        // card is charged for three units where two were added. The lock is held across the read,
        // the charge and the write, so the second caller sees the first one's count.
        var gate = OneAtATime(org.Id);
        await gate.WaitAsync(ct);
        try
        {
            return await PricedAsync(db, org, byUserId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Outcome> PricedAsync(
        BenDataContext db, Organization org, Guid byUserId, CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == org.Id, ct);

        var liveTours = await db.Tours
            .CountAsync(t => t.OrganizationId == org.Id && t.RetiredAtUtc == null, ct);
        var units = TourBilling.Units(org.Kind, liveTours);

        if (sub is null || sub.Status != SubscriptionStatus.Active)
            return new Outcome(0m,
                units <= 1
                    ? "Your first tour is what a plan covers. Nothing is owed until you subscribe."
                    : $"You run {units} tours. They will be priced together when you subscribe.");

        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices).ToListAsync(ct);
        var tier = tiers.FirstOrDefault(t => t.Id == sub.SubscriptionTierId);
        if (tier is null || tier.IsBandedByMembers
            || SubscriptionPricing.PriceFor(tier, sub.Interval) is not { } unitPrice)
            return new Outcome(0m, "This plan is not priced per tour, so nothing changed.");

        if (units <= sub.TourCountAtPeriodStart)
            return new Outcome(0m, "Covered by the tours you have already paid for this period.");

        if (sub.CurrentPeriodStart is not { } periodStart || sub.CurrentPeriodEnd is not { } periodEnd)
            return new Outcome(0m, "This tour is counted at your next renewal.");

        var extra = units - sub.TourCountAtPeriodStart;
        var now = DateTime.UtcNow;
        var payable = TourBilling.Remainder(unitPrice, periodStart, periodEnd, now) * extra;

        // A coupon that is still running applies here too. Without this, a business three days
        // into "your first three months are free" was charged real money for its second tour —
        // the renewal job honours the promise and this did not.
        var redemption = await db.CouponRedemptions.AsNoTracking()
            .Include(r => r.Coupon)
            .Where(r => r.OrganizationId == org.Id)
            .OrderByDescending(r => r.RedeemedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (redemption is not null && CouponMath.IsStillApplying(redemption))
            payable = CouponMath.PriceFor(payable, redemption.Coupon).Payable;

        payable = Math.Round(payable, 2, MidpointRounding.AwayFromZero);

        // The period is all but over. Nothing meaningful is owed for the hours left, and the
        // renewal a day away will price every tour properly.
        if (payable <= 0m)
        {
            sub.TourCountAtPeriodStart = units;
            sub.DateUpdated = now;
            sub.UpdatedByAppUserId = byUserId;
            await db.SaveChangesAsync(ct);
            return new Outcome(0m,
                redemption is not null && CouponMath.IsStillApplying(redemption)
                    ? $"Nothing to pay — your coupon covers this period. Your renewal on "
                      + $"{periodEnd:MM/dd/yyyy} will be for {units} tours."
                    : $"Nothing more this period — your renewal on {periodEnd:MM/dd/yyyy} covers {units} tours.");
        }

        if (!_stripe.IsConfigured
            || sub.ProviderCustomerRef is null || sub.ProviderPaymentMethodRef is null)
            return new Outcome(0m,
                $"There is no card on file, so this tour is counted at your renewal on {periodEnd:MM/dd/yyyy}.");

        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, org.Id, ct);
        var tax = TaxResolver.TaxOn(payable, taxRate);
        var daysLeft = Math.Max(1, (int)Math.Ceiling((periodEnd - now).TotalDays));
        var description = extra == 1
            ? $"Tour added — {daysLeft} days of this {Cadence(sub.Interval)} period"
            : $"{extra} tours added — {daysLeft} days of this {Cadence(sub.Interval)} period";

        var outcome = await _stripe.ChargeSavedCardAsync(new StripeRenewalCharge(
            sub.ProviderCustomerRef, sub.ProviderPaymentMethodRef,
            payable + tax, description,
            new Dictionary<string, string>
            {
                ["ih_org"] = org.Id.ToString(),
                ["ih_tour_addon"] = units.ToString(),
            },
            // One charge per period per resulting count: a double click, or a retry after a
            // timeout, cannot bill the same tour twice.
            // Keyed on where the count LANDS and how far it moved. Two callers that agree on the
            // destination but arrived from different places are charging different amounts, and
            // one key for both would silently make the second one free.
            IdempotencyKey: $"touradd-{org.Id:N}-{periodStart:yyyyMMdd}-{sub.TourCountAtPeriodStart}-{units}"), ct);

        if (!outcome.Succeeded)
        {
            _log.LogWarning(
                "Tour add-on charge declined for organization {OrganizationId}: {Reason}. "
              + "The tour stands; the renewal will count it.", org.Id, outcome.FailureReason);
            return new Outcome(0m,
                "Your card was declined, so this tour has not been paid for yet. It is still "
              + "yours to run, and your next renewal will include it.");
        }

        db.BillingLedgerEntries.Add(new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Charge,
            OrganizationId = org.Id,
            Amount = payable, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = description, PaymentReference = outcome.PaymentIntentRef,
            PeriodStart = now, PeriodEnd = periodEnd,
            DateCreated = now, CreatedByAppUserId = byUserId,
        });
        var payment = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment,
            OrganizationId = org.Id,
            Amount = payable, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = $"Card payment — {description}", PaymentReference = outcome.PaymentIntentRef,
            PeriodStart = now, PeriodEnd = periodEnd,
            DateCreated = now, CreatedByAppUserId = byUserId,
        };

        sub.TourCountAtPeriodStart = units;
        sub.PriceAtPeriodStart += payable;
        sub.DateUpdated = now;
        sub.UpdatedByAppUserId = byUserId;

        // The receipt number is allocated the way every other payment allocates it: read the
        // highest, try, and try again if somebody else took it first.
        for (var attempt = 0; ; attempt++)
        {
            payment.ReceiptNumber = 1 + await db.BillingLedgerEntries
                .MaxAsync(e => (int?)e.ReceiptNumber, ct) ?? 1;
            db.BillingLedgerEntries.Add(payment);
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) when (attempt < 2) { db.BillingLedgerEntries.Remove(payment); }
        }

        _log.LogInformation(
            "Charged ${Payable} to organization {OrganizationId} for tour {Count} of this period.",
            payable, org.Id, units);

        return new Outcome(payable,
            $"Charged ${payable:0.00} for the {daysLeft} days left in this period. Your renewal "
          + $"on {periodEnd:MM/dd/yyyy} will be for {units} tours.");
    }

    /// <summary>What the plan says about tours right now, without changing anything.</summary>
    public static async Task<(bool IsPerTour, string TierName, decimal UnitPrice, BillingInterval Interval,
                              int Covered, int Active, decimal? NextCostsToday, DateTime? PeriodEnd, bool HasCard)>
        DescribeAsync(BenDataContext db, Organization org, CancellationToken ct)
    {
        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices).ToListAsync(ct);
        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == org.Id, ct);
        var active = await db.Tours.CountAsync(t => t.OrganizationId == org.Id && t.RetiredAtUtc == null, ct);

        var interval = sub?.Interval ?? BillingInterval.Monthly;
        var tier = sub?.SubscriptionTierId is { } id ? tiers.FirstOrDefault(t => t.Id == id) : null;
        tier ??= SubscriptionTierResolver.BusinessTier(tiers);

        var isPerTour = SubscriptionTierResolver.IsBusinessKind(org.Kind)
                        && tier is { IsBandedByMembers: false };
        var unitPrice = tier is null ? 0m : SubscriptionPricing.PriceFor(tier, interval) ?? 0m;

        decimal? next = null;
        if (isPerTour && sub is { Status: SubscriptionStatus.Active, CurrentPeriodStart: { } ps, CurrentPeriodEnd: { } pe })
            next = TourBilling.Remainder(unitPrice, ps, pe, DateTime.UtcNow);

        return (isPerTour, tier?.Name ?? "No plan", unitPrice, interval,
                sub?.TourCountAtPeriodStart ?? 0, active, next, sub?.CurrentPeriodEnd,
                sub?.ProviderPaymentMethodRef is not null);
    }

    /// <summary>
    /// One in-process lock per business, so two tours added at once are priced in turn.
    /// </summary>
    /// <remarks>
    /// In-process is honest about what it covers: one web server. A second server would need the
    /// database to arbitrate, and the idempotency key is what stops that case double-charging a
    /// card — this is what stops the far likelier one, two people in the same office.
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private static SemaphoreSlim OneAtATime(Guid organizationId)
        => Locks.GetOrAdd(organizationId, _ => new SemaphoreSlim(1, 1));

    private static string Cadence(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly    => "monthly",
        BillingInterval.Quarterly  => "quarterly",
        BillingInterval.HalfYearly => "six-month",
        BillingInterval.Yearly     => "yearly",
        _                          => interval.ToString().ToLowerInvariant(),
    };
}
