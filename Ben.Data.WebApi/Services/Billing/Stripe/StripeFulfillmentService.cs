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
        decimal ListPrice, decimal Discount,
        DateTime? PeriodStartUtc = null,
        // Item 233: the tours a business period is priced for. Zero for everyone the ladder
        // prices, and for sessions created before the key existed.
        int TourCount = 0)
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
            public const string PeriodStart  = "ih_period_start";
            /// <summary>Present exactly when this payment buys an overflow SEAT, not the
            /// group's subscription — the two fulfill along entirely different paths.</summary>
            public const string Seat         = "ih_seat";
            public const string Tours        = "ih_tours";

            /// <summary>
            /// Present exactly when this payment buys EVENT CREDITS (item 235) — a one-off
            /// purchase, not a subscription, and fulfilled down its own path.
            /// </summary>
            public const string EventCredits = "ih_event_credits";
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
            [Keys.PeriodStart]  = PeriodStartUtc?.ToString("O") ?? string.Empty,
            [Keys.Tours]        = TourCount.ToString(),
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

            DateTime? periodStart = null;
            if (m.TryGetValue(Keys.PeriodStart, out var psRaw) && !string.IsNullOrWhiteSpace(psRaw)
                && DateTime.TryParse(psRaw, inv, System.Globalization.DateTimeStyles.RoundtripKind, out var ps))
                periodStart = ps;

            var tours = 0;
            if (m.TryGetValue(Keys.Tours, out var tr2)) int.TryParse(tr2, out tours);

            return new CheckoutFacts(orgId, tierId, (BillingInterval)ivInt, members,
                payable, taxRate, taxAmount, userId,
                string.IsNullOrWhiteSpace(coupon) ? null : coupon,
                list, discount, periodStart, tours);
        }
    }

    /// <summary>
    /// Records the payment and opens the period. Safe to call twice with the same checkout.
    /// </summary>
    public async Task FulfillAsync(StripeCompletedCheckout checkout, CancellationToken ct = default)
    {
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.Seat, out var seatRaw)
            && Guid.TryParse(seatRaw, out var seatId))
        {
            await FulfillSeatAsync(seatId, checkout, ct);
            return;
        }

        // Event credits are a one-off purchase with no period and no subscription to open, so they
        // take their own path rather than being bent through the renewal machinery (item 235).
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.EventCredits, out var creditsRaw)
            && int.TryParse(creditsRaw, out var creditCount) && creditCount > 0)
        {
            await FulfillEventCreditsAsync(creditCount, checkout, ct);
            return;
        }

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
        // A first checkout starts NOW. A renewal charged before the old period ends starts where
        // that period stops — the metadata says which this is, so a card charged a day early
        // neither gifts a free day nor bills one twice.
        var periodStart = facts.PeriodStartUtc ?? now;
        var periodEnd   = periodStart.AddMonths((int)facts.Interval);
        // A session created before the tour key existed carries no count, and opening its period
        // at zero would tell the add-on service that NOTHING was paid for — so the next tour
        // added is charged for every tour the business runs. When the tier is per-tour and the
        // metadata is silent, count what is live instead of believing the zero.
        var tourCount = facts.TourCount;
        if (tourCount == 0 && tier is { IsBandedByMembers: false })
        {
            var kind = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == facts.OrganizationId).Select(o => o.Kind).FirstOrDefaultAsync(ct);
            if (Ben.Data.Source.Services.SubscriptionTierResolver.IsBusinessKind(kind))
                tourCount = Ben.Data.Source.Services.TourBilling.Units(
                    kind, await BillableUnits.ActiveToursAsync(db, facts.OrganizationId, ct));
        }

        var snapshot = PeriodOpener.Open(
            sub, tier, SubscriptionStatus.Active, facts.Interval,
            periodStart, periodEnd, facts.MemberCount, facts.InitiatedByUserId, tourCount);

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

    /// <summary>
    /// A paid seat: the member's PendingPayment becomes Active, and the money lands on the
    /// GROUP's ledger with the member named in the description.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately none of the organization path runs here — no PeriodOpener, no contract
    /// snapshot, no coupons. A seat is one person's flat-priced ride on a band the group already
    /// bought; giving it the group's machinery would let a seat payment rewrite the group's
    /// period, which is exactly the confusion the two tables exist to prevent.</para>
    /// <para>The ledger row is the org's because the ledger's schema says money belongs to a
    /// group or to a referrer — and the seat IS group revenue, paid by a member. The payment row
    /// carries the member as <c>CreatedByAppUserId</c>, which is also what lets the payer fetch
    /// their own receipt without settings permission.</para>
    /// </remarks>
    private async Task FulfillSeatAsync(
        Guid seatId, StripeCompletedCheckout checkout, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var reference = checkout.PaymentIntentRef ?? checkout.SessionId;
        if (await db.BillingLedgerEntries.AnyAsync(e => e.PaymentReference == reference, ct))
        {
            _log.LogInformation("Seat payment {Reference} delivered again — already fulfilled.", reference);
            return;
        }

        var seat = await db.MemberSeatSubscriptions
            .Include(s => s.AppUser)
            .FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null)
        {
            _log.LogError("Seat payment {Reference} names seat {SeatId} which does not exist.",
                reference, seatId);
            return;
        }

        var now = DateTime.UtcNow;
        // Renewals stitch onto the old period's end, first payments start now — the same rule
        // as the group path, carried in the same metadata key.
        DateTime periodStart = now;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.PeriodStart, out var psRaw)
            && DateTime.TryParse(psRaw, System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.RoundtripKind, out var ps))
            periodStart = ps;

        seat.Status             = SubscriptionStatus.Active;
        seat.CurrentPeriodStart = periodStart;
        seat.CurrentPeriodEnd   = periodStart.AddMonths((int)seat.Interval);
        seat.ProviderName       = "Stripe";
        seat.ProviderCustomerRef      = checkout.CustomerRef ?? seat.ProviderCustomerRef;
        seat.ProviderPaymentMethodRef = checkout.PaymentMethodRef ?? seat.ProviderPaymentMethodRef;
        seat.DateUpdated        = now;
        seat.UpdatedByAppUserId = seat.AppUserId;

        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, seat.OrganizationId, ct);
        var tax = TaxResolver.TaxOn(seat.PriceAtStart, taxRate);
        var description = $"Overflow seat — {seat.AppUser.DisplayName ?? seat.AppUser.Email} "
                        + $"({Cadence(seat.Interval)})";

        db.BillingLedgerEntries.Add(new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Charge,
            OrganizationId = seat.OrganizationId,
            Amount = seat.PriceAtStart, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = description, PaymentReference = reference,
            PeriodStart = seat.CurrentPeriodStart, PeriodEnd = seat.CurrentPeriodEnd,
            DateCreated = now, CreatedByAppUserId = seat.AppUserId,
        });
        var payment = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment,
            OrganizationId = seat.OrganizationId,
            Amount = seat.PriceAtStart, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = $"Card payment — {description}", PaymentReference = reference,
            PeriodStart = seat.CurrentPeriodStart, PeriodEnd = seat.CurrentPeriodEnd,
            DateCreated = now, CreatedByAppUserId = seat.AppUserId,
        };

        for (var attempt = 0; ; attempt++)
        {
            payment.ReceiptNumber = 1 + await db.BillingLedgerEntries
                .MaxAsync(e => (int?)e.ReceiptNumber, ct) ?? 1;
            db.BillingLedgerEntries.Add(payment);
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) when (attempt < 2) { db.BillingLedgerEntries.Remove(payment); }
        }

        _log.LogInformation(
            "Stripe fulfilled seat: {Member} in org {OrganizationId} for ${Price}, receipt R-{Receipt:00000}.",
            seat.AppUserId, seat.OrganizationId, seat.PriceAtStart, payment.ReceiptNumber);
    }

    /// <summary>
    /// Hands over the event credits somebody just bought, and writes the receipt.
    /// </summary>
    /// <remarks>
    /// <para><b>Idempotent on the payment.</b> Stripe delivers a webhook more than once as a matter
    /// of course, and a second delivery must not hand somebody a second set of credits. The ledger's
    /// payment reference is the lock, exactly as it is for a seat.</para>
    ///
    /// <para><b>Taxed like the digital service it is</b>, through the same resolver a subscription
    /// uses. A credit is a sale, not a donation.</para>
    ///
    /// <para><b>No period.</b> A credit is not a subscription: it has no renewal, nothing to open
    /// and nothing to close. It has a life — a year — and it is spent when an event is published.
    /// </para>
    /// </remarks>
    private async Task FulfillEventCreditsAsync(
        int count, StripeCompletedCheckout checkout, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var reference = checkout.PaymentIntentRef ?? checkout.SessionId;
        if (await db.BillingLedgerEntries.AnyAsync(e => e.PaymentReference == reference, ct))
        {
            _log.LogInformation(
                "Event-credit payment {Reference} delivered again — already fulfilled.", reference);
            return;
        }

        if (!checkout.Metadata.TryGetValue(CheckoutFacts.Keys.Organization, out var orgRaw)
            || !Guid.TryParse(orgRaw, out var organizationId))
        {
            _log.LogError("Event-credit payment {Reference} names no organization.", reference);
            return;
        }

        Guid buyerId = Guid.Empty;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.User, out var userRaw))
            Guid.TryParse(userRaw, out buyerId);

        var unit = Services.Events.EventCredits.DefaultPriceUsd;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.List, out var listRaw)
            && decimal.TryParse(listRaw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var listed)
            && count > 0)
            unit = decimal.Round(listed / count, 2);

        var now = DateTime.UtcNow;
        var total = unit * count;

        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, organizationId, ct);
        var tax = TaxResolver.TaxOn(total, taxRate);
        var description = count == 1 ? "1 event credit" : $"{count} event credits";

        for (var i = 0; i < count; i++)
        {
            db.EventCredits.Add(new EventCredit
            {
                Id = Guid.NewGuid(),
                OwnerOrganizationId = organizationId,
                PriceAtPurchase = unit,
                Currency = "USD",
                PurchasedUtc = now,
                ExpiresUtc = now.Add(Services.Events.EventCredits.Life),
                ProviderCheckoutRef = checkout.SessionId,
                ProviderPaymentRef = reference,
                DateCreated = now,
                CreatedByAppUserId = buyerId,
            });
        }

        db.BillingLedgerEntries.Add(new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Charge,
            OrganizationId = organizationId,
            Amount = total, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = description, PaymentReference = reference,
            DateCreated = now, CreatedByAppUserId = buyerId,
        });

        var payment = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment,
            OrganizationId = organizationId,
            Amount = total, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = $"Card payment — {description}", PaymentReference = reference,
            DateCreated = now, CreatedByAppUserId = buyerId,
        };

        for (var attempt = 0; ; attempt++)
        {
            payment.ReceiptNumber = 1 + await db.BillingLedgerEntries
                .MaxAsync(e => (int?)e.ReceiptNumber, ct) ?? 1;
            db.BillingLedgerEntries.Add(payment);
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) when (attempt < 2) { db.BillingLedgerEntries.Remove(payment); }
        }

        // Stamped after the save so the number on the credit is the number on the receipt it was
        // actually written with, retries included.
        foreach (var credit in db.ChangeTracker.Entries<EventCredit>().Select(e => e.Entity))
            credit.ReceiptNumber = $"R-{payment.ReceiptNumber:00000}";
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Stripe fulfilled {Count} event credit(s) for org {OrganizationId}, receipt R-{Receipt:00000}.",
            count, organizationId, payment.ReceiptNumber);
    }

    private static async Task RedeemCouponAsync(
        BenDataContext db, OrganizationSubscription sub, CheckoutFacts facts,
        string typedCode, DateTime now, CancellationToken ct)
    {
        var normalised = CouponCodeGenerator.Normalise(typedCode);
        var code = await db.CouponCodes.Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.Code == normalised, ct);
        if (code is null) return;   // validated at checkout creation; a code deleted since is a no-op, not a lost payment

        var existing = await db.CouponRedemptions
            .FirstOrDefaultAsync(r => r.CouponId == code.CouponId
                                   && r.OrganizationId == facts.OrganizationId, ct);
        if (existing is not null)
        {
            // A renewal under a multi-period coupon: the discount was honoured in the price the
            // job computed, and the redemption's meter moves one period. Null means Forever and
            // has no meter to move.
            if (existing.PeriodsRemaining is > 0)
            {
                existing.PeriodsRemaining--;
                existing.DateUpdated = now;
            }
            return;
        }

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
