using Ben.Data.Common.Enums;
using Ben.Service.Models.Store;
using Ben.Web.Services;
using Ben.Web.Website.Library.Store.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The product editor's live preview (Ben, 09/24/2026): the product page drawn from the unsaved
/// form, by the rules the public page uses, through the same component.
/// </summary>
public sealed class StoreProductPreviewTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Colour = Guid.NewGuid(), Black = Guid.NewGuid(), Camo = Guid.NewGuid(), Retired = Guid.NewGuid();
    private static readonly Guid BlackVariant = Guid.NewGuid(), CamoVariant = Guid.NewGuid();

    private static StoreProductAdminRecord Saved(bool active = false) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Spirit boxes", true, null, "P-SB7", "p-sb7-spirit-box", "Saved short", "<p>Saved long</p>",
        active, false, null, null, 0, 0, 0, 4.5m, 2, 0, false,
        [new StoreImageRecord(Guid.NewGuid(), Guid.NewGuid(), "front", 0, null, 1200, 900)],
        [new StoreOptionRecord(Colour, "Colour", StoreOptionKind.Swatch,
            [new(Black, "Black", "#111111", 0, true), new(Camo, "Camo", "#5b6b3a", 1, true), new(Retired, "Blue", "#0000ff", 2, false)])],
        [
            new StoreVariantAdminRecord(BlackVariant, "SB7-BLK", "Black", 79.99m, 89.99m, 8, 1, 0, true, true, 0, [Black], false),
            new StoreVariantAdminRecord(CamoVariant, "SB7-CAMO", "Camo", 84.99m, null, 0, 0, 0, true, false, 1, [Camo], false),
        ],
        [], "/store/p/p-sb7-spirit-box", Now.AddDays(-3), null);

    private static StoreProductPreviewContext Context => new("Spirit boxes", "spirit-boxes", null, 3, 30, Now);

    private static StoreProductPreviewDraft Draft(
        string name = "P-SB7 Spirit Box", DateTime? newUntil = null, IReadOnlyDictionary<Guid, StoreVariantPreviewDraft>? variants = null)
        => new(name, "  Sweeps FM and AM.  ", "<p>Typed, not saved</p>", true, newUntil,
            [new StoreSpecGroup("Radio", [new StoreSpecRecord("Sweep", "50–350 ms")]), new StoreSpecGroup("Empty", [])], variants);

    [Fact]
    public void The_preview_shows_what_is_typed_not_what_is_saved()
    {
        var page = StoreProductPreview.Build(Saved(), Draft(), Context);

        Assert.Equal("P-SB7 Spirit Box", page.Name);
        Assert.Equal("Sweeps FM and AM.", page.ShortDescription);
        Assert.Equal("<p>Typed, not saved</p>", page.LongDescriptionHtml);
        Assert.True(page.IsFeatured);
        Assert.True(page.IsPreview);
        Assert.Equal("Radio", Assert.Single(page.Specs).GroupName);   // a heading with no lines is not drawn
        Assert.Equal(7, page.Variants.Single(v => v.Id == BlackVariant).Available);   // on hand less held
    }

    [Fact]
    public void Unsaved_prices_and_switches_are_drawn_by_the_public_pages_rules()
    {
        var page = StoreProductPreview.Build(Saved(), Draft(variants: new Dictionary<Guid, StoreVariantPreviewDraft>
        {
            [BlackVariant] = new(69.99m, 59.99m, true),   // an "old price" below the price is not one
            [CamoVariant] = new(84.99m, null, false),      // switched off, not saved yet
        }), Context);

        var only = Assert.Single(page.Variants);
        Assert.Equal((69.99m, (decimal?)null), (only.Price, only.CompareAtPrice));
        // Camo's only variant is off and Blue is retired: Black is the one choice left.
        Assert.Equal([Black], Assert.Single(page.Options).Values.Select(v => v.Id));
    }

    [Fact]
    public void New_lasts_to_the_end_of_the_chosen_day_and_a_blank_name_keeps_the_saved_one()
    {
        Assert.True(StoreProductPreview.Build(Saved(), Draft(newUntil: Now.Date), Context).IsNew);
        Assert.False(StoreProductPreview.Build(Saved(), Draft(newUntil: Now.Date.AddDays(-1)), Context).IsNew);
        Assert.Equal("P-SB7", StoreProductPreview.Build(Saved(), Draft(name: "  "), Context).Name);
    }

    [Fact]
    public async Task The_store_component_draws_the_draft_and_the_editors_banner()
    {
        var page = StoreProductPreview.Build(Saved(), Draft(), Context);
        RenderFragment banner = b => b.AddMarkupContent(0, "<div id=\"editor-note\">Editor's note</div>");

        var html = await StoreComponentRenderTests.RenderAsync<StoreProductView>(new()
        {
            [nameof(StoreProductView.Product)] = page,
            [nameof(StoreProductView.Banner)] = banner,
        }, Services);

        Assert.Contains("P-SB7 Spirit Box", html);
        Assert.Contains("Typed, not saved", html);
        Assert.Contains("Not on sale", html);
        Assert.Contains("editor-note", html);
        Assert.DoesNotContain("data-testid=\"store-preview\"", html);   // the editor's note replaces the shop's
    }

    [Fact]
    public async Task Without_a_banner_the_shops_preview_warning_is_drawn()
    {
        var html = await StoreComponentRenderTests.RenderAsync<StoreProductView>(new()
        {
            [nameof(StoreProductView.Product)] = StoreProductPreview.Build(Saved(), Draft(), Context),
        }, Services);

        Assert.Contains("data-testid=\"store-preview\"", html);
    }

    private static void Services(IServiceCollection services)
    {
        var urls = new Mock<IMediaUrlBuilder>();
        urls.Setup(u => u.StoreImage(It.IsAny<Guid>(), It.IsAny<bool>())).Returns((Guid id, bool _) => $"/media/store-image/{id}");
        services.AddSingleton(urls.Object);
        services.AddSingleton<NavigationManager, FakeNav>();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);
        var viewer = new Mock<IBenUserState>();
        viewer.Setup(v => v.BrowserTimeZone).Returns(TimeZoneInfo.Utc);
        services.AddSingleton(viewer.Object);
        services.AddSingleton(new Mock<Microsoft.JSInterop.IJSRuntime>().Object);
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/admin/store/products");
    }
}
