using Ben.Web.Website.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The handle that replaced an access token in a URL (item 201).
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> Media tickets used to be the viewer's API token encrypted into
/// the query string: unreadable, bound to one id, expiring — and 2504 characters. IIS refuses a
/// query string over 2048 with 404.15 <i>before the request reaches the application</i>, so
/// nothing of ours logged it, an IIS error page came back, and Ben's profile photos were simply
/// invisible on the deployed site while working perfectly on localhost, where Kestrel has no such
/// limit.</para>
///
/// <para>The first fix raised the IIS limit. That unblocked it and settled nothing: the token
/// grows on its own, so one more claim in a JWT puts some viewers back over, in exactly the same
/// invisible way. These tests pin the actual fix — the token stops travelling — and the ceiling
/// that would have caught the original bug before it ever deployed.</para>
/// </remarks>
public sealed class BrowserTicketStoreTests
{
    private static BrowserTicketStore Store() => new(new MemoryCache(new MemoryCacheOptions()));

    /// <summary>A realistic bearer token: this is what used to be carried in the URL.</summary>
    private static string BigToken() =>
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + new string('x', 1800) + ".signature";

    /// <summary>
    /// The rule that would have caught this before deployment: a ticket has to fit in a URL.
    /// </summary>
    /// <remarks>
    /// 2048 is the IIS default. The published web.config raises it, but this asserts against the
    /// DEFAULT on purpose — the value a ticket must fit inside is the one an unconfigured server
    /// enforces, not the one this deployment happens to allow. A ticket that only fits because of
    /// a config change is a ticket waiting to break somewhere else.
    /// </remarks>
    [Fact]
    public void A_ticket_fits_inside_the_default_iis_query_string_limit()
    {
        var ticket = Store().Issue("media", Guid.NewGuid(), BigToken(), TimeSpan.FromHours(1));

        Assert.True(ticket.Length < 2048,
            $"A ticket is {ticket.Length} characters; IIS refuses a query string over 2048 before "
            + "the application ever sees the request.");
    }

    /// <summary>And is nowhere near it, whatever the token does next.</summary>
    [Fact]
    public void A_ticket_does_not_grow_with_the_token()
    {
        var store = Store();
        var small = store.Issue("media", Guid.NewGuid(), "tiny", TimeSpan.FromHours(1));
        var huge  = store.Issue("media", Guid.NewGuid(), new string('y', 100_000), TimeSpan.FromHours(1));

        Assert.Equal(small.Length, huge.Length);
        Assert.True(small.Length < 64, $"Expected a short handle, got {small.Length} characters.");
    }

    /// <summary>The token itself never appears in what the browser is given.</summary>
    [Fact]
    public void The_token_does_not_travel()
    {
        var token = BigToken();
        var ticket = Store().Issue("media", Guid.NewGuid(), token, TimeSpan.FromHours(1));

        Assert.DoesNotContain("eyJhbGci", ticket, StringComparison.Ordinal);
        Assert.DoesNotContain("signature", ticket, StringComparison.Ordinal);
    }

    /// <summary>A handle is URL-safe, so nothing has to escape it into being longer.</summary>
    [Fact]
    public void A_ticket_needs_no_escaping()
    {
        var ticket = Store().Issue("media", Guid.NewGuid(), BigToken(), TimeSpan.FromHours(1));

        Assert.Equal(ticket, Uri.EscapeDataString(ticket));
    }

    /// <summary>Round trip: what goes in comes back.</summary>
    [Fact]
    public void A_ticket_redeems_to_the_token_it_stands_for()
    {
        var store = Store();
        var id = Guid.NewGuid();
        var token = BigToken();

        Assert.Equal(token, store.Redeem("media", id, store.Issue("media", id, token, TimeSpan.FromHours(1))));
    }

    /// <summary>
    /// Bound to one id — a ticket lifted from one image cannot fetch another.
    /// </summary>
    [Fact]
    public void A_ticket_for_one_file_does_not_open_another()
    {
        var store = Store();
        var ticket = store.Issue("media", Guid.NewGuid(), BigToken(), TimeSpan.FromHours(1));

        Assert.Null(store.Redeem("media", Guid.NewGuid(), ticket));
    }

    /// <summary>
    /// A media handle is not an upload handle, however valid it is.
    /// </summary>
    /// <remarks>
    /// The two have very different lifetimes — one hour against twelve — so letting a media ticket
    /// be redeemed as an upload one would quietly hand the shorter-lived thing the longer life.
    /// </remarks>
    [Fact]
    public void A_ticket_cannot_cross_scopes()
    {
        var store = Store();
        var id = Guid.NewGuid();
        var ticket = store.Issue("media", id, BigToken(), TimeSpan.FromHours(1));

        Assert.Null(store.Redeem("upload", id, ticket));
    }

    /// <summary>
    /// The same viewer, file and hour give the same URL — which is what lets the browser cache
    /// the image instead of refetching it on every render.
    /// </summary>
    [Fact]
    public void The_same_request_gives_the_same_url()
    {
        var store = Store();
        var id = Guid.NewGuid();
        var token = BigToken();

        Assert.Equal(
            store.Issue("media", id, token, TimeSpan.FromHours(1)),
            store.Issue("media", id, token, TimeSpan.FromHours(1)));
    }

    /// <summary>A different viewer gets a different handle for the same file.</summary>
    [Fact]
    public void A_different_viewer_gets_a_different_ticket()
    {
        var store = Store();
        var id = Guid.NewGuid();

        Assert.NotEqual(
            store.Issue("media", id, "token-for-alex", TimeSpan.FromHours(1)),
            store.Issue("media", id, "token-for-sam", TimeSpan.FromHours(1)));
    }

    /// <summary>Nonsense redeems to nothing rather than throwing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-handle")]
    public void An_unknown_ticket_is_refused_quietly(string ticket)
        => Assert.Null(Store().Redeem("media", Guid.NewGuid(), ticket));
}
