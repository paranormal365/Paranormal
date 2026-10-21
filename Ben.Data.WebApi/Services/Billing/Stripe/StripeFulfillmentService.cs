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
    private readonly Ben.Data.Common.Interfaces.IEmailService? _email;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<StripeFulfillmentService> _log;

    /// <param name="email">
    /// How the receipt is posted.
    /// <para>Optional and trailing, like every other dependency added to this area: the fixtures
    /// here are about money landing on the ledger correctly, not about mail, and none of them
    /// should have to learn what a mailer is to keep testing that.</para>
    /// </param>
    private readonly Ben.Data.WebApi.Services.Store.StoreOrderPayments? _store;

    public StripeFulfillmentService(
        IDbContextFactory<BenDataContext> dbFactory,
        ILogger<StripeFulfillmentService> log,
        Ben.Data.Common.Interfaces.IEmailService? email = null,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity>? site = null,
        Ben.Data.WebApi.Services.Store.StoreOrderPayments? store = null)
    {
        _dbFactory = dbFactory;
        _email = email;
        _site = site?.Value ?? new Ben.Data.Common.SiteIdentity();
        _log = log;
        _store = store;
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

            /// <summary>
            /// Set when the credits belong to a PERSON rather than a group (10/21/2026): the person
            /// who holds them. A person's purchase carries no organization key at all.
            /// </summary>
            public const string CreditOwnerUser = "ih_credit_owner_user";

            /// <summary>The credits' price before any subscriber price — for the ledger's wording.</summary>
            public const string CreditFullPrice = "ih_credit_full";

            /// <summary>"single" or "bundle", with "+subscriber" when the paid-plan price applied.</summary>
            public const string CreditBasis = "ih_credit_basis";

            /// <summary>
            /// Present exactly when this payment buys a month of the Ghost Walk Tour subscription
            /// (10/21/2026): the group it is for. Fulfilled down its own path, like a seat.
            /// </summary>
            public const string GhostWalk = "ih_ghost_walk";

            /// <summary>How many tour routes the Ghost Walk month pays for.</summary>
            public const string GhostWalkRoutes = "ih_gw_routes";

            /// <summary>One route for one month at the price charged.</summary>
            public const string GhostWalkUnit = "ih_gw_unit";

            /// <summary>Route changes made up to this moment are on this bill and are settled by it.</summary>
            public const string GhostWalkSettleBefore = "ih_gw_settle_before";

            /// <summary>A credit left over after this bill, carried to the next one (negative).</summary>
            public const string GhostWalkCarry = "ih_gw_carry";
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

    /// <summary>The gates a payment is fulfilled behind: a fixed set, so the same payment always meets the same one.</summary>
    private static readonly SemaphoreSlim[] Gates = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];

    private static SemaphoreSlim GateFor(string reference)
        => Gates[(int)((uint)StringComparer.Ordinal.GetHashCode(reference) % (uint)Gates.Length)];

    /// <summary>
    /// Records the payment and opens the period. Safe to call twice with the same checkout, including at
    /// the same moment.
    /// </summary>
    /// <remarks>
    /// Every checkout arrives twice, as checkout.session.completed and payment_intent.succeeded with the
    /// same payment. "Is there a ledger row for this reference" was the only guard, and two deliveries
    /// arriving together both found none: credits granted twice, two receipts, a coupon spent twice (site
    /// audit, 10/09/2026). A unique index can't referee it because the charge row and the payment row share
    /// the reference, so the second delivery waits here for the first to finish, then finds its rows and
    /// stops. The API runs as a single process; a second instance would need a database lock instead.
    /// </remarks>
    public async Task FulfillAsync(StripeCompletedCheckout checkout, CancellationToken ct = default)
    {
        var gate = GateFor(checkout.PaymentIntentRef ?? checkout.SessionId);
        await gate.WaitAsync(ct);
        try { await FulfillOnceAsync(checkout, ct); }
        finally { gate.Release(); }
    }

    private async Task FulfillOnceAsync(StripeCompletedCheckout checkout, CancellationToken ct)
    {
        // A store order's payment (storefront S4.6) — first, and never allowed to fall through to
        // the subscription path below, whose "no usable metadata" branch ignores and answers 200.
        // With the store's service missing that would drop a real payment on the floor, so it
        // throws instead: the webhook answers 500 and Stripe delivers it again.
        if (checkout.Metadata.TryGetValue(StoreStripeKeys.Order, out var storeOrderRaw))
        {
            if (_store is null)
                throw new InvalidOperationException("A store order was paid but the store's payment service is not registered.");
            if (!Guid.TryParse(storeOrderRaw, out var storeOrderId))
            {
                _log.LogError("Stripe payment {Reference} carries an unreadable store order id.", checkout.PaymentIntentRef ?? checkout.SessionId);
                return;
            }
            checkout.Metadata.TryGetValue(StoreStripeKeys.TaxCalculation, out var taxCalc);
            await _store.MarkPaidAsync(storeOrderId, checkout.PaymentIntentRef, checkout.ChargeRef,
                checkout.PaymentIntentRef ?? checkout.SessionId, checkout.AmountReceivedCents, taxCalc, ct);
            return;
        }

        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.Seat, out var seatRaw)
            && Guid.TryParse(seatRaw, out var seatId))
        {
            await FulfillSeatAsync(seatId, checkout, ct);
            return;
        }

        // A month of the Ghost Walk Tour subscription (10/21/2026): its own row beside the plan, so
        // its own path — the plan path below would rewrite the group's investigation plan.
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.GhostWalk, out var ghostRaw)
            && Guid.TryParse(ghostRaw, out var ghostOrgId))
        {
            await FulfillGhostWalkAsync(ghostOrgId, checkout, ct);
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

        if (_email is not null)
            await PostReceiptAsync(db, facts.OrganizationId, payment,
                tier?.Name is { Length: > 0 } t ? $"{t} plan" : "your plan", ct);
    }

    /// <summary>
    /// Posts the receipt for a payment that has just been recorded.
    /// </summary>
    /// <remarks>
    /// <para><b>A receipt existed and was never sent.</b> The ledger has carried receipt numbers
    /// since item 168, the billing page shows them, and nothing ever put one in front of the
    /// person who paid — they had to know to go and look. A receipt is the one letter somebody
    /// actively wants and may need for their own books (item 246).</para>
    ///
    /// <para><b>After the money is recorded, and never able to undo it.</b> This runs once the
    /// ledger row is saved and swallows anything it cannot do: a payment that went through and a
    /// letter that did not is a nuisance, and a letter failure that rolled back a payment would be
    /// very much worse.</para>
    ///
    /// <para>It goes to the group's billing people — the same audience the lapse and plan notices
    /// use — because the payer may be a card on file rather than a person reading mail.</para>
    /// </remarks>
    private async Task PostReceiptAsync(
        BenDataContext db, Guid? organizationId, BillingLedgerEntry payment, string what,
        CancellationToken ct, Guid? personId = null)
    {
        try
        {
            // A group's receipt goes to its billing people; a person's own purchase (10/21/2026)
            // goes to the person, named as who it was for.
            var org = organizationId is { } orgId
                ? await db.Organizations.AsNoTracking()
                    .Where(o => o.Id == orgId)
                    .Select(o => new { o.Name, o.CreatedByAppUserId })
                    .FirstOrDefaultAsync(ct)
                : null;
            if (org is null && personId is null) return;

            var recipientId = org?.CreatedByAppUserId ?? personId!.Value;
            var to = await db.AppUsers.AsNoTracking()
                .Where(u => u.Id == recipientId && u.Email != null && u.Email != "")
                .Select(u => new { u.Email, u.DisplayName })
                .FirstOrDefaultAsync(ct);
            if (to is null) return;
            org ??= new { Name = to.DisplayName ?? to.Email!, CreatedByAppUserId = recipientId };

            var reference = $"R-{payment.ReceiptNumber:00000}";

            var subject = $"{_site.Name} receipt {reference}";
            var body =
                $"<p>Thank you — your payment for <strong>{System.Net.WebUtility.HtmlEncode(org.Name)}</strong> "
              + "has gone through.</p>"
              + "<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;\">"
              + $"<tr><td><strong>Receipt</strong></td><td>{reference}</td></tr>"
              + $"<tr><td><strong>For</strong></td><td>{System.Net.WebUtility.HtmlEncode(what)}</td></tr>"
              + $"<tr><td><strong>Paid</strong></td><td>{payment.Amount:C}</td></tr>"
              + $"<tr><td><strong>Date</strong></td><td>{payment.DateCreated:MM/dd/yyyy}</td></tr>"
              + "</table>"
              + (organizationId is null
                    ? "<p>Your event credits page keeps every receipt, so this one is there whenever "
                      + "you need it again.</p>"
                    : "<p>Your group's billing page keeps every receipt, so this one is there whenever "
                      + "you need it again.</p>");

            await _email!.SendAsync(
                new Ben.Data.Common.Interfaces.EmailMessage(
                    to.Email!, subject, body,
                    Kind: Ben.Data.Common.Mail.MailKinds.PaymentReceipt.Key,
                    Payload: new Ben.Data.Common.Interfaces.MailPayload(
                        new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Organizations"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Name"] = org.Name,
                            },
                            ["AppUsers"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Email"] = to.Email,
                                ["DisplayName"] = to.DisplayName,
                            },
                            ["BillingLedgerEntries"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["ReceiptNumber"] = reference,
                                ["Amount"] = payment.Amount,
                                ["DateCreated"] = payment.DateCreated,
                            },
                        })),
                ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "The payment for org {OrganizationId} was recorded but its receipt could not be "
              + "posted.", organizationId);
        }
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
    /// <para><b>A group or a person</b> (10/21/2026). A group's credits land on its ledger; a person's
    /// land on the ledger with no group and the payer named, so the money trail has the sale either
    /// way.</para>
    ///
    /// <para><b>What was charged, exactly.</b> The total is the metadata's frozen figure — a bundle or a
    /// subscriber price included — and it is shared across the credits to the cent, so the credits add
    /// up to the receipt and a refund of one returns what that one cost.</para>
    ///
    /// <para><b>Taxed like the digital service it is</b>, at the rate frozen when checkout opened.</para>
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

        Guid? organizationId = null, personId = null;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.Organization, out var orgRaw)
            && Guid.TryParse(orgRaw, out var parsedOrg))
            organizationId = parsedOrg;
        else if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.CreditOwnerUser, out var personRaw)
                 && Guid.TryParse(personRaw, out var parsedPerson))
            personId = parsedPerson;

        if (organizationId is null && personId is null)
        {
            _log.LogError("Event-credit payment {Reference} names no group and no person.", reference);
            return;
        }

        Guid buyerId = Guid.Empty;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.User, out var userRaw))
            Guid.TryParse(userRaw, out buyerId);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var total = Services.Events.EventCredits.DefaultPriceUsd * count;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.List, out var listRaw)
            && decimal.TryParse(listRaw, System.Globalization.NumberStyles.Any, inv, out var listed))
            total = decimal.Round(listed, 2, MidpointRounding.AwayFromZero);

        var now = DateTime.UtcNow;

        // The tax the buyer was CHARGED, read back from the metadata (2026-09-17 audit): the rate
        // is frozen onto whatever document asked, and a later rule edit never rewrites a bill. The
        // live resolve is only the fallback for a session created before the key existed.
        decimal taxRate, tax;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.TaxRate, out var rateRaw)
            && decimal.TryParse(rateRaw, System.Globalization.NumberStyles.Number, inv, out taxRate)
            && checkout.Metadata.TryGetValue(CheckoutFacts.Keys.TaxAmount, out var taxRaw)
            && decimal.TryParse(taxRaw, System.Globalization.NumberStyles.Number, inv, out tax))
        {
            // Both figures came from the session the buyer actually paid.
        }
        else
        {
            _log.LogWarning(
                "Event-credit payment {Reference} carries no frozen tax; resolving it live.", reference);
            taxRate = 0m;
            if (organizationId is { } taxedOrg)
                (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, taxedOrg, ct);
            tax = TaxResolver.TaxOn(total, taxRate);
        }

        checkout.Metadata.TryGetValue(CheckoutFacts.Keys.CreditBasis, out var basis);
        var description = (count == 1 ? "1 event credit" : $"{count} event credits")
            + (basis?.StartsWith("bundle", StringComparison.Ordinal) == true ? " (bundle)" : "")
            + (basis?.EndsWith("+subscriber", StringComparison.Ordinal) == true ? ", paid-plan price" : "");

        var shares = Services.Events.EventCreditPricing.Share(total, count);
        for (var i = 0; i < count; i++)
        {
            db.EventCredits.Add(new EventCredit
            {
                Id = Guid.NewGuid(),
                OwnerOrganizationId = organizationId,
                OwnerAppUserId = organizationId is null ? personId : null,
                PriceAtPurchase = shares[i],
                Currency = "USD",
                PurchasedUtc = now,
                ExpiresUtc = now.Add(Services.Events.EventCredits.Life),
                ProviderCheckoutRef = checkout.SessionId,
                ProviderPaymentRef = reference,
                DateCreated = now,
                CreatedByAppUserId = buyerId,
            });
        }

        var payer = organizationId is null ? personId : null;
        db.BillingLedgerEntries.Add(new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Charge,
            OrganizationId = organizationId, PayerAppUserId = payer,
            Amount = total, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = description, PaymentReference = reference,
            DateCreated = now, CreatedByAppUserId = buyerId,
        });

        var payment = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment,
            OrganizationId = organizationId, PayerAppUserId = payer,
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
            "Stripe fulfilled {Count} event credit(s) for {Owner}, receipt R-{Receipt:00000}.",
            count, organizationId?.ToString() ?? $"person {personId}", payment.ReceiptNumber);

        if (_email is not null)
            await PostReceiptAsync(db, organizationId, payment, description, ct, personId);
    }

    /// <summary>
    /// A month of the Ghost Walk Tour subscription, paid (10/21/2026).
    /// </summary>
    /// <remarks>
    /// <para>Mirrors the plan path where they share a meaning — idempotent by payment reference, the
    /// period dated from the metadata's start when it is a renewal and from now when it is the first,
    /// a charge and a payment with frozen tax and the next receipt number — and leaves everything that
    /// is the plan's alone. It never touches the group's investigation plan.</para>
    ///
    /// <para><b>Fulfilled even if the subscription has since been taken off sale.</b> The money was
    /// taken while it was on sale, and a month somebody paid for is a month they get.</para>
    /// </remarks>
    private async Task FulfillGhostWalkAsync(Guid organizationId, StripeCompletedCheckout checkout, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var reference = checkout.PaymentIntentRef ?? checkout.SessionId;
        if (await db.BillingLedgerEntries.AnyAsync(e => e.PaymentReference == reference, ct))
        {
            _log.LogInformation("Ghost Walk payment {Reference} delivered again — already fulfilled.", reference);
            return;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!checkout.Metadata.TryGetValue(CheckoutFacts.Keys.Payable, out var payRaw)
            || !decimal.TryParse(payRaw, System.Globalization.NumberStyles.Number, inv, out var payable))
        {
            _log.LogError("Ghost Walk payment {Reference} carries no price; not fulfilled.", reference);
            return;
        }
        checkout.Metadata.TryGetValue(CheckoutFacts.Keys.TaxRate, out var rateRaw);
        checkout.Metadata.TryGetValue(CheckoutFacts.Keys.TaxAmount, out var taxRaw);
        decimal.TryParse(rateRaw, System.Globalization.NumberStyles.Number, inv, out var taxRate);
        decimal.TryParse(taxRaw, System.Globalization.NumberStyles.Number, inv, out var tax);

        Guid buyerId = Guid.Empty;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.User, out var userRaw))
            Guid.TryParse(userRaw, out buyerId);

        var now = DateTime.UtcNow;
        var periodStart = now;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.PeriodStart, out var psRaw)
            && DateTime.TryParse(psRaw, inv, System.Globalization.DateTimeStyles.RoundtripKind, out var ps))
            periodStart = ps;
        var periodEnd = periodStart.AddMonths(1);

        // Per route (Ben, 10/21/2026). A session from before the key existed paid for one route.
        var routes = 1;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.GhostWalkRoutes, out var routesRaw)
            && int.TryParse(routesRaw, out var parsedRoutes) && parsedRoutes > 0)
            routes = parsedRoutes;
        var unit = decimal.Round(payable / routes, 2, MidpointRounding.AwayFromZero);
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.GhostWalkUnit, out var unitRaw)
            && decimal.TryParse(unitRaw, System.Globalization.NumberStyles.Number, inv, out var parsedUnit) && parsedUnit > 0m)
            unit = parsedUnit;

        var sub = await db.GhostWalkSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);
        if (sub is null)
        {
            sub = new GhostWalkSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = organizationId,
                DateCreated = now, CreatedByAppUserId = buyerId,
            };
            db.GhostWalkSubscriptions.Add(sub);
        }
        else
        {
            sub.DateUpdated = now;
            sub.UpdatedByAppUserId = buyerId;
        }

        sub.Status = SubscriptionStatus.Active;
        sub.PriceAtPeriodStart = payable;
        sub.Quantity = routes;
        sub.UnitPriceAtPeriodStart = unit;
        sub.CurrentPeriodStart = periodStart;
        sub.CurrentPeriodEnd = periodEnd;
        sub.CancelAtPeriodEnd = false;
        sub.ProviderName = "Stripe";
        sub.ProviderSubscriptionRef = checkout.SessionId;
        sub.ProviderCustomerRef = checkout.CustomerRef ?? sub.ProviderCustomerRef;
        sub.ProviderPaymentMethodRef = checkout.PaymentMethodRef ?? sub.ProviderPaymentMethodRef;

        // The route changes this bill settles — Stripe's default proration, which puts the days a
        // route was added or given up mid-month on the next invoice rather than charging on the spot.
        var settled = 0m;
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.GhostWalkSettleBefore, out var beforeRaw)
            && DateTime.TryParse(beforeRaw, inv, System.Globalization.DateTimeStyles.RoundtripKind, out var before))
        {
            foreach (var change in await Services.Tours.GhostWalkRoutes.UnsettledAsync(db, sub.Id, before, ct))
            {
                change.SettledUtc = now;
                change.SettledPaymentRef = reference;
                change.DateUpdated = now;
                settled += change.ProratedAmount;
            }
        }

        // A month whose credits came to more than its bill: the rest waits for the next one.
        if (checkout.Metadata.TryGetValue(CheckoutFacts.Keys.GhostWalkCarry, out var carryRaw)
            && decimal.TryParse(carryRaw, System.Globalization.NumberStyles.Number, inv, out var carry) && carry < 0m)
        {
            db.GhostWalkRouteChanges.Add(new GhostWalkRouteChange
            {
                Id = Guid.NewGuid(), GhostWalkSubscriptionId = sub.Id, ChangedUtc = now,
                FromQuantity = routes, ToQuantity = routes, ProratedAmount = carry,
                DateCreated = now, CreatedByAppUserId = buyerId,
            });
        }

        var description = $"Ghost Walk Tour subscription — {routes} tour {(routes == 1 ? "route" : "routes")} × ${unit:0.00}, monthly"
            + (settled == 0m ? "" : settled > 0m
                ? $", plus ${settled:0.00} for routes added last month"
                : $", less ${-settled:0.00} for routes given up last month");
        db.BillingLedgerEntries.Add(new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Charge,
            OrganizationId = organizationId,
            Amount = payable, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = description, PaymentReference = reference,
            PeriodStart = periodStart, PeriodEnd = periodEnd,
            DateCreated = now, CreatedByAppUserId = buyerId,
        });

        // Credits covered the whole month: nothing was paid, so there is no payment and no receipt —
        // the charge of nothing is the record, as for a 100%-off plan period.
        if (checkout.PaymentIntentRef is null && payable == 0m)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var payment = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment,
            OrganizationId = organizationId,
            Amount = payable, TaxRatePercent = taxRate, TaxAmount = tax,
            Description = $"Card payment — {description}", PaymentReference = reference,
            PeriodStart = periodStart, PeriodEnd = periodEnd,
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

        _log.LogInformation(
            "Stripe fulfilled Ghost Walk Tour subscription for org {OrganizationId} through {End:MM/dd/yyyy}, receipt R-{Receipt:00000}.",
            organizationId, periodEnd, payment.ReceiptNumber);

        if (_email is not null)
            await PostReceiptAsync(db, organizationId, payment, "the Ghost Walk Tour subscription", ct);
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
