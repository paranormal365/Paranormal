using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The place search behind "Where (a place)" on a public calendar event (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Found on production: typing "Bell Witch" found nothing, because the box asked the
/// duplicate-place matcher, which wants the exact name. These tests pin the search that replaced
/// it: a part of the name finds the place, a private home never comes back, and nobody signed out
/// can ask.</para>
///
/// <para>The seeded cave is the production row, field for field, as the public place page
/// reports it.</para>
/// </remarks>
public class PlaceSearchTests
{
    private static readonly Guid MeId = Guid.NewGuid();

    private static PlaceController Build(IDbContextFactory<BenDataContext> f, bool signedIn = true)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = signedIn
                        ? new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, MeId.ToString())], "Bearer"))
                        : new ClaimsPrincipal(new ClaimsIdentity()),
                }
            }
        };

    private static Place BellWitchCave() => new()
    {
        Id = Guid.Parse("b27248e7-8504-4329-ad2d-51dba75e8384"),
        Name = "Bell Witch Cave",
        StreetAddress1 = "430 Keysburg Rd",
        City = "Adams",
        State = "TN",
        ZipCode = "37010",
        Country = "US",
        Latitude = 36.5806m,
        Longitude = -87.0644m,
        Kind = PlaceKind.PublicLocation,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = MeId,
    };

    private static Place Place(string name, string city = "Nashville", PlaceKind kind = PlaceKind.PublicLocation)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            StreetAddress1 = "1 Main St",
            City = city,
            State = "TN",
            Kind = kind,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = MeId,
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(params Place[] places)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        db.Places.AddRange(places);
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<IReadOnlyList<PlaceCandidate>> SearchAsync(
        IDbContextFactory<BenDataContext> f, string? q)
    {
        var result = await Build(f).SearchPublicLocations(q, default);
        var value = Assert.IsType<OkObjectResult>(result.Result).Value;
        return Assert.IsAssignableFrom<IEnumerable<PlaceCandidate>>(value).ToList();
    }

    // ── Finding the place ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bell Witch")]
    [InlineData("bell witch")]
    [InlineData("Witch")]
    [InlineData("Bell Witch Cave")]
    [InlineData("  BELL   WITCH  ")]
    public async Task Part_of_the_name_finds_Bell_Witch_Cave(string typed)
    {
        var f = await SeedAsync(BellWitchCave());

        var found = Assert.Single(await SearchAsync(f, typed));

        Assert.Equal("Bell Witch Cave", found.Name);
        Assert.Equal(PlaceKind.PublicLocation, found.Kind);
    }

    [Theory]
    [InlineData("Adams")]
    [InlineData("Bell Witch Adams")]
    public async Task The_town_counts_too(string typed)
    {
        var f = await SeedAsync(BellWitchCave(), Place("Belmont Mansion"));

        Assert.Equal("Bell Witch Cave", Assert.Single(await SearchAsync(f, typed)).Name);
    }

    [Fact]
    public async Task Every_word_has_to_appear()
    {
        var f = await SeedAsync(BellWitchCave(), Place("Bell Tower"));

        Assert.Equal("Bell Witch Cave", Assert.Single(await SearchAsync(f, "Bell Cave")).Name);
    }

    /// <summary>
    /// The duplicate matcher is unchanged: a part of a name still finds nothing there. That is the
    /// rule that broke the event form, and it stays, because the screens that stop duplicate places
    /// from being created depend on it being strict.
    /// </summary>
    [Fact]
    public async Task The_duplicate_matcher_still_needs_the_whole_name()
    {
        var f = await SeedAsync(BellWitchCave());

        var partial = await Build(f).FindCandidates(null, null, null, null, "Bell Witch", null, null, default);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<PlaceCandidate>>(
            Assert.IsType<OkObjectResult>(partial.Result).Value));

        // The whole name, sent exactly as the event form sent it (no street, town or position),
        // does match. So on 10/16 the full-name search returning "No places found" was not the
        // matcher's rule: it was a request that failed and was shown as an empty answer.
        var whole = await Build(f).FindCandidates(null, null, null, null, "Bell Witch Cave", null, null, default);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<PlaceCandidate>>(
            Assert.IsType<OkObjectResult>(whole.Result).Value));
    }

    // ── What may come back ────────────────────────────────────────────────────

    [Fact]
    public async Task A_private_residence_is_never_returned()
    {
        var f = await SeedAsync(
            BellWitchCave(),
            Place("Bell Witch Farmhouse", city: "Adams", kind: PlaceKind.PrivateResidence));

        // Typed by name, by town, and by both: the home never appears. A public event can't be
        // held there, and listing it would hand its street to anybody signed in.
        foreach (var typed in new[] { "Bell Witch", "Farmhouse", "Adams", "Bell Witch Farmhouse" })
            Assert.DoesNotContain(await SearchAsync(f, typed), p => p.Kind == PlaceKind.PrivateResidence);

        Assert.Empty(await SearchAsync(f, "Farmhouse"));
    }

    /// <summary>
    /// The same search serves a hosted event's venue box, so it has to return exactly what
    /// <c>HostedEventController.ValidateAsync</c> accepts: any place that isn't a private
    /// residence. Written over every kind there is, so a new kind added later is decided here
    /// rather than slipping through.
    /// </summary>
    [Fact]
    public async Task It_returns_every_kind_of_place_a_venue_may_be_and_no_other()
    {
        var f = await SeedAsync(Enum.GetValues<PlaceKind>()
            .Select(k => Place($"Riverside {k}", kind: k))
            .ToArray());

        var kinds = (await SearchAsync(f, "Riverside")).Select(p => p.Kind).OrderBy(k => k);

        Assert.Equal(
            Enum.GetValues<PlaceKind>().Where(k => k != PlaceKind.PrivateResidence).OrderBy(k => k),
            kinds);
    }

    [Fact]
    public async Task The_answer_is_capped()
    {
        var f = await SeedAsync(Enumerable.Range(1, 30)
            .Select(i => Place($"Old Mill {i:D2}"))
            .ToArray());

        Assert.Equal(PlaceController.SearchResultCap, (await SearchAsync(f, "Mill")).Count);
    }

    [Fact]
    public async Task A_name_that_starts_with_the_words_comes_first()
    {
        // "Adams Bell Witch Tour" sorts first by alphabet, so the cave leading proves the order.
        var f = await SeedAsync(Place("Adams Bell Witch Tour"), BellWitchCave());

        var found = await SearchAsync(f, "Bell Witch");

        Assert.Equal(["Bell Witch Cave", "Adams Bell Witch Tour"], found.Select(p => p.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("B")]
    public async Task Too_little_to_go_on_returns_nothing(string? typed)
    {
        var f = await SeedAsync(BellWitchCave());

        Assert.Empty(await SearchAsync(f, typed));
    }

    [Theory]
    [InlineData("%%")]
    [InlineData("__")]
    public async Task Wildcards_are_taken_literally(string typed)
    {
        var f = await SeedAsync(BellWitchCave());

        // Unescaped, "%%" would match every public place there is.
        Assert.Empty(await SearchAsync(f, typed));
    }

    [Fact]
    public async Task Each_place_carries_its_investigation_count()
    {
        var cave = BellWitchCave();
        var f = await SeedAsync(cave);
        await using (var db = await f.CreateDbContextAsync())
        {
            var orgId = Guid.NewGuid();
            db.Organizations.Add(new Organization
            { Id = orgId, Name = "Mine", UrlName = "mine", DateCreated = DateTime.UtcNow });
            db.Investigations.Add(new Investigation
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, PlaceId = cave.Id,
                Title = "Autumn survey", ScheduledDateTime = DateTime.UtcNow.AddDays(-20),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MeId,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, Assert.Single(await SearchAsync(f, "Bell Witch")).InvestigationCount);
    }

    // ── Who may ask ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_caller_who_is_not_signed_in_is_refused()
    {
        var f = await SeedAsync(BellWitchCave());

        var result = await Build(f, signedIn: false).SearchPublicLocations("Bell Witch", default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void The_endpoint_sits_behind_sign_in()
    {
        // The pipeline refuses a request with no token before the action runs; the check above
        // covers a token that names nobody. Both have to hold.
        Assert.NotNull(typeof(PlaceController).GetCustomAttribute<AuthorizeAttribute>(inherit: true));

        var action = typeof(PlaceController).GetMethod(nameof(PlaceController.SearchPublicLocations))!;
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("search", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
