using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// The checkout (storefront S4.1): what the buyer types, and what the server answers with. Every
// figure is the server's — the page draws these, it never adds them up itself.

/// <summary>A US delivery or billing address as typed. <paramref name="State"/> is the two-letter code.</summary>
public sealed record StoreAddressInput(
    string FullName, string Phone, string Street1, string? Street2, string City, string State, string Zip);

/// <param name="Billing">Null when billing is the same as shipping.</param>
public sealed record StoreCheckoutRequest(
    string Email, StoreAddressInput Shipping, StoreAddressInput? Billing, string? BillCompany,
    bool AgreedToTerms, string? BuyerNotes);

/// <param name="Tax">All the sales tax, shipping's included.</param>
/// <param name="ShippingTax">The part of <paramref name="Tax"/> charged on shipping (some states tax it).</param>
public sealed record StoreCheckoutTotals(
    decimal Subtotal, decimal Discount, decimal Shipping, decimal Tax, decimal Total, decimal ShippingTax = 0m);

/// <summary>An order placed and waiting for payment — or already paid, when nothing was owed.</summary>
/// <param name="ClientSecret">What the Payment Element mounts with; null when <paramref name="PaidWithoutCharge"/>.</param>
/// <param name="PublishableKey">The Stripe key the page loads Stripe.js with; a fake one in test checkout.</param>
/// <param name="FakeCheckout">No real Stripe: the page offers "Test checkout — no card needed".</param>
/// <param name="ReservationExpiresUtc">When the held stock goes back — the page counts down to it.</param>
public sealed record StoreCheckoutPrepared(
    Guid OrderId, int OrderNumber, string? ClientSecret, string? PublishableKey, StoreCheckoutTotals Totals,
    bool PaidWithoutCharge, string ReturnUrl, DateTime ReservationExpiresUtc, bool FakeCheckout = false);

/// <summary>Where an order stands, for the page Stripe sends the buyer back to (it polls this).</summary>
/// <param name="IsFinal">Paid, cancelled or refunded — nothing more will change on its own.</param>
/// <param name="Problem">Why it did not go through, in words; null when nothing is wrong.</param>
/// <param name="OrderUrl">The order's own page, for the person who placed it (with its token for a guest).</param>
public sealed record StoreOrderStatusView(
    Guid OrderId, int OrderNumber, StoreOrderStatus Status, bool IsFinal, string? Problem, string? OrderUrl,
    DateTime? ReservationExpiresUtc);

/// <summary>The checkout's sentences, shared by the service that says them and the tests and pages that look for them.</summary>
public static class StoreCheckoutSentences
{
    public const string Paused = "The store isn't taking orders at the moment.";
    public const string PaymentsNotSetUp = "Online payment isn't set up yet — nothing was charged.";
    public const string EmailInvalid = "Enter a valid email address.";
    public static string Required(string field) => $"{field} is required.";
    public const string ChooseAState = "Choose a state.";
    public const string ZipInvalid = "Enter a 5-digit ZIP code.";
    public const string AgreeToTerms = "You need to agree to the terms and conditions to place an order.";
    public const string CartEmpty = "Your cart is empty.";
    public static string TooManyLines(int max) => $"A cart holds up to {max} different items.";
    public static string ItemsChanged(string firstProblem) => $"Some items in your cart changed: {firstProblem}";
    public const string TooManyOpen = "Too many checkouts are open from this connection — try again in a few minutes.";
    public static string OnlyLeft(int available, string product) => $"Only {available} of {product} are left.";
    public const string StockBusy = "Stock is being updated — try again.";
    public const string TaxUnavailable = "Sales tax couldn't be calculated just now. Try again in a moment.";
    public static string ZipDoesNotMatch(string state) => $"We couldn't match that ZIP code to {state} — check the address.";
    public const string OrderingPaused = "Online ordering is paused for a moment — please try again later.";
    public const string EarlierPaymentGoingThrough = "Your earlier payment is still going through — give it a moment.";
    public const string EarlierPaymentProcessing = "Your earlier payment is still being processed — wait a moment and refresh.";
    public const string ReservationExpired = "Your reservation expired — we've checked stock again.";
}
