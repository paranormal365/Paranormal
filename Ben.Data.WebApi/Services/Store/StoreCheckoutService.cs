using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>How a checkout attempt came out, for the controller to turn into a status.</summary>
public enum StoreCheckoutOutcome { Ok, BadRequest, Conflict, Unavailable }

/// <param name="StillProcessingOrderId">Set when an earlier payment on this cart is still going through — the page polls it.</param>
public sealed record StoreCheckoutResult(
    StoreCheckoutOutcome Outcome, StoreCheckoutPrepared? Prepared, string? Sentence, Guid? StillProcessingOrderId = null)
{
    public static StoreCheckoutResult Refused(StoreCheckoutOutcome outcome, string sentence, Guid? orderId = null)
        => new(outcome, null, sentence, orderId);
}

/// <summary>
/// Turns a cart into an order waiting for payment (storefront S4.7, plan §5.5 steps 1–9).
/// </summary>
/// <remarks>
/// <para><b>The order exists before any money moves.</b> Stock and the discount code are held in the
/// same transaction that writes the order, so the last unit and a single-use code can each be won
/// only once; the PaymentIntent is created afterwards (never inside a transaction), and a Stripe
/// failure leaves a placed order with no intent for the next attempt or the expiry job.</para>
///
/// <para><b>Pressing Continue twice is not two orders.</b> An unpaid order already open for this
/// cart with the same contents, address and email is REUSED: its totals are rewritten to what will
/// be charged now (shipping rates move, tax is recalculated) and its hold extended. Anything
/// different cancels the old one — at Stripe first — and places afresh.</para>
///
/// <para><b>What is charged is the server's sum</b>: prices re-read from the variants, the code
/// re-checked with the email now known, shipping from the settings, tax from Stripe Tax on each
/// line net of its share of the discount.</para>
/// </remarks>
public sealed partial class StoreCheckoutService(
    IDbContextFactory<BenDataContext> dbFactory, IStoreStripeGateway gateway, IStoreTaxService tax,
    StoreOrderPayments payments, StoreAlerts alerts, StorePaymentSetup setup, IOptions<StripeOptions> stripe,
    ILogger<StoreCheckoutService> log, TimeProvider? clock = null)
{
    public const string StripeDidNotAnswer = "Payment couldn't be started just now — nothing was charged. Try again in a moment.";

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex ZipShape();

    /// <summary>The first thing wrong with what was typed, in the buyer's words; null when all is well.</summary>
    public static string? Problem(StoreCheckoutRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || !EmailShape().IsMatch(r.Email.Trim()) || r.Email.Length > 256)
            return StoreCheckoutSentences.EmailInvalid;
        foreach (var a in r.Billing is null ? [r.Shipping] : new[] { r.Shipping, r.Billing })
            if (AddressProblem(a) is { } why) return why;
        if (!r.AgreedToTerms) return StoreCheckoutSentences.AgreeToTerms;
        return null;
    }

    private static string? AddressProblem(StoreAddressInput? a)
    {
        if (a is null) return StoreCheckoutSentences.Required("The address");
        if (string.IsNullOrWhiteSpace(a.FullName)) return StoreCheckoutSentences.Required("Full name");
        if (string.IsNullOrWhiteSpace(a.Phone)) return StoreCheckoutSentences.Required("Phone");
        if (string.IsNullOrWhiteSpace(a.Street1)) return StoreCheckoutSentences.Required("Street address");
        if (string.IsNullOrWhiteSpace(a.City)) return StoreCheckoutSentences.Required("City");
        if (!UsStates.IsValid(a.State)) return StoreCheckoutSentences.ChooseAState;
        if (string.IsNullOrWhiteSpace(a.Zip) || !ZipShape().IsMatch(a.Zip.Trim())) return StoreCheckoutSentences.ZipInvalid;
        return null;
    }

    /// <summary>What makes two attempts "the same checkout": contents at their prices, the code, the address and the email.</summary>
    public static string Fingerprint(IEnumerable<(Guid VariantId, int Quantity, decimal UnitPrice)> lines, Guid? couponId,
        StoreAddressInput shipTo, string email)
    {
        var sb = new StringBuilder();
        foreach (var l in lines.OrderBy(l => l.VariantId)) sb.Append($"{l.VariantId:N}:{l.Quantity}:{l.UnitPrice:0.00};");
        sb.Append($"|{couponId}|{shipTo.FullName.Trim()}|{shipTo.Street1.Trim()}|{shipTo.Street2?.Trim()}|{shipTo.City.Trim()}|")
          .Append($"{UsStates.Normalize(shipTo.State)}|{shipTo.Zip.Trim()}|{shipTo.Phone.Trim()}|{StoreEmail.Normalize(email)}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed record Line(StoreProductVariant Variant, StoreProduct Product, int Quantity, int Available, bool Sellable)
    {
        public decimal LineTotal => StoreMoney.Round(Variant.Price * Quantity);
    }

    private sealed record Priced(
        List<Line> Lines, decimal Subtotal, StoreCoupon? Coupon, decimal Discount, IReadOnlyList<decimal> Shares,
        decimal Shipping, StoreTaxResult Tax, decimal Total);

    public async Task<StoreCheckoutResult> PrepareAsync(StoreCartCaller caller, string? ip, StoreCheckoutRequest request, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await StoreSettingsReader.ReadAsync(db, ct);

        // 1–2. Paused, or no way to take a card: before anything is held.
        if (!settings.CheckoutEnabled) return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.Paused);
        if (!(setup.Fake || (setup.HasSecretKey && setup.HasPublishableKey)) || !gateway.IsConfigured)
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.PaymentsNotSetUp);
        if (Problem(request) is { } typed) return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, typed);
        if (caller.IsNobody) return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, StoreCartSentences.NoCart);

        var email = request.Email.Trim();
        var emailKey = StoreEmail.Normalize(email);

        var cart = await new StoreCartService(db, clock).ResolveAsync(caller, create: false, ct);
        if (cart is null) return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, StoreCheckoutSentences.CartEmpty);

        var open = await db.StoreOrders.Include(o => o.Items)
            .Where(o => o.StoreCartId == cart.Id && o.Status == StoreOrderStatus.PendingPayment && o.ReservationReleasedUtc == null)
            .OrderByDescending(o => o.PlacedUtc).FirstOrDefaultAsync(ct);
        var held = open?.Items.GroupBy(i => i.VariantId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity)) ?? [];

        var lines = (await db.StoreCartItems.AsNoTracking().Where(i => i.CartId == cart.Id)
                .Select(i => new { i.Quantity, i.Variant, i.Variant.Product, Sellable = i.Variant.IsActive && i.Variant.Product.IsActive && i.Variant.Product.Category.IsActive })
                .ToListAsync(ct))
            .Select(x => new Line(x.Variant, x.Product, x.Quantity,
                Math.Max(0, x.Variant.StockOnHand - x.Variant.StockReserved) + held.GetValueOrDefault(x.Variant.Id), x.Sellable))
            .OrderBy(l => l.Variant.Id).ToList();

        if (lines.Count == 0) return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, StoreCheckoutSentences.CartEmpty);
        if (lines.Count > StoreCartRules.MaxDistinctLines)
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, StoreCheckoutSentences.TooManyLines(StoreCartRules.MaxDistinctLines));
        if (lines.FirstOrDefault(l => !l.Sellable || l.Available <= 0 || l.Quantity > l.Available) is { } bad)
        {
            var why = !bad.Sellable ? StoreCartSentences.NoLongerAvailable
                    : bad.Available <= 0 ? $"{bad.Product.Name} is sold out."
                    : $"only {bad.Available} of {bad.Product.Name} are left.";
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.ItemsChanged(why));
        }

        var openCount = await db.StoreOrders.CountAsync(o => o.Status == StoreOrderStatus.PendingPayment && o.ReservationReleasedUtc == null
            && (open == null || o.Id != open.Id) && ((ip != null && o.PlacedFromIp == ip) || o.BuyerEmailNormalized == emailKey), ct);
        if (openCount >= StoreCheckoutRules.MaxOpenCheckoutsPerCaller)
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.TooManyOpen);

        // 3. The code, re-checked with the email and the account now known.
        var subtotal = lines.Sum(l => l.LineTotal);
        StoreCoupon? coupon = null;
        var discount = 0m;
        if (cart.CouponId is { } couponId)
        {
            coupon = await db.StoreCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == couponId, ct);
            var prior = coupon is null ? 0 : await StoreCouponReservations.PriorRedemptionsAsync(db, coupon.Id, emailKey, caller.UserId, open?.Id, ct);
            if (StoreCouponMath.WhyNotRedeemable(coupon, subtotal, prior, now) is { } why)
                return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, why);
            discount = StoreCouponMath.DiscountFor(coupon!, subtotal);
        }
        var shares = StoreCouponMath.Allocate(lines.Select(l => l.LineTotal).ToList(), discount);

        // 5. Shipping, judged after the discount.
        var free = settings.ShippingFlatRate <= 0m
                || (settings.FreeShippingThreshold > 0m && subtotal - discount >= settings.FreeShippingThreshold);
        var shipping = free ? 0m : settings.ShippingFlatRate;

        // 4. Tax, on each line net of its share of the discount.
        if (!tax.IsAvailable(settings))
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.TaxUnavailable);
        StoreTaxResult taxed;
        try
        {
            taxed = await tax.CalculateAsync(new StoreTaxRequest(
                lines.Select((l, i) => new StoreTaxLine(l.Variant.Sku, StoreMoney.Cents(l.LineTotal - shares[i]), l.Quantity,
                    l.Product.StripeTaxCode ?? StripeTaxService.DefaultTaxCode)).ToList(),
                StoreMoney.Cents(shipping),
                new StoreTaxAddress(request.Shipping.Street1.Trim(), request.Shipping.Street2?.Trim(), request.Shipping.City.Trim(),
                    UsStates.Normalize(request.Shipping.State)!, request.Shipping.Zip.Trim()),
                new StoreTaxAddress(settings.ShipFrom.Street, null, settings.ShipFrom.City, settings.ShipFrom.State, settings.ShipFrom.Zip)), ct);
        }
        catch (StoreTaxException ex) when (ex.Failure == StoreTaxFailure.BuyerAddress)
        {
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.BadRequest, ex.Message);
        }
        catch (StoreTaxException ex) when (ex.Failure == StoreTaxFailure.Configuration)
        {
            log.LogError(ex, "Stripe Tax refused the store's own setup; checkout is paused with a sentence.");
            await alerts.TaxConfigurationAsync(ex.Message, ct);
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.OrderingPaused);
        }
        catch (StoreTaxException)
        {
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.TaxUnavailable);
        }

        var taxAmount = taxed.TaxCents / 100m;
        var total = subtotal - discount + shipping + taxAmount;
        var priced = new Priced(lines, subtotal, coupon, discount, shares, shipping, taxed, total);
        var fingerprint = Fingerprint(lines.Select(l => (l.Variant.Id, l.Quantity, l.Variant.Price)), coupon?.Id, request.Shipping, email);

        // 6. The same checkout again: rewrite the open order to what is charged now.
        if (open is not null)
        {
            if (open.CartFingerprint == fingerprint)
            {
                var reused = await ReuseAsync(db, open, priced, settings, now, ct);
                if (reused is not null) return reused;
            }
            else
            {
                var cancelled = await payments.CancelPendingOrderAsync(open.Id, "Checkout restarted", ct);
                if (cancelled == StoreOrderPayments.CancelOutcome.StillProcessing)
                    return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.EarlierPaymentProcessing, open.Id);
            }
        }

        // 7. Place it: stock and code held with the order, or not at all.
        return await PlaceAsync(caller, ip, request, cart, priced, fingerprint, settings, now, ct);
    }

    private async Task<StoreCheckoutResult?> ReuseAsync(BenDataContext db, StoreOrder open, Priced p, StoreSettingsSnapshot settings,
        DateTime now, CancellationToken ct)
    {
        var previous = (open.Subtotal, open.DiscountAmount, open.ShippingAmount, open.TaxAmount, open.ShippingTaxAmount, open.Total, open.StripeTaxCalculationId);
        var expires = now.AddMinutes(settings.ReservationMinutes);
        var shippingTax = p.Tax.ShippingTaxCents / 100m;

        var rows = await db.StoreOrders
            .Where(o => o.Id == open.Id && o.Status == StoreOrderStatus.PendingPayment && o.ReservationReleasedUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Subtotal, p.Subtotal).SetProperty(o => o.DiscountAmount, p.Discount)
                .SetProperty(o => o.ShippingAmount, p.Shipping).SetProperty(o => o.TaxAmount, p.Tax.TaxCents / 100m)
                .SetProperty(o => o.ShippingTaxAmount, shippingTax).SetProperty(o => o.Total, p.Total)
                .SetProperty(o => o.StripeTaxCalculationId, p.Tax.CalculationId)
                .SetProperty(o => o.ReservationExpiresUtc, expires).SetProperty(o => o.DateUpdated, now), ct);
        if (rows == 0) return null;   // the expiry job released it between the read and here: place afresh

        for (var i = 0; i < p.Lines.Count; i++)
        {
            var variantId = p.Lines[i].Variant.Id;
            var share = p.Shares[i];
            var lineTax = p.Tax.LineTaxCents.GetValueOrDefault(p.Lines[i].Variant.Sku) / 100m;
            await db.StoreOrderItems.Where(x => x.OrderId == open.Id && x.VariantId == variantId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LineDiscount, share).SetProperty(x => x.TaxAmount, lineTax), ct);
        }

        var cents = StoreMoney.Cents(p.Total);
        try
        {
            if (open.StripePaymentIntentId is { } pi)
                await gateway.UpdatePaymentIntentAsync(pi, cents, Metadata(open, p.Tax.CalculationId), ct);
            else if (cents > 0)
                await CreateIntentAsync(db, open, p, settings, ct);
        }
        catch (StoreStripeUnexpectedStateException)
        {
            // The earlier tab's payment is confirming on the old amount: put the row back to it, so
            // the row and the intent never disagree about which calculation is being charged.
            await db.StoreOrders.Where(o => o.Id == open.Id && o.Status == StoreOrderStatus.PendingPayment)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Subtotal, previous.Subtotal).SetProperty(o => o.DiscountAmount, previous.DiscountAmount)
                    .SetProperty(o => o.ShippingAmount, previous.ShippingAmount).SetProperty(o => o.TaxAmount, previous.TaxAmount)
                    .SetProperty(o => o.ShippingTaxAmount, previous.ShippingTaxAmount).SetProperty(o => o.Total, previous.Total)
                    .SetProperty(o => o.StripeTaxCalculationId, previous.StripeTaxCalculationId), ct);
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.EarlierPaymentGoingThrough, open.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or Stripe.StripeException)
        {
            log.LogError(ex, "Stripe could not update the intent for store order {Number}.", open.OrderNumber);
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Unavailable, StripeDidNotAnswer);
        }

        await using var fresh = await dbFactory.CreateDbContextAsync(ct);
        var order = await fresh.StoreOrders.AsNoTracking().FirstAsync(o => o.Id == open.Id, ct);
        if (cents == 0)
        {
            await payments.MarkPaidAsync(order.Id, order.StripePaymentIntentId, null, $"free-{order.Id:N}", 0, order.StripeTaxCalculationId, ct);
            return new StoreCheckoutResult(StoreCheckoutOutcome.Ok, Prepared(order, null, p, paid: true), null);
        }
        return new StoreCheckoutResult(StoreCheckoutOutcome.Ok, Prepared(order, await ClientSecretAsync(order, ct), p, paid: false), null);
    }

    private async Task<StoreCheckoutResult> PlaceAsync(StoreCartCaller caller, string? ip, StoreCheckoutRequest r, StoreCart cart,
        Priced p, string fingerprint, StoreSettingsSnapshot settings, DateTime now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = r.Shipping;
        var b = r.Billing;
        var order = new StoreOrder
        {
            Id = Guid.NewGuid(), Status = StoreOrderStatus.PendingPayment, StoreCartId = cart.Id,
            BuyerAppUserId = caller.UserId, BuyerEmail = r.Email.Trim(), BuyerEmailNormalized = StoreEmail.Normalize(r.Email),
            BuyerName = s.FullName.Trim(),
            ShipName = s.FullName.Trim(), ShipPhone = s.Phone.Trim(), ShipStreet1 = s.Street1.Trim(), ShipStreet2 = Blank(s.Street2),
            ShipCity = s.City.Trim(), ShipState = UsStates.Normalize(s.State)!, ShipZip = s.Zip.Trim(), ShipCountry = "US",
            BillingSameAsShipping = b is null,
            BillName = b?.FullName.Trim(), BillCompany = Blank(r.BillCompany), BillStreet1 = b?.Street1.Trim(), BillStreet2 = Blank(b?.Street2),
            BillCity = b?.City.Trim(), BillState = b is null ? null : UsStates.Normalize(b.State), BillZip = b?.Zip.Trim(), BillCountry = b is null ? null : "US",
            Subtotal = p.Subtotal, DiscountAmount = p.Discount, ShippingAmount = p.Shipping,
            TaxAmount = p.Tax.TaxCents / 100m, ShippingTaxAmount = p.Tax.ShippingTaxCents / 100m, Total = p.Total, Currency = "usd",
            CouponId = p.Coupon?.Id, CouponCode = p.Coupon?.Code, CartFingerprint = fingerprint, PlacedFromIp = ip,
            StripeTaxCalculationId = p.Tax.CalculationId,
            AccessToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),
            ReservationExpiresUtc = now.AddMinutes(settings.ReservationMinutes),
            BuyerNotes = Blank(r.BuyerNotes), PlacedUtc = now, DateCreated = now,
        };

        var pictures = await db.StoreProductImages.AsNoTracking()
            .Where(i => p.Lines.Select(l => l.Product.Id).Contains(i.ProductId))
            .Select(i => new { i.ProductId, i.VariantId, i.UploadFileId, i.SortOrder }).ToListAsync(ct);

        try
        {
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

            // In variant order, so two checkouts wanting the same two things cannot deadlock.
            foreach (var l in p.Lines)
                if (!await StoreStock.TryReserveAsync(db, l.Variant.Id, l.Quantity, ct))
                {
                    var left = await db.StoreProductVariants.AsNoTracking().Where(v => v.Id == l.Variant.Id)
                        .Select(v => v.StockOnHand - v.StockReserved).FirstAsync(ct);
                    return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.OnlyLeft(Math.Max(0, left), l.Product.Name));
                }

            db.StoreOrders.Add(order);
            for (var i = 0; i < p.Lines.Count; i++)
            {
                var l = p.Lines[i];
                db.StoreOrderItems.Add(new StoreOrderItem
                {
                    Id = Guid.NewGuid(), OrderId = order.Id, ProductId = l.Product.Id, VariantId = l.Variant.Id,
                    ProductName = l.Product.Name, VariantName = l.Variant.Name, Sku = l.Variant.Sku,
                    UnitPrice = l.Variant.Price, CompareAtPrice = l.Variant.CompareAtPrice is { } was && was > l.Variant.Price ? was : null,
                    Quantity = l.Quantity, LineTotal = l.LineTotal, LineDiscount = p.Shares[i],
                    TaxAmount = p.Tax.LineTaxCents.GetValueOrDefault(l.Variant.Sku) / 100m,
                    StripeTaxCode = l.Product.StripeTaxCode,
                    ImageUploadFileId = (pictures.Where(x => x.VariantId == l.Variant.Id).OrderBy(x => x.SortOrder).FirstOrDefault()
                                      ?? pictures.Where(x => x.ProductId == l.Product.Id).OrderBy(x => x.SortOrder).FirstOrDefault())?.UploadFileId,
                    DateCreated = now,
                });
            }
            db.StoreOrderEvents.Add(new StoreOrderEvent
            {
                Id = Guid.NewGuid(), OrderId = order.Id, Kind = StoreOrderEventKind.Placed, ToStatus = StoreOrderStatus.PendingPayment,
                ActorAppUserId = caller.UserId, Amount = p.Total, OccurredUtc = now,
            });
            await StoreOrderNumbers.SaveNumberedAsync(db, order, ct);

            if (p.Coupon is not null && p.Discount > 0m
                && await StoreCouponReservations.TryReserveAsync(db, p.Coupon, order, p.Discount, now, ct) is { } refusal)
                return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, refusal);   // the transaction rolls back with the order

            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.StockBusy);
        }

        // 8. Nothing owed: paid on the spot (the code is already held, so N free checkouts of a single-use code cannot all win).
        if (StoreMoney.Cents(p.Total) == 0)
        {
            await payments.MarkPaidAsync(order.Id, null, null, $"free-{order.Id:N}", 0, order.StripeTaxCalculationId, ct);
            return new StoreCheckoutResult(StoreCheckoutOutcome.Ok, Prepared(order, null, p, paid: true), null);
        }

        // 9. The intent, outside any transaction.
        try
        {
            var secret = await CreateIntentAsync(db, order, p, settings, ct);
            return new StoreCheckoutResult(StoreCheckoutOutcome.Ok, Prepared(order, secret, p, paid: false), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or Stripe.StripeException or TaskCanceledException)
        {
            log.LogError(ex, "Stripe could not start payment for store order {Number}; it stays placed with no intent.", order.OrderNumber);
            return StoreCheckoutResult.Refused(StoreCheckoutOutcome.Unavailable, StripeDidNotAnswer);
        }
    }

    private async Task<string> CreateIntentAsync(BenDataContext db, StoreOrder order, Priced p, StoreSettingsSnapshot settings, CancellationToken ct)
    {
        var handle = await gateway.CreatePaymentIntentAsync(new StorePaymentIntentSpec(
            order.Id, StoreMoney.Cents(p.Total), "usd", Metadata(order, p.Tax.CalculationId),
            new StoreShippingDetails(order.ShipName, order.ShipPhone, order.ShipStreet1, order.ShipStreet2, order.ShipCity, order.ShipState, order.ShipZip),
            $"IsHaunted store order {order.OrderNumber}", order.OrderNumber.ToString(), $"store-order-{order.Id:N}",
            AllowLink: settings.LinkEnabled), ct);
        if (handle.LinkRefused) await alerts.LinkNotActivatedAsync(ct);

        await db.StoreOrders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.StripePaymentIntentId, handle.PaymentIntentId), ct);
        order.StripePaymentIntentId = handle.PaymentIntentId;
        return handle.ClientSecret;
    }

    /// <summary>The existing intent's secret, for a reused order — asked of Stripe, never remembered here.</summary>
    private async Task<string?> ClientSecretAsync(StoreOrder order, CancellationToken ct)
        => order.StripePaymentIntentId is { } pi ? await gateway.GetClientSecretAsync(pi, ct) : null;

    private static IReadOnlyDictionary<string, string> Metadata(StoreOrder order, string taxCalculationId) => new Dictionary<string, string>
    {
        [StoreStripeKeys.Order] = order.Id.ToString(),
        [StoreStripeKeys.Cart] = order.StoreCartId?.ToString() ?? "",
        [StoreStripeKeys.TaxCalculation] = taxCalculationId,
        [StoreStripeKeys.OrderNumber] = order.OrderNumber.ToString(),
    };

    private StoreCheckoutPrepared Prepared(StoreOrder order, string? secret, Priced p, bool paid)
    {
        var fake = gateway is FakeStoreStripeGateway;
        return new StoreCheckoutPrepared(order.Id, order.OrderNumber, paid ? null : secret,
            paid ? null : fake ? FakeStoreStripeGateway.PublishableKey : stripe.Value.PublishableKey,
            new StoreCheckoutTotals(p.Subtotal, p.Discount, p.Shipping, p.Tax.TaxCents / 100m, p.Total, p.Tax.ShippingTaxCents / 100m),
            paid, $"/store/checkout/complete?order={order.Id}", order.ReservationExpiresUtc ?? Now, fake);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool IsDeadlock(Exception ex)
        => ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 }
        || ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 1205 };
}
