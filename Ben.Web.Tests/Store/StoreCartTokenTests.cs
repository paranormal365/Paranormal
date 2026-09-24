using System.Net;
using Ben.Service.Models.Store;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Ben.Web.Website.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The cart cookie and the two headers the website sends on the visitor's behalf (storefront S3.4).
/// </summary>
public sealed class StoreCartTokenTests
{
    private const string Token = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";   // 43 URL-safe characters

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"count\":0}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string? Header(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    // ── the client ───────────────────────────────────────────────────────────

    /// <summary>
    /// The discriminating order: the client exists BEFORE the circuit restores the token (MainLayout
    /// does it after the client is built), so a header fixed in the constructor would be missing.
    /// </summary>
    [Fact]
    public async Task A_token_set_after_the_client_is_built_is_on_the_next_store_request()
    {
        var handler = new CapturingHandler();
        var cart = new StoreCartTokenHolder();
        var client = new WebApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") },
            new WebApiTokenStore(), cart);

        cart.Token = Token;
        await client.GetAnonymousItemAsync<StoreCartCount>("/api/store/cart/count");

        Assert.Equal(Token, Header(handler.LastRequest!, WebApiClient.CartHeader));
    }

    [Fact]
    public async Task The_token_goes_to_store_calls_only_signed_in_or_not()
    {
        var handler = new CapturingHandler();
        var cart = new StoreCartTokenHolder { Token = Token };
        var client = new WebApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") },
            new WebApiTokenStore { AccessToken = "bearer" }, cart);

        await client.GetItemAsync<StoreCartCount>("/api/store/cart/count");
        Assert.Equal(Token, Header(handler.LastRequest!, WebApiClient.CartHeader));

        await client.GetItemAsync<StoreCartCount>("/api/notifications/summary");
        Assert.Null(Header(handler.LastRequest!, WebApiClient.CartHeader));
    }

    [Fact]
    public async Task The_visitors_address_is_on_every_request_once_it_is_known()
    {
        var handler = new CapturingHandler();
        var visitor = new VisitorAddressHolder();
        var client = new WebApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") },
            new WebApiTokenStore(), visitor: visitor);

        await client.GetAnonymousItemAsync<StoreCartCount>("/api/public/store/info");
        Assert.Null(Header(handler.LastRequest!, "X-Forwarded-For"));

        visitor.Address = "203.0.113.9";
        await client.GetItemAsync<StoreCartCount>("/api/notifications/summary");
        Assert.Equal("203.0.113.9", Header(handler.LastRequest!, "X-Forwarded-For"));
        await client.PostAnonymousVoidAsync("/api/public/store/products/x/viewed", new { });
        Assert.Equal("203.0.113.9", Header(handler.LastRequest!, "X-Forwarded-For"));
    }

    // ── the cookie ───────────────────────────────────────────────────────────

    private static DefaultHttpContext Page(bool https = true, string path = "/store", string? cookie = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Path = path;
        context.Request.Headers.Accept = "text/html,application/xhtml+xml";
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.4");
        if (cookie is not null) context.Request.Headers.Cookie = $"{StoreCartCookie.Name}={cookie}";
        return context;
    }

    private static string? SetCookie(HttpContext context) => context.Response.Headers.SetCookie.FirstOrDefault();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_first_page_makes_an_http_only_cookie_secure_when_the_request_is(bool https)
    {
        var context = Page(https);
        var cart = new StoreCartTokenHolder();
        var visitor = new VisitorAddressHolder();

        StoreCartCookie.Apply(context, cart, visitor, storeIsOn: true);

        var cookie = SetCookie(context)!;
        Assert.StartsWith($"{StoreCartCookie.Name}={cart.Token};", cookie);
        Assert.Equal(StoreCartRules.TokenLength, cart.Token!.Length);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(https, cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("198.51.100.4", visitor.Address);
    }

    [Fact]
    public void An_existing_cookie_is_used_and_not_remade()
    {
        var context = Page(cookie: Token);
        var cart = new StoreCartTokenHolder();

        StoreCartCookie.Apply(context, cart, new VisitorAddressHolder(), storeIsOn: true);

        Assert.Equal(Token, cart.Token);
        Assert.Null(SetCookie(context));
    }

    [Theory]
    [InlineData("/store", false, false)]              // the store is dark: no cookie for anyone
    [InlineData("/_blazor", true, false)]             // the circuit's own traffic
    [InlineData("/_framework/blazor.web.js", true, false)]
    [InlineData("/media/store-image/abc", true, false)]
    [InlineData("/css/site.css", true, false)]
    [InlineData("/store/cart", true, true)]
    public void A_cookie_is_made_only_for_a_page_while_the_store_is_open(string path, bool storeIsOn, bool made)
    {
        var context = Page(path: path);
        StoreCartCookie.Apply(context, new StoreCartTokenHolder(), new VisitorAddressHolder(), storeIsOn);
        Assert.Equal(made, SetCookie(context) is not null);
    }

    [Fact]
    public void A_malformed_cookie_is_replaced()
    {
        var context = Page(cookie: "not-a-real-token");
        var cart = new StoreCartTokenHolder();

        StoreCartCookie.Apply(context, cart, new VisitorAddressHolder(), storeIsOn: true);

        Assert.NotEqual("not-a-real-token", cart.Token);
        Assert.NotNull(SetCookie(context));
    }
}
