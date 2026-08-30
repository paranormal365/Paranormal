using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing.StripeIntegration;

/// <summary>
/// Charges the saved card as a period runs out, so an Active group stays active.
/// </summary>
/// <remarks>
/// <para><b>Renewal is re-banding time.</b> The price is computed fresh from the LIVE tier list
/// and the CURRENT member count — the PeriodOpener contract: a queued price reduction lands at
/// renewal precisely because renewal reads the live terms, and a group that grew re-bands the
/// same way. What was frozen last period protects last period, not this one.</para>
///
/// <para><b>A multi-period coupon keeps its promise here.</b> "50% off your first three periods"
/// was shown at the quote; the redemption's meter (<c>PeriodsRemaining</c>) says whether this
/// renewal is still inside the promise, and fulfillment moves the meter. Forever coupons have no
/// meter and simply keep applying.</para>
///
/// <para><b>Failure is the lapse machinery's job, not this one's.</b> A declined card is logged
/// and left: the pre-renewal notices have already warned, tomorrow's pass retries with a fresh
/// idempotency key, and if nothing lands before the period ends, <c>SubscriptionLapseJob</c>
/// winds the group down exactly as if no card existed. One consequence engine, not two.</para>
///
/// <para><b>Double-charge safety is layered:</b> the Stripe idempotency key caps each
/// subscription at one charge attempt per period per day; the synchronous fulfillment after a
/// success advances <c>CurrentPeriodEnd</c>, which removes the subscription from tomorrow's
/// eligibility; and fulfillment itself is idempotent by payment reference, so the webhook's
/// later delivery of the same success is a no-op.</para>
/// </remarks>
public sealed class StripeRenewalJob : IScheduledJob
{
    /// <summary>How close to its end a period must be before its renewal is charged.</summary>
    /// <remarks>A day: early enough that a transient decline gets retried before anything
    /// lapses, late enough that a person cancelling mid-period was almost always heard.</remarks>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(1);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IStripeGateway _stripe;
    private readonly StripeFulfillmentService _fulfillment;
    private readonly ILogger<StripeRenewalJob> _log;

    public StripeRenewalJob(
        IDbContextFactory<BenDataContext> dbFactory, IStripeGateway stripe,
        StripeFulfillmentService fulfillment, ILogger<StripeRenewalJob> log)
    {
        _dbFactory = dbFactory;
        _stripe = stripe;
        _fulfillment = fulfillment;
        _log = log;
    }

