using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing.StripeIntegration;

/// <summary>
/// Turns "Stripe says they paid" into everything the manual admin path does by hand.
/// </summary>
/// <remarks>
/// <para><b>The metadata is the contract.</b> Amounts, tier, member count, coupon and tax were
/// computed by our engine at checkout creation and frozen into the session's metadata; the person
/// paid exactly that. Fulfillment therefore does not re-price anything — re-resolving the tier
/// here would bill people an amount nobody showed them, the moment a member joins between click
/// and card.</para>
///
/// <para><b>Mirrors AdminOrganizationSubscriptionController.Set deliberately</b> — same
/// PeriodOpener call, same snapshot replacement, same coupon redemption, same lapse-restore.
/// Where the two ever differ, the manual path is the specification.</para>
///
/// <para><b>Idempotent by payment reference.</b> Stripe retries webhooks until acknowledged and
/// may deliver twice; the ledger's payment row for the intent is the fact that fulfillment
/// already happened. One period, one receipt, however many deliveries.</para>
/// </remarks>
public sealed class StripeFulfillmentService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<StripeFulfillmentService> _log;

    public StripeFulfillmentService(
        IDbContextFactory<BenDataContext> dbFactory, ILogger<StripeFulfillmentService> log)
    {
        _dbFactory = dbFactory;
        _log = log;
    }

    /// <summary>The frozen facts a checkout was created with, read back from metadata.</summary>
    public sealed record CheckoutFacts(
        Guid OrganizationId, Guid TierId, BillingInterval Interval, int MemberCount,
        decimal Payable, decimal TaxRatePercent, decimal TaxAmount,
        Guid InitiatedByUserId, string? CouponCode,
        decimal ListPrice, decimal Discount)
    {
        public static class Keys
        {
            public const string Organization = "ih_org";
            public const string Tier         = "ih_tier";
            public const string Interval     = "ih_interval";
            public const string Members      = "ih_members";
            public const string Payable      = "ih_payable";
            public const string TaxRate      = "ih_tax_rate";
            public const string TaxAmount    = "ih_tax_amount";
            public const string User         = "ih_user";
            public const string Coupon       = "ih_coupon";
            public const string List         = "ih_list";
            public const string Discount     = "ih_discount";
        }

        public Dictionary<string, string> ToMetadata() => new()
        {
            [Keys.Organization] = OrganizationId.ToString(),
            [Keys.Tier]         = TierId.ToString(),
            [Keys.Interval]     = ((int)Interval).ToString(),
            [Keys.Members]      = MemberCount.ToString(),
            [Keys.Payable]      = Payable.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [Keys.TaxRate]      = TaxRatePercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [Keys.TaxAmount]    = TaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [Keys.User]         = InitiatedByUserId.ToString(),
            [Keys.Coupon]       = CouponCode ?? string.Empty,
            [Keys.List]         = ListPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [Keys.Discount]     = Discount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        /// <summary>Null when the metadata is not ours or is torn — a session created by
        /// something else must be ignored, not guessed at.</summary>
        public static CheckoutFacts? FromMetadata(IReadOnlyDictionary<string, string> m)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!m.TryGetValue(Keys.Organization, out var org) || !Guid.TryParse(org, out var orgId)) return null;
            if (!m.TryGetValue(Keys.Tier, out var tier) || !Guid.TryParse(tier, out var tierId)) return null;
            if (!m.TryGetValue(Keys.Interval, out var iv) || !int.TryParse(iv, out var ivInt)) return null;
            if (!m.TryGetValue(Keys.Members, out var mem) || !int.TryParse(mem, out var members)) return null;
            if (!m.TryGetValue(Keys.Payable, out var pay) || !decimal.TryParse(pay, System.Globalization.NumberStyles.Number, inv, out var payable)) return null;
            if (!m.TryGetValue(Keys.TaxRate, out var tr) || !decimal.TryParse(tr, System.Globalization.NumberStyles.Number, inv, out var taxRate)) return null;
            if (!m.TryGetValue(Keys.TaxAmount, out var ta) || !decimal.TryParse(ta, System.Globalization.NumberStyles.Number, inv, out var taxAmount)) return null;
            if (!m.TryGetValue(Keys.User, out var usr) || !Guid.TryParse(usr, out var userId)) return null;
            m.TryGetValue(Keys.Coupon, out var coupon);
            // List/discount arrived later than the other keys; sessions created before them
            // read back as an undiscounted sale of the payable amount, which is the truth
            // those sessions were sold at.
            decimal list = payable, discount = 0m;
            if (m.TryGetValue(Keys.List, out var l))
                decimal.TryParse(l, System.Globalization.NumberStyles.Number, inv, out list);
            if (m.TryGetValue(Keys.Discount, out var d))
                decimal.TryParse(d, System.Globalization.NumberStyles.Number, inv, out discount);

            return new CheckoutFacts(orgId, tierId, (BillingInterval)ivInt, members,
                payable, taxRate, taxAmount, userId,
                string.IsNullOrWhiteSpace(coupon) ? null : coupon,
                list, discount);
        }
    }

    /// <summary>
    /// Records the payment and opens the period. Safe to call twice with the same checkout.
    /// </summary>
    public async Task FulfillAsync(StripeCompletedCheckout checkout, CancellationToken ct = default)
    {
        var facts = CheckoutFacts.FromMetadata(checkout.Metadata);
        if (facts is null)
        {
            // Not ours (or torn). Logged loudly rather than thrown: throwing makes Stripe retry a
            // delivery that will never become ours.
            _log.LogWarning("Stripe checkout {SessionId} completed without usable metadata — ignored.",
                checkout.SessionId);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // ── idempotency: the ledger row carrying this reference is the fact it already ran ──
        // The reference goes on the CHARGE row as well as the payment, so the free-coupon path
        // (which records a charge of zero and no payment — nothing was paid) is just as safe to
        // deliver twice as the paid one.
        var reference = checkout.PaymentIntentRef ?? checkout.SessionId;
        if (await db.BillingLedgerEntries.AnyAsync(e => e.PaymentReference == reference, ct))
        {
            _log.LogInformation("Stripe checkout {SessionId} delivered again — already fulfilled.",
                checkout.SessionId);
            return;
        }

        var tier = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.Prices).Include(t => t.Limits)
            .FirstOrDefaultAsync(t => t.Id == facts.TierId, ct);
        if (tier is null)
        {
            // A paid session naming a tier that no longer exists is money taken for nothing —
            // the loudest log level short of throwing, and the admin ledger will show the
            // payment row so the money is at least visible.
            _log.LogError("Stripe checkout {SessionId} paid for tier {TierId} which no longer exists.",
                checkout.SessionId, facts.TierId);
        }

        var now = DateTime.UtcNow;
        var sub = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == facts.OrganizationId, ct);
        var isNew = sub is null;
        sub ??= new OrganizationSubscription
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = facts.OrganizationId,
            DateCreated        = now,
            CreatedByAppUserId = facts.InitiatedByUserId,
        };
        var wasLapsed = sub.Status == SubscriptionStatus.Lapsed;

        // ── the coupon, redeemed where the money is recorded — as on the manual path ──
        if (facts.CouponCode is { } typedCode)
            await RedeemCouponAsync(db, sub, facts, typedCode, now, ct);

        // ── the period, via the one opener every provider shares ──
        sub.CancelAtPeriodEnd = false;
        var periodStart = now;
        var periodEnd   = now.AddMonths((int)facts.Interval);
        var snapshot = PeriodOpener.Open(
            sub, tier, SubscriptionStatus.Active, facts.Interval,
            periodStart, periodEnd, facts.MemberCount, facts.InitiatedByUserId);

        // The person paid the quoted (possibly discounted) amount; the period records what was
        // actually charged, not the list price the opener read off the tier.
        sub.PriceAtPeriodStart = facts.Payable;
        if (snapshot is not null)
        {
            snapshot.Price = facts.Payable;
            await PeriodOpener.ReplaceSnapshotAsync(db, sub.Id, snapshot.PeriodStartUtc, ct);
            db.SubscriptionContractTerms.Add(snapshot);
        }

        sub.ProviderName             = "Stripe";
        sub.ProviderSubscriptionRef  = checkout.SessionId;
        sub.ProviderCustomerRef      = checkout.CustomerRef ?? sub.ProviderCustomerRef;
        sub.ProviderPaymentMethodRef = checkout.PaymentMethodRef ?? sub.ProviderPaymentMethodRef;

        if (isNew) db.OrganizationSubscriptions.Add(sub);
        else { sub.DateUpdated = now; sub.UpdatedByAppUserId = facts.InitiatedByUserId; }

        if (wasLapsed)
        {
            await PeriodOpener.RestorePausedCasesAsync(db, facts.OrganizationId, now, ct);
            sub.StrandedClientNoticeSentAtUtc = null;
        }

        // ── the money trail: a charge with frozen tax, and the payment that settles it ──
        var description = $"{tier?.Name ?? "Subscription"} — {Cadence(facts.Interval)}"
                        + (facts.CouponCode is null ? "" : $" (coupon {facts.CouponCode})");
        db.BillingLedgerEntries.Add(new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Charge,
            OrganizationId = facts.OrganizationId,
            Amount = facts.Payable, TaxRatePercent = facts.TaxRatePercent, TaxAmount = facts.TaxAmount,
            Description = description,
            PaymentReference = reference,
            PeriodStart = periodStart, PeriodEnd = periodEnd,
            DateCreated = now, CreatedByAppUserId = facts.InitiatedByUserId,
        });

        // A 100%-off period paid nothing, so there is no payment to record and no receipt to
        // number — the zero-amount charge above, naming its coupon, is the whole money trail
        // (item 195's rule). Everything else about the period opened exactly as if paid.
        if (checkout.PaymentIntentRef is null && facts.Payable == 0m)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation(
                "Stripe-free fulfilled: org {OrganizationId} on \"{Tier}\" {Interval} at $0 (coupon {Coupon}).",
                facts.OrganizationId, tier?.Name, facts.Interval, facts.CouponCode);
            return;
        }

        var payment = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment,
            OrganizationId = facts.OrganizationId,
            Amount = facts.Payable, TaxRatePercent = facts.TaxRatePercent, TaxAmount = facts.TaxAmount,
            Description = $"Card payment — {description}",
            PaymentReference = reference,
            PeriodStart = periodStart, PeriodEnd = periodEnd,
            DateCreated = now, CreatedByAppUserId = facts.InitiatedByUserId,
        };

        // The same next-number-with-unique-index dance the admin path does; the index referees.
        for (var attempt = 0; ; attempt++)
        {
            payment.ReceiptNumber = 1 + await db.BillingLedgerEntries
                .MaxAsync(e => (int?)e.ReceiptNumber, ct) ?? 1;
            db.BillingLedgerEntries.Add(payment);
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                db.BillingLedgerEntries.Remove(payment);
            }
        }

        _log.LogInformation(
            "Stripe fulfilled: org {OrganizationId} on \"{Tier}\" {Interval} for ${Payable} (+${Tax} tax), receipt R-{Receipt:00000}.",
            facts.OrganizationId, tier?.Name, facts.Interval, facts.Payable, facts.TaxAmount, payment.ReceiptNumber);
    }

    private static async Task RedeemCouponAsync(
        BenDataContext db, OrganizationSubscription sub, CheckoutFacts facts,
        string typedCode, DateTime now, CancellationToken ct)
    {
        var normalised = CouponCodeGenerator.Normalise(typedCode);
        var code = await db.CouponCodes.Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.Code == normalised, ct);
        if (code is null) return;   // validated at checkout creation; a code deleted since is a no-op, not a lost payment

        var alreadyRedeemed = await db.CouponRedemptions
            .AnyAsync(r => r.CouponId == code.CouponId && r.OrganizationId == facts.OrganizationId, ct);
        if (alreadyRedeemed) return;

        db.CouponRedemptions.Add(new CouponRedemption
        {
            Id = Guid.NewGuid(),
            CouponId = code.CouponId, CouponCodeId = code.Id,
            OrganizationId = facts.OrganizationId,
            PeriodsRemaining = CouponMath.PeriodsFor(code.Coupon) is { } periods ? periods - 1 : null,
            RedeemedAtUtc = now,
            // Frozen at checkout creation, like every other fact — reimbursement math must
            // survive later price edits (the CouponRedemption.ListPrice rule).
            ListPrice = facts.ListPrice, Discount = facts.Discount, Payable = facts.Payable,
            DateCreated = now, CreatedByAppUserId = facts.InitiatedByUserId,
        });
        code.RedemptionCount++;
        code.Coupon.RedemptionCount++;
    }

    private static string Cadence(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly    => "monthly",
        BillingInterval.Quarterly  => "quarterly",
        BillingInterval.HalfYearly => "every six months",
        BillingInterval.Yearly     => "yearly",
        _                          => interval.ToString().ToLowerInvariant(),
    };
}
