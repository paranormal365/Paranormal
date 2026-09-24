using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Store;
using Ben.Web.Services;
using Ben.Web.Website.Library.Store.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The store's bigger shared pieces: the card, the option picker, the gallery and the filters (storefront S2.6).</summary>
public sealed class StoreSharedComponentTests
{
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/store/products");
    }

    private static void Services(IServiceCollection services)
    {
        var urls = new Mock<IMediaUrlBuilder>();
        urls.Setup(u => u.StoreImage(It.IsAny<Guid>(), It.IsAny<bool>()))
            .Returns((Guid id, bool thumb) => thumb ? $"/media/store-image/{id}/thumb" : $"/media/store-image/{id}");
        services.AddSingleton(urls.Object);
        services.AddSingleton<NavigationManager, FakeNav>();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);
        services.AddSingleton(new Mock<Microsoft.JSInterop.IJSRuntime>().Object);
    }

    private static Task<string> RenderAsync<T>(Dictionary<string, object?> p) where T : IComponent
        => StoreComponentRenderTests.RenderAsync<T>(p, Services);

    // ── option picker ────────────────────────────────────────────────────────

    private static readonly Guid Colour = Guid.NewGuid(), Size = Guid.NewGuid();
    private static readonly Guid Black = Guid.NewGuid(), Camo = Guid.NewGuid(), Small = Guid.NewGuid(), Large = Guid.NewGuid();

    private static readonly StoreOptionRecord[] Options =
    [
        new(Colour, "Colour", StoreOptionKind.Swatch, [new(Black, "Black", "#111111", 0, true), new(Camo, "Camo", "#5b6b3a", 1, true)]),
        new(Size, "Size", StoreOptionKind.Pill, [new(Small, "Small", null, 0, true), new(Large, "Large", null, 1, true)]),
    ];

    // Camo comes only in Small, and none is left; Black comes in both.
    private static readonly StoreVariantPublicRecord[] Variants =
    [
        new(Guid.NewGuid(), "B-S", "Black / Small", 10m, null, 3, [Black, Small], true),
        new(Guid.NewGuid(), "B-L", "Black / Large", 12m, null, 1, [Black, Large], false),
        new(Guid.NewGuid(), "C-S", "Camo / Small", 11m, null, 0, [Camo, Small], false),
    ];

    [Fact]
    public void A_choice_is_available_sold_out_or_unavailable()
    {
        var small = new Dictionary<Guid, Guid> { [Size] = Small };
        var large = new Dictionary<Guid, Guid> { [Size] = Large };

        Assert.Equal(StoreOptionPicker.ChoiceState.SoldOut, StoreOptionPicker.StateOf(Options, Variants, small, Colour, Camo));
        Assert.Equal(StoreOptionPicker.ChoiceState.Unavailable, StoreOptionPicker.StateOf(Options, Variants, large, Colour, Camo));
        Assert.Equal(StoreOptionPicker.ChoiceState.Available, StoreOptionPicker.StateOf(Options, Variants, large, Colour, Black));
    }

    [Fact]
    public async Task OptionPicker_renders_sold_out_and_unavailable_differently()
    {
        string CamoLabel(string html) => Regex.Match(html, "<label[^>]*title=\"Camo[^\"]*\"[^>]*>").Value;
        string CamoInput(string html) => Regex.Match(html, $"<input[^>]*id=\"opt-{Colour:N}-{Camo:N}\"[^>]*>").Value;

        var smallChosen = await RenderAsync<StoreOptionPicker>(new()
        {
            [nameof(StoreOptionPicker.Options)] = Options, [nameof(StoreOptionPicker.Variants)] = Variants,
            [nameof(StoreOptionPicker.Selected)] = new Dictionary<Guid, Guid> { [Size] = Small },
        });
        Assert.Contains("ben-selector--soldout", CamoLabel(smallChosen));
        Assert.Contains("title=\"Camo — Sold out\"", CamoLabel(smallChosen));
        Assert.DoesNotContain("disabled", CamoInput(smallChosen));
        Assert.DoesNotContain("line-through", smallChosen);

        var largeChosen = await RenderAsync<StoreOptionPicker>(new()
        {
            [nameof(StoreOptionPicker.Options)] = Options, [nameof(StoreOptionPicker.Variants)] = Variants,
            [nameof(StoreOptionPicker.Selected)] = new Dictionary<Guid, Guid> { [Size] = Large },
        });
        Assert.Contains("disabled", CamoInput(largeChosen));
        Assert.DoesNotContain("ben-selector--soldout", CamoLabel(largeChosen));
        Assert.Matches($"<label for=\"opt-{Size:N}-{Large:N}\"", largeChosen);
    }

    [Fact]
    public void The_choices_name_a_variant_once_complete_and_open_on_one_in_stock()
    {
        Assert.Null(StoreOptionPicker.VariantFor(Options, Variants, new Dictionary<Guid, Guid> { [Colour] = Black }));
        Assert.Equal("B-L", StoreOptionPicker.VariantFor(Options, Variants, new Dictionary<Guid, Guid> { [Colour] = Black, [Size] = Large })!.Sku);

        var start = StoreOptionPicker.StartingChoices(Options, Variants);
        Assert.Equal((Black, Small), (start[Colour], start[Size]));

        var soldOutDefault = Variants.Select(v => v with { IsDefault = v.Sku == "C-S" }).ToArray();
        Assert.Equal(Black, StoreOptionPicker.StartingChoices(Options, soldOutDefault)[Colour]);
    }

    // ── card ─────────────────────────────────────────────────────────────────

    private static StoreProductCard Card(bool inStock, int? left = null) => new(
        Guid.NewGuid(), "P-SB7 Spirit Box", "p-sb7-spirit-box", "Spirit Boxes", "spirit-boxes", Guid.NewGuid(), "front",
        79.99m, 84.99m, null, null, 0m, 0, inStock, left, false, false, 2, null);

    [Fact]
    public async Task Sold_out_card_carries_the_marker()
    {
        var soldOut = await RenderAsync<StoreCard>(new() { [nameof(StoreCard.Card)] = Card(inStock: false) });
        Assert.Contains("ben-card ben-card--soldout", soldOut);
        Assert.Contains(">Sold out<", soldOut);

        var inStock = await RenderAsync<StoreCard>(new() { [nameof(StoreCard.Card)] = Card(inStock: true, left: 2) });
        Assert.DoesNotContain("ben-card--soldout", inStock);
        Assert.Contains("Only 2 left", inStock);
        Assert.Contains("href=\"/store/p/p-sb7-spirit-box\"", inStock);
        Assert.Contains("/thumb", inStock);
        Assert.Contains("$79.99 – $84.99", inStock);
    }

    /// <summary>The rail is hidden only where there is a hover to reveal it.</summary>
    [Fact]
    public void Card_rail_is_reachable_without_hover()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Ben.slnx"))) dir = dir.Parent;
        var css = File.ReadAllText(Path.Combine(dir.FullName, "Ben.Web.Website.Library", "wwwroot", "kit", "ben-store.css"));

        var hoverBlock = Regex.Match(css, @"@media \(hover: hover\) \{(.*?)\n\}", RegexOptions.Singleline);
        Assert.True(hoverBlock.Success, "ben-store.css has no (hover: hover) block for the card rail.");
        Assert.Matches(@"\.ben-card__rail \{[^}]*opacity: 0", hoverBlock.Groups[1].Value);

        var outside = css.Remove(hoverBlock.Index, hoverBlock.Length);
        Assert.DoesNotMatch(@"\.ben-card__rail \{[^}]*(opacity: 0|display: none|visibility: hidden)", outside);
    }

    // ── gallery ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_gallery_shows_the_chosen_variants_own_picture()
    {
        var front = Guid.NewGuid(); var camoPicture = Guid.NewGuid(); var camoVariant = Guid.NewGuid();
        var images = new[]
        {
            new StoreImageRecord(Guid.NewGuid(), front, "front", 0, null, 1200, 900),
            new StoreImageRecord(Guid.NewGuid(), camoPicture, "camo", 1, camoVariant, 1200, 900),
        };

        var plain = await RenderAsync<StoreProductGallery>(new() { [nameof(StoreProductGallery.Images)] = images });
        Assert.Matches($"<img src=\"/media/store-image/{front}\"[^>]*data-testid=\"gallery-main\"", plain);

        var camo = await RenderAsync<StoreProductGallery>(new()
        {
            [nameof(StoreProductGallery.Images)] = images, [nameof(StoreProductGallery.VariantId)] = camoVariant,
        });
        Assert.Matches($"<img src=\"/media/store-image/{camoPicture}\"[^>]*data-testid=\"gallery-main\"", camo);
    }

    // ── filters ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Filters_are_addresses_and_chosen_ones_can_be_taken_off()
    {
        var facets = new StoreFilterFacets(
            [new StorePriceBandFacet("50-100", "$50 – $100", 3)],
            [new StoreOptionFacet("Colour", StoreOptionKind.Swatch, [new StoreFacetValue("Colour:Black", "Black", 3, "#111111")])],
            [new StoreRatingFacet(4, 0)], 5);
        var query = new StoreListingQuery(Q: "box", Options: ["Colour:Black"], Sort: "newest", Page: 3);

        var html = await RenderAsync<StoreFilterForm>(new()
        {
            [nameof(StoreFilterForm.Facets)] = facets, [nameof(StoreFilterForm.Query)] = query,
            [nameof(StoreFilterForm.BaseHref)] = "/store/c/spirit-boxes",
        });

        Assert.Contains("href=\"/store/c/spirit-boxes?q=box&price=50-100&opt=Colour%3ABlack&sort=newest\"", html);
        Assert.Contains("href=\"/store/c/spirit-boxes?q=box&sort=newest\"", html);
        Assert.Matches("<input class=\"form-check-input\" type=\"checkbox\" id=\"filter-colour-black\" checked", html);
        Assert.Contains("No reviews yet.", html);
    }
}