    public string Name => "stripe-renewals";

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_stripe.IsConfigured) return;

        var now = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Due: an Active, uncancelled Stripe subscription whose period ends inside the window —
        // or has already ended without lapsing yet, which is a decline being retried.
        var due = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.ProviderName == "Stripe"
                     && s.Status == SubscriptionStatus.Active
                     && !s.CancelAtPeriodEnd
                     && s.ProviderCustomerRef != null
                     && s.ProviderPaymentMethodRef != null
                     && s.CurrentPeriodEnd != null
                     && s.CurrentPeriodEnd <= now + RenewalWindow)
            .ToListAsync(ct);

        foreach (var sub in due)
        {
            try
            {
                await RenewOneAsync(db, sub, now, ct);
            }
            catch (Exception ex)
            {
                // One group's bad state must not stop the rest of the run — the scheduler's own
                // rule, applied per row.
                _log.LogError(ex, "Renewal failed unexpectedly for organization {OrganizationId}.",
                    sub.OrganizationId);
            }
        }

        // ── overflow seats, same window, their holder's own card ─────────────
        var dueSeats = await db.MemberSeatSubscriptions.AsNoTracking()
            .Include(s => s.Organization)
            .Where(s => s.ProviderName == "Stripe"
                     && s.Status == SubscriptionStatus.Active
                     && s.ProviderCustomerRef != null
                     && s.ProviderPaymentMethodRef != null
                     && s.CurrentPeriodEnd != null
                     && s.CurrentPeriodEnd <= now + RenewalWindow)
            .ToListAsync(ct);

        foreach (var seat in dueSeats)
        {
            try
            {
                await RenewSeatAsync(db, seat, now, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Seat renewal failed unexpectedly for seat {SeatId}.", seat.Id);
            }
        }
    }

    private async Task RenewSeatAsync(
        BenDataContext db, Ben.Data.Source.Entities.MemberSeatSubscription seat,
        DateTime now, CancellationToken ct)
    {
        // A member who left the group is not charged for a seat they no longer occupy — the
        // seat simply runs out. Nothing lapses it; an expired seat with an inactive membership
        // is its own explanation on the admin screen.
        var stillMember = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == seat.OrganizationId
                        && m.AppUserId == seat.AppUserId && m.IsActive, ct);
        if (!stillMember)
        {
            _log.LogInformation(
                "Seat {SeatId} not renewed — the member has left {OrganizationName}.",
                seat.Id, seat.Organization.Name);
            return;
        }

        // The frozen price, forever: the offer said what the ride costs, and unlike the group's
        // banded subscription there is no live world to re-read — a seat has no band.
        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, seat.OrganizationId, ct);
        var tax = TaxResolver.TaxOn(seat.PriceAtStart, taxRate);
        var periodStart = seat.CurrentPeriodEnd!.Value;

        var outcome = await _stripe.ChargeSavedCardAsync(new StripeRenewalCharge(
            seat.ProviderCustomerRef!, seat.ProviderPaymentMethodRef!,
            seat.PriceAtStart + tax,
            $"IsHaunted member seat renewal — {seat.Organization.Name}",
            new Dictionary<string, string>
            {
                [StripeFulfillmentService.CheckoutFacts.Keys.Seat] = seat.Id.ToString(),
                [StripeFulfillmentService.CheckoutFacts.Keys.PeriodStart] = periodStart.ToString("O"),
            },
            IdempotencyKey: $"seat-{seat.Id:N}-{periodStart:yyyyMMdd}-{now:yyyyMMdd}"), ct);

        if (!outcome.Succeeded)
        {
            _log.LogWarning(
                "Seat renewal declined for {SeatId}: {Reason} (intent {Intent}). Tomorrow retries.",
                seat.Id, outcome.FailureReason, outcome.PaymentIntentRef);
            return;
        }

        await _fulfillment.FulfillAsync(new StripeCompletedCheckout(
            outcome.PaymentIntentRef, outcome.PaymentIntentRef,
            seat.ProviderCustomerRef, seat.ProviderPaymentMethodRef,
            new Dictionary<string, string>
            {
                [StripeFulfillmentService.CheckoutFacts.Keys.Seat] = seat.Id.ToString(),
                [StripeFulfillmentService.CheckoutFacts.Keys.PeriodStart] = periodStart.ToString("O"),
            }), ct);
    }

    private async Task RenewOneAsync(
        BenDataContext db, Ben.Data.Source.Entities.OrganizationSubscription sub,
        DateTime now, CancellationToken ct)
    {
        // ── re-band from the live world ──────────────────────────────────────
        var tiers = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.Prices).ToListAsync(ct);
        if (SubscriptionTierResolver.Validate(tiers) is { } broken)
        {
            _log.LogError("Renewals cannot price: {Problem}. Nothing was charged.", broken);
            return;
        }

        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == sub.OrganizationId && m.IsActive, ct);
        var tier = SubscriptionTierResolver.Resolve(tiers, members);

        if (SubscriptionPricing.PriceFor(tier, sub.Interval) is not { } listPrice)
        {
            // The tier stopped selling this cadence since last period. Charging a different
            // cadence than agreed is not an option; the lapse machinery will speak for us.
            _log.LogWarning(
                "Organization {OrganizationId} renews {Interval} but \"{Tier}\" no longer sells it — skipped.",
                sub.OrganizationId, sub.Interval, tier.Name);
            return;
        }

        // ── the coupon's continuing promise ──────────────────────────────────
        var payable = listPrice;
        var discount = 0m;
        string? couponCode = null;
        var redemption = await db.CouponRedemptions.AsNoTracking()
            .Include(r => r.Coupon)
            .Where(r => r.OrganizationId == sub.OrganizationId)
            .OrderByDescending(r => r.RedeemedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (redemption is not null && CouponMath.IsStillApplying(redemption))
        {
            var price = CouponMath.PriceFor(listPrice, redemption.Coupon);
            payable = price.Payable;
            discount = price.Discount;
            couponCode = await db.CouponCodes.AsNoTracking()
                .Where(c => c.Id == redemption.CouponCodeId)
                .Select(c => c.Code)
                .FirstOrDefaultAsync(ct);
        }

        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, sub.OrganizationId, ct);
        var tax = TaxResolver.TaxOn(payable, taxRate);

        var periodStart = sub.CurrentPeriodEnd!.Value;
        var facts = new StripeFulfillmentService.CheckoutFacts(
            sub.OrganizationId, tier.Id, sub.Interval, members,
            payable, taxRate, tax,
            // Renewal has no person at a keyboard; the row is attributed to whoever set the
            // subscription up, which is also who the pre-renewal notices were addressed to.
            sub.UpdatedByAppUserId ?? sub.CreatedByAppUserId,
            couponCode, listPrice, discount, periodStart);

        // ── a free continuing period skips the card entirely ─────────────────
        if (payable == 0m)
        {
            await _fulfillment.FulfillAsync(new StripeCompletedCheckout(
                $"renew-free-{sub.Id:N}-{periodStart:yyyyMMdd}",
                null, sub.ProviderCustomerRef, sub.ProviderPaymentMethodRef,
                facts.ToMetadata()), ct);
            return;
        }

        var outcome = await _stripe.ChargeSavedCardAsync(new StripeRenewalCharge(
            sub.ProviderCustomerRef!, sub.ProviderPaymentMethodRef!,
            payable + tax,
            $"IsHaunted renewal — {tier.Name}, {members} members",
            facts.ToMetadata(),
            IdempotencyKey: $"renew-{sub.Id:N}-{periodStart:yyyyMMdd}-{now:yyyyMMdd}"), ct);

        if (!outcome.Succeeded)
        {
            _log.LogWarning(
                "Renewal charge declined for organization {OrganizationId}: {Reason} (intent {Intent}). "
              + "Tomorrow's pass retries; SubscriptionLapseJob owns the consequence.",
                sub.OrganizationId, outcome.FailureReason, outcome.PaymentIntentRef);
            return;
        }

        // Fulfilled here and now rather than waiting on the webhook, so a renewal is never
        // hostage to webhook registration; the webhook's own delivery no-ops by reference.
        await _fulfillment.FulfillAsync(new StripeCompletedCheckout(
            outcome.PaymentIntentRef, outcome.PaymentIntentRef,
            sub.ProviderCustomerRef, sub.ProviderPaymentMethodRef,
            facts.ToMetadata()), ct);
    }
}
