using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using StripeTax = Stripe.Tax;

namespace Ben.Data.WebApi.Services.Store;

// Sales tax for store orders (storefront S4.3), worked out by Stripe Tax: a Calculation before the
// buyer pays, a Transaction from it once they have, a Reversal when money goes back. TaxResolver and
// TaxRateRule — the subscription side's own rates — are deliberately not consulted (Ben's decision).

/// <param name="AmountCents">The line's total NET of its share of any discount: Stripe taxes exactly what it is sent.</param>
public sealed record StoreTaxLine(string Reference, long AmountCents, int Quantity, string TaxCode);

public sealed record StoreTaxAddress(string Line1, string? Line2, string City, string State, string PostalCode, string Country = "US");

public sealed record StoreTaxRequest(IReadOnlyList<StoreTaxLine> Lines, long ShippingCents, StoreTaxAddress ShipTo, StoreTaxAddress ShipFrom);

/// <param name="TaxCents">All the tax — Stripe's <c>tax_amount_exclusive</c>, shipping's included.</param>
/// <param name="LineTaxCents">Each line's own tax, by its reference; with shipping's, they add up to <paramref name="TaxCents"/>.</param>
public sealed record StoreTaxResult(
    string CalculationId, long TaxCents, long ShippingTaxCents, IReadOnlyDictionary<string, long> LineTaxCents, DateTime? ExpiresUtc);

public enum StoreTaxReversalMode { Full, Partial }

/// <summary>Why sales tax could not be worked out, in the terms the checkout answers with.</summary>
public enum StoreTaxFailure
{
    /// <summary>Stripe was slow, down, or rate-limited — try again (503).</summary>
    Transient,
    /// <summary>The buyer's ZIP does not match their state — the buyer's to fix (400; no alert).</summary>
    BuyerAddress,
    /// <summary>Something about the store's own Stripe setup — an alert to the SuperAdmins (503).</summary>
    Configuration,
}

public sealed class StoreTaxException(StoreTaxFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public StoreTaxFailure Failure { get; } = failure;
}

/// <summary>Sales tax through Stripe Tax, plus the readiness probe the settings page uses.</summary>
public interface IStoreTaxService : IStoreTaxProbe
{
    /// <summary>False without a ship-from address — Stripe Tax needs to know where the parcel leaves.</summary>
    bool IsAvailable(StoreSettingsSnapshot settings);

    /// <exception cref="StoreTaxException">The tax could not be worked out, and why.</exception>
    Task<StoreTaxResult> CalculateAsync(StoreTaxRequest request, CancellationToken ct = default);

    /// <summary>A calculation made earlier — the one a payment was actually charged on.</summary>
    Task<StoreTaxResult> GetAsync(string calculationId, CancellationToken ct = default);

