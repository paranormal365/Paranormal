using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store's rate limits key on who is calling, never on anything the caller makes up
/// (storefront S0.13).
/// </summary>
/// <remarks>
/// <para>A guest's cart travels in an <c>X-Ben-Cart</c> header and an emailed order link carries a
/// <c>?t=</c> token. Both are the caller's to invent, so a partition keyed on either gives a script a
/// fresh window for every value — the checkout ceiling of ten a minute becomes unlimited by rotating
/// the header. These facts run the store's own partition function, the one the policies are
/// registered with, against requests that differ only in those values.</para>
///
/// <para>The visitor's address arrives forwarded: the website calls the API from this machine and
/// passes the visitor on in <c>X-Forwarded-For</c>, which the API trusts from loopback only. The
/// forwarded facts run the real <see cref="ForwardedHeadersMiddleware"/> with the options
/// <c>Program.cs</c> uses.</para>
/// </remarks>
public sealed class StoreRateLimitPartitionTests
{
    private static HttpContext Request(string ip, string? cart = null, string? token = null, string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (cart is not null) context.Request.Headers["X-Ben-Cart"] = cart;
        if (token is not null) context.Request.QueryString = new QueryString($"?t={token}");
        if (userId is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        return context;
    }

    /// <summary>Runs the website's forwarded request through the middleware the API uses.</summary>
    private static async Task<HttpContext> ForwardedAsync(string peer, string visitor)
    {
        var context = Request(peer);
        context.Request.Headers["X-Forwarded-For"] = visitor;
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor }));
        await middleware.Invoke(context);
        return context;
    }

    private static PartitionedRateLimiter<HttpContext> Limiter(string policy)
        => PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimiting.StorePartition(c, policy));

    [Fact]
    public void The_four_store_limits_are_the_planned_ones()
    {
        Assert.Equal(240, RateLimiting.StorePoliciesPerMinute[RateLimiting.StoreBrowsePolicy]);
        Assert.Equal(120, RateLimiting.StorePoliciesPerMinute[RateLimiting.StoreCartPolicy]);
        Assert.Equal(10, RateLimiting.StorePoliciesPerMinute[RateLimiting.StoreCheckoutPolicy]);
        Assert.Equal(30, RateLimiting.StorePoliciesPerMinute[RateLimiting.StoreOrderDoorPolicy]);
    }

    [Fact]
    public void Two_cart_tokens_from_one_address_share_one_window()
        => Assert.Equal(RateLimiting.ClientKey(Request("203.0.113.9", cart: "aaaa")),
                        RateLimiting.ClientKey(Request("203.0.113.9", cart: "bbbb")));

    [Fact]
    public void Two_order_link_tokens_from_one_address_share_one_window()
        => Assert.Equal(RateLimiting.ClientKey(Request("203.0.113.9", token: "1111")),
                        RateLimiting.ClientKey(Request("203.0.113.9", token: "2222")));

    /// <summary>The attack itself: twenty made-up carts, one address, a ceiling of ten.</summary>
    [Fact]
    public void Rotating_the_cart_header_does_not_get_past_the_checkout_ceiling()
    {
        using var limiter = Limiter(RateLimiting.StoreCheckoutPolicy);

        var admitted = Enumerable.Range(0, 20)
            .Count(i => limiter.AttemptAcquire(Request("203.0.113.9", cart: $"made-up-{i}", token: $"t-{i}")).IsAcquired);

        Assert.Equal(10, admitted);
    }

    [Fact]
    public async Task Two_visitors_forwarded_by_the_website_get_a_window_each()
    {
        var sarah = await ForwardedAsync("127.0.0.1", "203.0.113.9");
        var ed = await ForwardedAsync("127.0.0.1", "198.51.100.4");

        Assert.Equal("ip:203.0.113.9", RateLimiting.ClientKey(sarah));
        Assert.Equal("ip:198.51.100.4", RateLimiting.ClientKey(ed));

        using var limiter = Limiter(RateLimiting.StoreCheckoutPolicy);
        for (var i = 0; i < 10; i++) Assert.True(limiter.AttemptAcquire(sarah).IsAcquired);
        Assert.False(limiter.AttemptAcquire(sarah).IsAcquired);
        Assert.True(limiter.AttemptAcquire(ed).IsAcquired, "one visitor's checkouts used up another's");
    }

    /// <summary>Anybody on the internet can send the header; only this machine is believed.</summary>
    [Fact]
    public async Task A_forwarded_address_from_anywhere_but_this_machine_is_ignored()
    {
        var spoofed = await ForwardedAsync("198.51.100.77", "203.0.113.9");
        Assert.Equal("ip:198.51.100.77", RateLimiting.ClientKey(spoofed));
    }

    [Fact]
    public void A_signed_in_buyer_is_keyed_by_account_wherever_they_are()
    {
        var userId = Guid.NewGuid().ToString();
        Assert.Equal($"user:{userId}", RateLimiting.ClientKey(Request("203.0.113.9", cart: "x", userId: userId)));
        Assert.Equal(RateLimiting.ClientKey(Request("203.0.113.9", userId: userId)),
                     RateLimiting.ClientKey(Request("198.51.100.4", userId: userId)));
    }
}
