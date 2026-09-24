namespace Ben.Service.Models.Store;

// The cart as every surface draws it — the header menu, the drawer and the cart page (storefront
// S3.1). All three read the SAME record, so the three money rows (Products, Shipping, Total before
// tax) cannot disagree: StoreCartSurfacesAgreeTests and The_four_surfaces_agree hold them to it.

/// <summary>One line of the cart, re-read from the variant on every look — the cart stores no prices.</summary>
/// <param name="Available">What can still be bought: on hand less held, plus whatever this buyer's own open checkout is holding.</param>
/// <param name="YouSave">The old price less the price, times the quantity; null when there is no old price.</param>
/// <param name="IsPurchasable">False when the line has a <paramref name="Problem"/>.</param>
/// <param name="Problem">
/// Exactly one of <see cref="StoreCartSentences.SoldOut"/>, <see cref="StoreCartSentences.OnlyLeft"/>
/// or <see cref="StoreCartSentences.NoLongerAvailable"/>; null for a line that can be bought.
/// </param>
public sealed record StoreCartLineView(
    Guid VariantId, Guid ProductId, string ProductName, string? VariantName, string Sku, string ProductSlug,
    Guid? ImageUploadFileId, decimal UnitPrice, decimal? CompareAtPrice, int Quantity, int Available,
    decimal LineTotal, decimal? YouSave, bool IsPurchasable, string? Problem);

/// <summary>The whole cart and its money, as the buyer sees it before checkout.</summary>
/// <param name="CartId">Null when there is no cart yet — nothing has been added from this browser or account.</param>
/// <param name="Count">Units, not lines: two of one thing and one of another is 3.</param>
/// <param name="Discount">What the applied code takes off the products; never more than they cost, never off shipping.</param>
/// <param name="CouponProblem">Why the applied code does not count right now (the minimum, the dates); it stays applied.</param>
/// <param name="Shipping">
/// Null ONLY when the cart is empty ("–"); otherwise the flat rate, or 0 when the order ships free.
/// </param>
/// <param name="FreeShippingThreshold">0 when orders never ship free.</param>
/// <param name="TotalBeforeTax">Products less the discount plus shipping. Sales tax is worked out at checkout.</param>
/// <param name="CheckoutEnabled">The store's pause switch (<c>store.checkout-enabled</c>).</param>
/// <param name="WhyNotCheckout">The sentence shown where "To checkout" would be, when <paramref name="CanCheckout"/> is false.</param>
/// <param name="Notice">
/// What happened to the last change when it was not quite what was asked — "Only 3 left." after a
/// quantity was capped. Set only on the answer to a write; the surfaces show it as a warning.
/// </param>
public sealed record StoreCartView(
    Guid? CartId, IReadOnlyList<StoreCartLineView> Lines, int Count, decimal Subtotal,
    string? CouponCode, decimal Discount, string? CouponProblem,
    decimal? Shipping, bool ShippingIsFree, decimal FreeShippingThreshold, int LowStockThreshold,
    string? SupportEmail, decimal TotalBeforeTax, bool CheckoutEnabled, bool CanCheckout, string? WhyNotCheckout,
    string? Notice = null)
{
    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>What the header badge shows: units in the cart.</summary>
public sealed record StoreCartCount(int Count);

public sealed record AddToCartRequest(Guid VariantId, int Quantity);

/// <summary>0 removes the line.</summary>
public sealed record SetCartQuantityRequest(int Quantity);

public sealed record ApplyCartCouponRequest(string Code);

/// <summary>The cart's sentences, shared by the server that says them and the tests and pages that look for them.</summary>
public static class StoreCartSentences
{
    public const string SoldOut = "Sold out.";
    public static string OnlyLeft(int n) => $"Only {n} left.";
    /// <summary>The one sentence for an inactive or removed variant, an inactive product or a hidden category.</summary>
    public const string NoLongerAvailable = "That item is no longer available.";

    public const string ChooseHowMany = "Choose how many.";
    public const string FixTheLines = "Fix the items marked above before checking out.";
    public const string CheckoutPaused = "The store isn't taking orders at the moment.";
    public const string NoCart = "Your browser didn't send a cart — reload the page and try again.";
    public const string NotInCart = "That item isn't in your cart.";
    public const string EnterACode = "Enter a code.";
    public const string NoCode = "There's no code on your cart.";
    public const string AddSomethingFirst = "Add something to your cart before using a code.";
    public static readonly string TooManyLines = $"Your cart can hold {StoreCartRules.MaxDistinctLines} different items — check out or remove one first.";
    public static string AtMost(int n) => $"You can buy up to {n} at a time.";
}