    /// <summary>Files the tax as collected. Returns the transaction id.</summary>
    Task<string> CommitAsync(string calculationId, string reference, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Takes tax back for a refund. Returns the reversal's transaction id.</summary>
    Task<string> ReverseAsync(string transactionId, string reference, StoreTaxReversalMode mode, long flatAmountCents,
        string idempotencyKey, CancellationToken ct = default);
}

/// <summary>The real service.</summary>
/// <remarks>
/// Errors are classified by Stripe's own code first, then its type: <c>customer_tax_location_invalid</c>
/// is the buyer's address (a sentence, no alert); any other <c>invalid_request_error</c> is the store's
/// configuration (an alert); connection and rate-limit errors are worth a retry.
/// </remarks>
public sealed class StripeTaxService(IOptions<StripeOptions> options, ILogger<StripeTaxProbe> log)
    : StripeTaxProbe(options, log), IStoreTaxService
{
    public const string DefaultTaxCode = "txcd_99999999";   // general tangible goods
    public const string ShippingTaxCode = "txcd_92010001";

    public bool IsAvailable(StoreSettingsSnapshot settings) => Client is not null && settings.HasShipFrom;

    private StripeClient Stripe => Client ?? throw new StoreTaxException(StoreTaxFailure.Configuration, NotSetUp);

    /// <summary>The options sent to Stripe — public so a test can see exactly what is taxed.</summary>
    public static StripeTax.CalculationCreateOptions BuildCalculationOptions(StoreTaxRequest request) => new()
    {
        Currency = "usd",
        CustomerDetails = new StripeTax.CalculationCustomerDetailsOptions
        {
            Address = Address(request.ShipTo),
            AddressSource = "shipping",
        },
        ShipFromDetails = new StripeTax.CalculationShipFromDetailsOptions { Address = Address(request.ShipFrom) },
        LineItems = request.Lines.Select(l => new StripeTax.CalculationLineItemOptions
        {
            Amount = l.AmountCents,
            Quantity = l.Quantity,
            Reference = l.Reference,
            TaxCode = l.TaxCode,
            TaxBehavior = "exclusive",
        }).ToList(),
        ShippingCost = new StripeTax.CalculationShippingCostOptions
        {
            Amount = request.ShippingCents,
            TaxBehavior = "exclusive",
            TaxCode = ShippingTaxCode,
        },
    };

    private static AddressOptions Address(StoreTaxAddress a) => new()
    {
        Line1 = a.Line1, Line2 = a.Line2, City = a.City, State = a.State, PostalCode = a.PostalCode, Country = a.Country,
    };

    public async Task<StoreTaxResult> CalculateAsync(StoreTaxRequest request, CancellationToken ct = default)
    {
        try
        {
            var calculation = await new StripeTax.CalculationService(Stripe).CreateAsync(BuildCalculationOptions(request), cancellationToken: ct);
            return await ResultAsync(calculation, ct);
        }
        catch (StripeException ex)
        {
            throw Classify(ex, request.ShipTo.State);
        }
        catch (HttpRequestException ex)
        {
            throw new StoreTaxException(StoreTaxFailure.Transient, ex.Message, ex);
        }
    }

    public async Task<StoreTaxResult> GetAsync(string calculationId, CancellationToken ct = default)
    {
        try
        {
            return await ResultAsync(await new StripeTax.CalculationService(Stripe).GetAsync(calculationId, cancellationToken: ct), ct);
        }
        catch (StripeException ex)
        {
            throw Classify(ex, "");
        }
    }

    private async Task<StoreTaxResult> ResultAsync(StripeTax.Calculation calculation, CancellationToken ct)
    {
        // The line items are a separate list; the SDK's auto-pager walks every page of it.
        var lines = new Dictionary<string, long>();
        await foreach (var line in new StripeTax.CalculationService(Stripe).ListLineItemsAutoPagingAsync(
                           calculation.Id, new StripeTax.CalculationLineItemListOptions { Limit = 100 }, cancellationToken: ct))
        {
            if (line.Reference is { } reference) lines[reference] = line.AmountTax;
        }
        return new StoreTaxResult(calculation.Id, calculation.TaxAmountExclusive, calculation.ShippingCost?.AmountTax ?? 0, lines,
            calculation.ExpiresAt);
    }

    public async Task<string> CommitAsync(string calculationId, string reference, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var transaction = await new StripeTax.TransactionService(Stripe).CreateFromCalculationAsync(
                new StripeTax.TransactionCreateFromCalculationOptions { Calculation = calculationId, Reference = reference },
                new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
            return transaction.Id;
        }
        catch (StripeException ex)
        {
            throw Classify(ex, "");
        }
    }

    public async Task<string> ReverseAsync(string transactionId, string reference, StoreTaxReversalMode mode, long flatAmountCents,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var options = new StripeTax.TransactionCreateReversalOptions
            {
                OriginalTransaction = transactionId,
                Reference = reference,
                Mode = mode == StoreTaxReversalMode.Full ? "full" : "partial",
            };
            // A partial reversal is a flat amount across the whole transaction, sent NEGATIVE.
            if (mode == StoreTaxReversalMode.Partial) options.FlatAmount = -Math.Abs(flatAmountCents);
            var reversal = await new StripeTax.TransactionService(Stripe).CreateReversalAsync(
                options, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
            return reversal.Id;
        }
        catch (StripeException ex)
        {
            throw Classify(ex, "");
        }
    }

    /// <summary>What a Stripe refusal means for the checkout — public for tests.</summary>
    public static StoreTaxException Classify(StripeException ex, string state)
    {
        var code = ex.StripeError?.Code;
        var type = ex.StripeError?.Type;
        if (code == "customer_tax_location_invalid")
            return new StoreTaxException(StoreTaxFailure.BuyerAddress, Ben.Service.Models.Store.StoreCheckoutSentences.ZipDoesNotMatch(state), ex);
        if (type == "invalid_request_error")
            return new StoreTaxException(StoreTaxFailure.Configuration, ex.StripeError?.Message ?? ex.Message, ex);
        return new StoreTaxException(StoreTaxFailure.Transient, ex.StripeError?.Message ?? ex.Message, ex);
    }
}

/// <summary>
/// Development without Stripe: 8% on everything, shipping included — and, like the real one,
/// nothing at all without a ship-from address (the e2e harness PUTs one).
/// </summary>
public sealed class FakeStoreTaxService : IStoreTaxService
{
    public const decimal Rate = 0.08m;

