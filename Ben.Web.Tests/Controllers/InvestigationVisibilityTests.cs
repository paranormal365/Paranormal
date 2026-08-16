using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Who may see another organization's investigation at a shared place.
/// </summary>
/// <remarks>
/// <para>Everything here runs through one predicate on purpose. Sharing rules spread across several
/// queries drift, and the way that is discovered is that something private appears somewhere it
/// should not. These tests describe the rules once, against the one function that implements them.</para>
///
/// <para>The decision worth pinning down: <c>PlaceInvestigators</c> is <b>not reciprocal</b>. You
/// qualify by having investigated the place, not by having shared anything of your own.</para>
/// </remarks>
public class InvestigationVisibilityTests
{
    private static readonly Guid MineOrgId = Guid.NewGuid();
    private static readonly Guid TheirOrgId = Guid.NewGuid();
    private static readonly Guid MeId = Guid.NewGuid();

    private static PlaceController Build(IDbContextFactory<BenDataContext> f, Guid? asUser = null)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, (asUser ?? MeId).ToString())], "Bearer"))
                }
            }
        };

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid PlaceId);

    /// <summary>
    /// One shared place, my organization and theirs, and me as a member of mine only.
    /// </summary>
    private static async Task<World> SeedAsync(PlaceKind kind = PlaceKind.PublicLocation)
    {
        var factory = TestDbFactory.Create();
        var placeId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[] { (MineOrgId, "Mine"), (TheirOrgId, "Theirs") })
            db.Organizations.Add(new Organization
            { Id = id, Name = name, UrlName = name.ToLowerInvariant(), DateCreated = DateTime.UtcNow });

        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = MineOrgId, AppUserId = MeId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
        });

        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Shared Place", Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = MeId,
        });

        await db.SaveChangesAsync();
        return new World(factory, placeId);
    }

    private static async Task AddInvestigationAsync(
        World w, Guid orgId, InvestigationVisibility visibility, string title, Guid? placeId = null)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        db.Investigations.Add(new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, PlaceId = placeId ?? w.PlaceId,
            Title = title, Visibility = visibility,
            ScheduledDateTime = DateTime.UtcNow.AddDays(-10),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<PlaceInvestigationRow>> VisibleAsync(World w, Guid? asUser = null)
    {
        var result = await Build(w.Factory, asUser).GetInvestigations(w.PlaceId, default);
        return Assert.IsAssignableFrom<IEnumerable<PlaceInvestigationRow>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
    }

    // ── The three scopes ──────────────────────────────────────────────────────

    [Fact]
    public async Task Another_groups_group_only_investigation_is_invisible()
    {
        var w = await SeedAsync();
        await AddInvestigationAsync(w, TheirOrgId, InvestigationVisibility.GroupOnly, "Theirs, private");
        // I qualify as a place investigator — and it still must not show.
        await AddInvestigationAsync(w, MineOrgId, InvestigationVisibility.GroupOnly, "Mine");

        var visible = await VisibleAsync(w);

        Assert.Equal("Mine", Assert.Single(visible).Title);
    }

    [Fact]
    public async Task My_own_groups_investigation_is_always_visible_whatever_its_scope()
    {
        var w = await SeedAsync();
        await AddInvestigationAsync(w, MineOrgId, InvestigationVisibility.GroupOnly, "Mine");

        // Nothing about sharing restricts an organization's view of its own work.
        Assert.Single(await VisibleAsync(w));
    }

    [Fact]
    public async Task A_public_investigation_is_visible_to_anyone_signed_in()
    {
        var w = await SeedAsync();
        await AddInvestigationAsync(w, TheirOrgId, InvestigationVisibility.Public, "Theirs, public");

        // A stranger in no organization at all still sees it.
        Assert.Single(await VisibleAsync(w, asUser: Guid.NewGuid()));
    }

    [Fact]
    public async Task Place_investigators_scope_needs_me_to_have_investigated_the_place()
    {
        var w = await SeedAsync();
        await AddInvestigationAsync(w, TheirOrgId, InvestigationVisibility.PlaceInvestigators, "Theirs, shared");

        // My organization has never been here, so I do not qualify yet.
        Assert.Empty(await VisibleAsync(w));

        await AddInvestigationAsync(w, MineOrgId, InvestigationVisibility.GroupOnly, "Mine");

        var visible = await VisibleAsync(w);
        Assert.Equal(2, visible.Count);
        Assert.Contains(visible, v => v.Title == "Theirs, shared");
    }

    [Fact]
    public async Task Qualifying_does_not_require_sharing_anything_of_my_own()
    {
        var w = await SeedAsync();
        await AddInvestigationAsync(w, TheirOrgId, InvestigationVisibility.PlaceInvestigators, "Theirs, shared");
        // Mine stays group-only: I contribute nothing to the pool and still read from it.
        await AddInvestigationAsync(w, MineOrgId, InvestigationVisibility.GroupOnly, "Mine, private");

        var visible = await VisibleAsync(w);

        // The not-reciprocal decision, stated as a test so that reversing it is a deliberate act
        // rather than something that quietly happens.
        Assert.Contains(visible, v => v.Title == "Theirs, shared");
    }

    [Fact]
    public async Task Having_investigated_a_different_place_does_not_qualify_me_here()
    {
        var w = await SeedAsync();
        var elsewhere = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.Places.Add(new Place
            { Id = elsewhere, Name = "Somewhere else", DateCreated = DateTime.UtcNow, CreatedByAppUserId = MeId });
            await db.SaveChangesAsync();
        }

        await AddInvestigationAsync(w, TheirOrgId, InvestigationVisibility.PlaceInvestigators, "Theirs, shared");
        await AddInvestigationAsync(w, MineOrgId, InvestigationVisibility.GroupOnly, "Mine, elsewhere", placeId: elsewhere);

        // The qualification is per place, not "has investigated anything anywhere".
        Assert.Empty(await VisibleAsync(w));
    }

    [Fact]
    public async Task The_row_says_whether_it_is_my_own_groups_work()
    {
        var w = await SeedAsync();
        await AddInvestigationAsync(w, MineOrgId, InvestigationVisibility.GroupOnly, "Mine");
        await AddInvestigationAsync(w, TheirOrgId, InvestigationVisibility.Public, "Theirs");

        var visible = await VisibleAsync(w);

        // So the page can separate "our visits" from "what others have shared" without guessing.
        Assert.True(visible.Single(v => v.Title == "Mine").IsMine);
        Assert.False(visible.Single(v => v.Title == "Theirs").IsMine);
    }

    // ── Defaults and refusals ─────────────────────────────────────────────────

    [Fact]
    public void A_landmark_defaults_to_sharing_with_fellow_investigators()
    {
        var place = new Place { Kind = PlaceKind.PublicLocation };

        Assert.Equal(InvestigationVisibility.PlaceInvestigators,
            InvestigationVisibilityFilter.DefaultFor(place));
    }

    [Fact]
    public void A_home_and_an_unknown_place_both_default_to_the_group_alone()
    {
        Assert.Equal(InvestigationVisibility.GroupOnly,
            InvestigationVisibilityFilter.DefaultFor(new Place { Kind = PlaceKind.PrivateResidence }));

        // No place yet is treated as cautiously as a home, rather than falling through to something
        // wider by accident.
        Assert.Equal(InvestigationVisibility.GroupOnly,
            InvestigationVisibilityFilter.DefaultFor(null));
    }

    [Fact]
    public void Publishing_an_investigation_at_somebodys_home_is_refused()
    {
        var home = new Place { Kind = PlaceKind.PrivateResidence };

        var rejection = InvestigationVisibilityFilter.Reject(InvestigationVisibility.Public, home);

        // There is no way to ask the client's permission yet, so the option is withheld rather
        // than offered without the consent behind it.
        Assert.False(string.IsNullOrWhiteSpace(rejection));
    }

    [Fact]
    public void Publishing_an_investigation_at_a_landmark_is_allowed()
    {
        Assert.Null(InvestigationVisibilityFilter.Reject(
            InvestigationVisibility.Public, new Place { Kind = PlaceKind.PublicLocation }));
    }

    [Fact]
    public void Sharing_with_place_investigators_needs_a_place()
    {
        // Without one the audience has no members, so the setting would silently behave as
        // group-only — worse than refusing it.
        Assert.False(string.IsNullOrWhiteSpace(
            InvestigationVisibilityFilter.Reject(InvestigationVisibility.PlaceInvestigators, null)));
    }

    [Fact]
    public void Group_only_is_allowed_everywhere()
    {
        Assert.Null(InvestigationVisibilityFilter.Reject(InvestigationVisibility.GroupOnly, null));
        Assert.Null(InvestigationVisibilityFilter.Reject(
            InvestigationVisibility.GroupOnly, new Place { Kind = PlaceKind.PrivateResidence }));
    }
}
