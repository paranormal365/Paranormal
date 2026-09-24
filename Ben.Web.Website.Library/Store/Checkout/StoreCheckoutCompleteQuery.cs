namespace Ben.Web.Website.Library.Store.Checkout;

/// <summary>
/// What the thank-you page reads from its address (storefront S4.11): the order, and — when Stripe
/// sent the buyer back from a bank's own page — how Stripe says that went.
/// </summary>
/// <remarks>
/// Stripe appends <c>payment_intent</c>, <c>payment_intent_client_secret</c> and
/// <c>redirect_status</c> to the return address. Only <c>redirect_status=failed</c> is acted on,
/// and only to stop waiting sooner: the order's own status, read from our server, is what the page
/// believes — an address anybody can type proves nothing.
/// </remarks>
public sealed record StoreCheckoutCompleteQuery(Guid? OrderId, bool StripeSaysFailed)
{
    /// <summary>How long to wait between status reads: quick at first, then easing off. About a minute in all.</summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff =
        [.. new[] { 1, 1, 2, 2, 3, 3, 5, 5, 8, 8, 13, 13 }.Select(s => TimeSpan.FromSeconds(s))];

    public static StoreCheckoutCompleteQuery Parse(string uri)
    {
        // Everything after '?', by hand: on Unix, Uri reads "/store/…" as an absolute file path and drops the query.
        var query = uri.Split('#')[0] is var noFragment && noFragment.Contains('?') ? noFragment[noFragment.IndexOf('?')..] : "";
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);
        Guid? order = values.TryGetValue("order", out var o) && Guid.TryParse(o.ToString(), out var id) && id != Guid.Empty ? id : null;
        var failed = values.TryGetValue("redirect_status", out var r) && string.Equals(r.ToString(), "failed", StringComparison.OrdinalIgnoreCase);
        return new(order, failed);
    }

    /// <summary>The private token in an order's own address (<c>/store/orders/{id}?t=…</c>), which opens the full order.</summary>
    public static string? TokenIn(string? orderUrl)
    {
        if (string.IsNullOrEmpty(orderUrl) || !orderUrl.Contains('?')) return null;
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(orderUrl[orderUrl.IndexOf('?')..]);
        return values.TryGetValue("t", out var t) && !string.IsNullOrEmpty(t.ToString()) ? t.ToString() : null;
    }
}