    private readonly FakeStoreTaxProbe _probe = new();
    private readonly Dictionary<string, StoreTaxResult> _made = [];
    private int _next;

    /// <summary>Every calculation committed, in order — what tests read.</summary>
    public List<string> Committed { get; } = [];
    public List<(string TransactionId, StoreTaxReversalMode Mode, long FlatAmountCents)> Reversed { get; } = [];

    /// <summary>Make the next calls refuse, as Stripe would (tests).</summary>
    public StoreTaxFailure? Refuse { get; set; }

    public Task<StoreTaxReadiness> ProbeAsync(CancellationToken ct = default) => _probe.ProbeAsync(ct);

    public bool IsAvailable(StoreSettingsSnapshot settings) => settings.HasShipFrom;

    public Task<StoreTaxResult> CalculateAsync(StoreTaxRequest request, CancellationToken ct = default)
    {
        if (Refuse is { } failure)
            throw new StoreTaxException(failure, failure == StoreTaxFailure.BuyerAddress
                ? Ben.Service.Models.Store.StoreCheckoutSentences.ZipDoesNotMatch(request.ShipTo.State) : "refused (fake)");

        var lines = request.Lines.ToDictionary(l => l.Reference, l => (long)Math.Round(l.AmountCents * Rate, MidpointRounding.AwayFromZero));
        var shipping = (long)Math.Round(request.ShippingCents * Rate, MidpointRounding.AwayFromZero);
        lock (_made)
        {
            var result = new StoreTaxResult($"taxcalc_fake_{++_next}", lines.Values.Sum() + shipping, shipping, lines, DateTime.UtcNow.AddHours(24));
            _made[result.CalculationId] = result;
            return Task.FromResult(result);
        }
    }

    public Task<StoreTaxResult> GetAsync(string calculationId, CancellationToken ct = default)
    {
        lock (_made) return _made.TryGetValue(calculationId, out var r)
            ? Task.FromResult(r)
            : throw new StoreTaxException(StoreTaxFailure.Configuration, $"No such calculation {calculationId} (fake).");
    }

    public Task<string> CommitAsync(string calculationId, string reference, string idempotencyKey, CancellationToken ct = default)
    {
        if (Refuse is { } failure) throw new StoreTaxException(failure, "refused (fake)");
        lock (Committed)
        {
            if (!Committed.Contains(calculationId)) Committed.Add(calculationId);
            return Task.FromResult($"tax_txn_fake_{calculationId}");
        }
    }

    public Task<string> ReverseAsync(string transactionId, string reference, StoreTaxReversalMode mode, long flatAmountCents,
        string idempotencyKey, CancellationToken ct = default)
    {
        if (Refuse is { } failure) throw new StoreTaxException(failure, "refused (fake)");
        lock (Reversed)
        {
            Reversed.Add((transactionId, mode, flatAmountCents));
            return Task.FromResult($"tax_rev_fake_{Reversed.Count}");
        }
    }
}
