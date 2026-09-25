using Ben.Service.Models.Store;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.RegularExpressions;
using Ben.Web.Website.Library.Store.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The store's small shared pieces draw what they promise (storefront S2.5).</summary>
public sealed class StoreComponentRenderTests
{
    internal static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters, Action<IServiceCollection>? services = null)
        where T : IComponent
    {
        var collection = new ServiceCollection();
        services?.Invoke(collection);
        // What the cart buttons and hearts on cards and the product page reach for (S3.5, S6.3), unless the test
        // brought its own.
        collection.TryAddSingleton(new Moq.Mock<Ben.Web.Services.IBenUserState>().Object);
        collection.TryAddScoped(sp => new Ben.Web.Services.StoreCartState(
            sp.GetService<Ben.Web.Services.IBenAdminClient>() ?? new Moq.Mock<Ben.Web.Services.IBenAdminClient>().Object,
            sp.GetRequiredService<Ben.Web.Services.IBenUserState>()));
        collection.TryAddScoped<Ben.Web.Website.Library.Kit.BenToastService>();
        // The heart on every card (S6.3).
        collection.TryAddScoped(sp => new Ben.Web.Services.StoreFavouriteState(
            sp.GetService<Ben.Web.Services.IBenAdminClient>() ?? new Moq.Mock<Ben.Web.Services.IBenAdminClient>().Object,
            sp.GetRequiredService<Ben.Web.Services.IBenUserState>()));
        await using var provider = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    [Theory]
    [InlineData(StoreCheckoutStep.Cart, "Step 1 of 4 · Cart", 0)]
    [InlineData(StoreCheckoutStep.PlaceOrder, "Step 2 of 4 · Place order", 1)]
    [InlineData(StoreCheckoutStep.Payment, "Step 3 of 4 · Payment", 2)]
    [InlineData(StoreCheckoutStep.Complete, "Step 4 of 4 · Complete", 3)]
    public async Task The_steps_show_all_four_and_say_which_this_is(StoreCheckoutStep current, string caption, int done)
    {
        var html = await RenderAsync<StoreCheckoutSteps>(new() { [nameof(StoreCheckoutSteps.Current)] = current });

        foreach (var label in new[] { "Cart", "Place order", "Payment", "Complete" }) Assert.Contains($">{label}<", html);
        Assert.Contains(caption, html);
        Assert.Equal(done, Regex.Matches(html, "ben-steps__item--done").Count);
        Assert.Single(Regex.Matches(html, "aria-current=\"step\""));
    }

    /// <summary>From the cart the next step is a link, and from the checkout the cart is; nothing skips ahead to paying (S4.11).</summary>
    [Theory]
    [InlineData(StoreCheckoutStep.Cart, "href=\"/store/checkout\"")]
    [InlineData(StoreCheckoutStep.PlaceOrder, "href=\"/store/cart\"")]
    [InlineData(StoreCheckoutStep.Payment, null)]
    [InlineData(StoreCheckoutStep.Complete, null)]
    public async Task The_steps_link_only_one_step_either_way(StoreCheckoutStep current, string? link)
    {
        var html = await RenderAsync<StoreCheckoutSteps>(new() { [nameof(StoreCheckoutSteps.Current)] = current });

        if (link is null) Assert.DoesNotContain("href=", html);
        else Assert.Single(Regex.Matches(html, "href="));
        if (link is not null) Assert.Contains(link, html);
    }

    [Fact]
    public async Task A_price_strikes_through_the_old_one_and_shows_a_range()
    {
        var sale = await RenderAsync<StorePrice>(new() { [nameof(StorePrice.Price)] = 59.99m, [nameof(StorePrice.Was)] = 69.99m });
        Assert.Contains("<del class=\"ben-price__was\">$69.99</del>", sale);
        Assert.Contains("$59.99", sale);

        var range = await RenderAsync<StorePrice>(new() { [nameof(StorePrice.Price)] = 39m, [nameof(StorePrice.Max)] = 49m });
        Assert.Contains("$39.00 – $49.00", range);

        var noSale = await RenderAsync<StorePrice>(new() { [nameof(StorePrice.Price)] = 70m, [nameof(StorePrice.Was)] = 69.99m });
        Assert.DoesNotContain("<del", noSale);
    }

    [Fact]
    public async Task Stars_are_absent_until_somebody_reviews_and_round_to_halves()
    {
        Assert.Equal("", (await RenderAsync<StoreStars>(new() { [nameof(StoreStars.Rating)] = 0m, [nameof(StoreStars.Count)] = 0 })).Trim());

        var html = await RenderAsync<StoreStars>(new() { [nameof(StoreStars.Rating)] = 3.6m, [nameof(StoreStars.Count)] = 7 });
        Assert.Contains("3.6 out of 5 stars from 7 reviews", html);
        Assert.Single(Regex.Matches(html, "ben-stars__half"));
        Assert.Single(Regex.Matches(html, "ben-stars__empty"));
        Assert.Contains("(7)", html);
    }

    [Fact]
    public async Task Badges_say_sold_out_discount_and_new()
    {
        var html = await RenderAsync<StoreBadges>(new()
        {
            [nameof(StoreBadges.SoldOut)] = true, [nameof(StoreBadges.Discount)] = 14, [nameof(StoreBadges.IsNew)] = true,
        });
        Assert.Contains(">Sold out<", html);
        Assert.Contains(">-14%<", html);
        Assert.Contains(">New<", html);
        Assert.Equal("", (await RenderAsync<StoreBadges>(new())).Trim());
    }

    [Fact]
    public async Task The_quantity_buttons_stop_at_the_ends()
    {
        var atOne = await RenderAsync<StoreQuantity>(new() { [nameof(StoreQuantity.Value)] = 1, [nameof(StoreQuantity.Max)] = 3 });
        Assert.Matches("aria-label=\"One fewer\" disabled", atOne);
        Assert.DoesNotMatch("aria-label=\"One more\" disabled", atOne);

        var atMax = await RenderAsync<StoreQuantity>(new() { [nameof(StoreQuantity.Value)] = 3, [nameof(StoreQuantity.Max)] = 3 });
        Assert.Matches("aria-label=\"One more\" disabled", atMax);
    }

    [Fact]
    public async Task The_empty_state_offers_a_way_on()
    {
        var html = await RenderAsync<StoreEmptyState>(new()
        {
            [nameof(StoreEmptyState.Title)] = "Your cart is empty", [nameof(StoreEmptyState.LinkText)] = "Browse the store",
            [nameof(StoreEmptyState.LinkHref)] = "/store",
        });
        Assert.Contains("Your cart is empty", html);
        Assert.Contains("href=\"/store\"", html);
    }

    private static StoreInvoiceRecord Invoice(decimal refunded) => new(
        100042, new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 15, 2, 0, DateTimeKind.Utc),
        new StoreInvoiceParty("IsHaunted.com", null, "401 Church St", null, "Nashville", "TN", "37219", "shop@example.com"),
        new StoreInvoiceParty("Ada Buyer", "Spook Co", "9 Oak Ave", null, "Memphis", "TN", "38103", "ada@example.com"),
        new StoreInvoiceParty("Ada Buyer", null, "1 Elm St", "Apt 2", "Nashville", "TN", "37203", null),
        [new StoreInvoiceLine("K-II EMF Meter", "KII-EMF", 2, 59.99m, 12m, 10.09m, 119.98m)],
        119.98m, 12m, "GHOST10", 7.95m, 10.83m, 126.76m, refunded, 126.76m - refunded, "USPS", "9400 1", 30, "shop@example.com");

    /// <summary>The invoice (S4.13): three parties, the line's discount and tax, and a balance only once something was refunded.</summary>
    [Fact]
    public async Task The_invoice_shows_its_parties_and_a_balance_only_after_a_refund()
    {
        var viewer = new Moq.Mock<Ben.Web.Services.IBenUserState>();
        viewer.SetupGet(u => u.BrowserTimeZone).Returns(TimeZoneInfo.Utc);
        void Utc(IServiceCollection c) => c.AddSingleton(viewer.Object);
        var paid = await RenderAsync<Ben.Web.Website.Library.Store.Orders.StoreInvoiceSheet>(new() { ["Invoice"] = Invoice(0m) }, Utc);
        var refunded = await RenderAsync<Ben.Web.Website.Library.Store.Orders.StoreInvoiceSheet>(new() { ["Invoice"] = Invoice(26.76m) }, Utc);

        Assert.Contains("Invoice: #100042", paid);
        Assert.Contains("09/20/2026 3:02 PM", paid);   // paid, not placed
        foreach (var party in new[] { "401 Church St", "Spook Co", "Apt 2" }) Assert.Contains(party, paid);
        Assert.Contains("−$12.00", paid);
        Assert.Contains("Discount (GHOST10)", paid);
        Assert.DoesNotContain("invoice-balance", paid);
        Assert.Contains("−$26.76", refunded);
        Assert.Matches("invoice-balance\"[^>]*>\\$100\\.00<", refunded);
    }
}
