using Ben.Data.Common.Enums;
using Ben.Web.Services.Help;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Help written for a site role (front matter <c>role:</c>, store sellers, backlog 251) reaches
/// that role — which the audience ladder alone cannot say: a seller is not above a group member.
/// </summary>
public sealed class HelpRoleVisibilityTests
{
    private static readonly HelpDocument ForSellers = new("selling", "Selling", "", "Getting Started", HelpAudience.SignedIn, 1, "", Role: "Seller");

    [Fact]
    public void A_role_document_is_hidden_from_somebody_on_the_right_rung_without_the_role()
    {
        Assert.False(new HelpViewer(HelpAudience.OrganizationAdministrator).CanSee(ForSellers));
        Assert.False(new HelpViewer(HelpAudience.SignedIn, ["Moderator"]).CanSee(ForSellers));
    }

    [Fact]
    public void It_shows_to_the_role_and_to_an_app_administrator()
    {
        Assert.True(new HelpViewer(HelpAudience.SignedIn, ["seller"]).CanSee(ForSellers));   // either case
        Assert.True(new HelpViewer(HelpAudience.AppAdministrator).CanSee(ForSellers));
    }

    [Fact]
    public void The_role_does_not_lower_the_floor()
    {
        Assert.False(HelpViewer.Anonymous.CanSee(ForSellers));
        Assert.False(new HelpViewer(HelpAudience.Everyone, ["Seller"]).CanSee(ForSellers));
    }

    [Fact]
    public void The_sellers_document_names_its_role_and_is_found_only_by_a_seller()
    {
        var service = new HelpContentService();
        var doc = HelpContentService.LoadAll().Single(d => d.Slug == "selling-in-the-store");

        Assert.Equal("Seller", doc.Role);
        Assert.Null(service.Find(doc.Slug, new HelpViewer(HelpAudience.OrganizationAdministrator)));
        Assert.NotNull(service.Find(doc.Slug, new HelpViewer(HelpAudience.SignedIn, ["Seller"])));
    }
}
