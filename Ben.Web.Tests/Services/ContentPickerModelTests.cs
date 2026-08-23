using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 175: the content picker's filtering, pinned where xUnit can hold it — the component
/// only renders what this model answers.
/// </summary>
public sealed class ContentPickerModelTests
{
    private static ContentPickerItem Item(string name, string type, string? meta = null,
        params (string Facet, string Value)[] facets)
        => new(Guid.NewGuid(), name, type, meta,
               facets.Length == 0 ? null : facets.ToDictionary(f => f.Facet, f => f.Value));

    private static ContentPickerModel Loaded(params ContentPickerItem[] items)
    {
        var model = new ContentPickerModel();
        model.SetItems(items);
        return model;
    }

    [Fact]
    public void Search_matches_name_type_and_meta_case_insensitively()
    {
        var model = Loaded(
            Item("ghost-orb.png", "Image", "Sarah Mitchell · 2 MB"),
            Item("evp-session.wav", "Audio", "James Thornton · 5 MB"));

        model.Search = "ORB";
        Assert.Equal("ghost-orb.png", Assert.Single(model.Visible()).Name);

        model.Search = "audio";
        Assert.Equal("evp-session.wav", Assert.Single(model.Visible()).Name);

        model.Search = "thornton";
        Assert.Equal("evp-session.wav", Assert.Single(model.Visible()).Name);

        model.Search = "poltergeist";
        Assert.Empty(model.Visible());
    }

    [Fact]
    public void Type_and_facet_filters_must_all_agree()
    {
        var model = Loaded(
            Item("a.png", "Image", null, ("Source", "Public")),
            Item("b.png", "Image", null, ("Source", "Shared with this group")),
            Item("c.wav", "Audio", null, ("Source", "Shared with this group")));

        model.TypeFilter = "Image";
        Assert.Equal(2, model.Visible().Count);

        model.FacetFilters["Source"] = "Shared with this group";
        Assert.Equal("b.png", Assert.Single(model.Visible()).Name);

        model.TypeFilter = null;
        Assert.Equal(2, model.Visible().Count);   // b + c share the facet value
    }

    [Fact]
    public void Facet_dropdowns_are_built_from_what_actually_arrived()
    {
        var model = Loaded(
            Item("a.png", "Image", null, ("Source", "Public"), ("Investigation", "Oct 12 visit")),
            Item("b.png", "Image", null, ("Source", "Shared with this group")));

        Assert.Equal(["Investigation", "Source"], model.FacetNames);
        Assert.Equal(["Public", "Shared with this group"], model.FacetValues("Source"));
        Assert.Equal(["Oct 12 visit"], model.FacetValues("Investigation"));
    }

    [Fact]
    public void A_reload_drops_filters_that_no_longer_point_at_anything()
    {
        var model = Loaded(Item("a.png", "Image"), Item("b.wav", "Audio", null, ("Source", "Public")));
        model.TypeFilter = "Audio";
        model.FacetFilters["Source"] = "Public";

        // The next load has neither audio nor the facet: stale filters would show NOTHING and
        // read as an empty library.
        model.SetItems([Item("c.png", "Image")]);

        Assert.Null(model.TypeFilter);
        Assert.Empty(model.FacetFilters);
        Assert.Single(model.Visible());
    }
}
