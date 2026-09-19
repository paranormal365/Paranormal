using Ben.Video.Editor.Models.Assets;
using Ben.Video.Core.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The asset gallery's search box and type picker.
/// </summary>
public sealed class AssetFilterTests
{
    private static VideoAssetCatalogItem Asset(
        string name,
        VideoAssetType type = VideoAssetType.Clipart,
        string? category = null,
        params string[] tags) =>
        new()
        {
            Id       = Guid.NewGuid().ToString(),
            Name     = name,
            Type     = type,
            Category = category,
            Tags     = tags,
        };

    private static readonly VideoAssetCatalogItem[] Catalogue =
    [
        Asset("Speech Bubble", VideoAssetType.Callout, "Annotations", "talk", "arrow"),
        Asset("Red Circle",    VideoAssetType.Shape,   "Basics",      "round"),
        Asset("Ghost",         VideoAssetType.Clipart, "Halloween",   "spooky", "sheet"),
    ];

    [Fact]
    public void No_search_and_no_type_returns_everything() =>
        Assert.Equal(3, AssetFilter.Apply(Catalogue, null, null).Count);

    /// <summary>
    /// Clearing the box has to restore the gallery, not empty it — the difference between a search
    /// people will use twice and one they will not.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_search_matches_everything(string search) =>
        Assert.Equal(3, AssetFilter.Apply(Catalogue, search, null).Count);

    [Fact]
    public void A_name_search_finds_the_asset() =>
        Assert.Equal("Ghost", Assert.Single(AssetFilter.Apply(Catalogue, "ghos", null)).Name);

    [Fact]
    public void Case_does_not_matter() =>
        Assert.Single(AssetFilter.Apply(Catalogue, "GHOST", null));

    /// <summary>
    /// Tags carry an asset's useful words: "arrow" is a tag on half the callouts and the name of
    /// almost none of them.
    /// </summary>
    [Fact]
    public void A_tag_search_finds_the_asset() =>
        Assert.Equal("Speech Bubble", Assert.Single(AssetFilter.Apply(Catalogue, "arrow", null)).Name);

    [Fact]
    public void A_category_search_finds_the_asset() =>
        Assert.Equal("Ghost", Assert.Single(AssetFilter.Apply(Catalogue, "hallow", null)).Name);

    [Fact]
    public void A_type_narrows_to_that_type() =>
        Assert.Equal(
            "Red Circle",
            Assert.Single(AssetFilter.Apply(Catalogue, null, VideoAssetType.Shape)).Name);

    /// <summary>
    /// The two narrow together rather than either one winning.
    /// </summary>
    [Fact]
    public void A_search_that_matches_another_type_finds_nothing()
    {
        Assert.Empty(AssetFilter.Apply(Catalogue, "ghost", VideoAssetType.Shape));
        Assert.Single(AssetFilter.Apply(Catalogue, "ghost", VideoAssetType.Clipart));
    }

    [Fact]
    public void A_search_that_matches_nothing_returns_nothing() =>
        Assert.Empty(AssetFilter.Apply(Catalogue, "poltergeist", null));

    /// <summary>
    /// The gallery asks before its first load has finished.
    /// </summary>
    [Fact]
    public void An_empty_catalogue_is_not_a_crash()
    {
        Assert.Empty(AssetFilter.Apply(null, "ghost", VideoAssetType.Shape));
        Assert.Empty(AssetFilter.Apply([], "ghost", null));
    }

    /// <summary>
    /// Whitespace around a pasted term is not part of the term.
    /// </summary>
    [Fact]
    public void A_search_is_trimmed() =>
        Assert.Single(AssetFilter.Apply(Catalogue, "  ghost  ", null));
}
