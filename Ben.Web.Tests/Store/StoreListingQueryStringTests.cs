using Ben.Service.Models.Store;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>A listing's filters survive the address bar both ways, and nonsense in it is dropped (storefront S2.1).</summary>
public sealed class StoreListingQueryStringTests
{
    [Fact]
    public void Every_filter_round_trips()
    {
        var query = new StoreListingQuery("spirit box & more", "50-100", 10m, 90.5m, ["Colour:Black", "Size:Large Plus"],
            4, InStock: true, "price-asc", 3);

        var written = StoreListingQueryString.From(query);
        var read = StoreListingQueryString.Parse(written);

        Assert.Equal("?q=spirit%20box%20%26%20more&price=50-100&min=10&max=90.5&opt=Colour%3ABlack&opt=Size%3ALarge%20Plus&rating=4&instock=1&sort=price-asc&page=3", written);
        Assert.Equal(query with { Options = null }, read with { Options = null });
        Assert.Equal(query.Options, read.Options);
    }

    [Fact]
    public void Defaults_are_never_written_so_one_listing_has_one_address()
    {
        Assert.Equal("", StoreListingQueryString.From(new StoreListingQuery()));
        Assert.Equal("", StoreListingQueryString.From(new StoreListingQuery(Sort: "popular", Page: 1)));
        Assert.Equal("?sort=newest", StoreListingQueryString.From(new StoreListingQuery(Sort: "newest", Page: 4), includePage: false));
    }

    [Theory]
    [InlineData("?price=cheap", null)]
    [InlineData("?price=UNDER-25", "under-25")]
    public void An_unknown_band_is_dropped(string raw, string? band)
        => Assert.Equal(band, StoreListingQueryString.Parse(raw).PriceBand);

    [Fact]
    public void Nonsense_is_dropped_rather_than_refused()
    {
        var q = StoreListingQueryString.Parse("rating=9&page=0&min=-5&max=abc&sort=cheapest-first&opt=nocolon&opt=:x&opt=Colour:&q=%20%20");

        Assert.Equal(new StoreListingQuery(Options: [], Sort: "popular", Page: 1) with { Options = null }, q with { Options = null });
        Assert.Empty(q.Options!);
    }

    [Fact]
    public void A_backwards_price_range_is_turned_round_and_repeats_are_dropped()
    {
        var q = StoreListingQueryString.Parse("min=100&max=20&opt=Colour:Black&opt=colour:black");
        Assert.Equal((20m, 100m), (q.PriceMin, q.PriceMax));
        Assert.Single(q.Options!);
    }

    [Fact]
    public void Removing_one_chosen_value_goes_back_to_the_first_page()
    {
        var q = new StoreListingQuery(Options: ["Colour:Black", "Size:Large"], Page: 3);
        Assert.Equal(new StoreListingQuery(Options: ["Size:Large"], Page: 1) with { Options = null },
            StoreListingQueryString.Without(q, "colour:black") with { Options = null });
        Assert.Equal(["Size:Large"], StoreListingQueryString.Without(q, "colour:black").Options);
    }

    /// <summary>A plain HTML search form writes spaces as "+".</summary>
    [Fact]
    public void A_search_typed_into_a_form_reads_its_spaces()
        => Assert.Equal("spirit box", StoreListingQueryString.Parse("?q=spirit+box").Q);
}
