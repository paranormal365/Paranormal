using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// Deciding about files people added to a public place (10/09/2026).
/// </summary>
/// <remarks>
/// Ben added a video to Cragfont, was told somebody would look at it, and found nowhere to look: anything the
/// screener held stayed held for good. SuperAdmins and moderators decide for every place, and the owners and
/// administrators of a place's confirmed venue decide for that place.
/// </remarks>
public sealed class PlaceFileReviewTests
{
    private static readonly Guid Contributor = Guid.NewGuid();
    private static readonly Guid VenueAdmin = Guid.NewGuid();
    private static readonly Guid VenueMember = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly Guid Moderator = Guid.NewGuid();
    private static readonly Guid FileTypeId = new("70000000-0000-0000-0000-000000000001");

    private sealed record World(SqliteTestDb Db, Guid Cragfont, Guid Elsewhere, Guid AtCragfont, Guid AtElsewhere);

    private static async Task<World> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[] { (Contributor, "A visitor"), (VenueAdmin, "Venue admin"), (VenueMember, "Venue member"), (Stranger, "Stranger"), (Moderator, "Moderator") })
            db.AppUsers.Add(new AppUser { Id = id, DisplayName = name, Handle = name.Replace(" ", "").ToLowerInvariant(), DateCreated = now });

        var cragfont = Guid.NewGuid(); var elsewhere = Guid.NewGuid();
        db.Places.Add(new Place { Id = cragfont, Name = "Cragfont", Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = Contributor });
        db.Places.Add(new Place { Id = elsewhere, Name = "Elsewhere", Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = Contributor });

        var venueOrg = Guid.NewGuid();
        db.Organizations.Add(new Organization { Id = venueOrg, Name = "Cragfont House", UrlName = "cragfont-house", DateCreated = now, CreatedByAppUserId = VenueAdmin });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        { Id = Guid.NewGuid(), OrganizationId = venueOrg, AppUserId = VenueAdmin, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = now, CreatedByAppUserId = VenueAdmin });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        { Id = Guid.NewGuid(), OrganizationId = venueOrg, AppUserId = VenueMember, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = now, CreatedByAppUserId = VenueAdmin });
        db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
        { Id = Guid.NewGuid(), OrganizationId = venueOrg, PlaceId = cragfont, VerifiedUtc = now.AddDays(-3), DateCreated = now, CreatedByAppUserId = VenueAdmin });

        db.UploadFileTypes.Add(new UploadFileType { Id = FileTypeId, Name = "Feed media", DateCreated = now, CreatedByAppUserId = Contributor });

        Guid File(Guid placeId)
        {
            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = FileTypeId, AppUserId = Contributor,
                FileName = "hallway.mp4", StoredFileName = $"{fileId}.mp4", ContentType = "video/mp4",
                FileSize = 10, StoragePath = $"x/{fileId}.mp4", DateCreated = now, CreatedByAppUserId = Contributor,
            });
            var id = Guid.NewGuid();
            db.PlaceEvidence.Add(new PlaceEvidence
            {
                Id = id, PlaceId = placeId, UploadFileId = fileId, AddedByAppUserId = Contributor,
                Caption = "The upstairs hallway", ReviewState = FeedMediaReviewState.Pending,
                DateCreated = now, CreatedByAppUserId = Contributor,
            });
            return id;
        }

        var atCragfont = File(cragfont);
        var atElsewhere = File(elsewhere);
        await db.SaveChangesAsync();
        return new World(sqlite, cragfont, elsewhere, atCragfont, atElsewhere);
    }

    private static PlaceFileReviewController As(World w, Guid userId, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new PlaceFileReviewController(w.Db.Factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) },
            },
        };
    }

    private static async Task<List<PlaceFileReviewRow>> ListAsync(PlaceFileReviewController c)
        => [.. Assert.IsAssignableFrom<IEnumerable<PlaceFileReviewRow>>(
               Assert.IsType<OkObjectResult>((await c.List(includeHeld: true, placeId: null, default)).Result).Value)];

    [Fact]
    public async Task A_SuperAdmin_and_a_moderator_see_every_place_waiting()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;

        Assert.Equal(2, (await ListAsync(As(w, Moderator, RoleNames.SuperAdmin))).Count);
        Assert.Equal(2, (await ListAsync(As(w, Moderator, RoleNames.Moderator))).Count);
    }

    [Fact]
    public async Task The_confirmed_venues_administrator_sees_only_their_place()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;

        var rows = await ListAsync(As(w, VenueAdmin));
        var row = Assert.Single(rows);
        Assert.Equal(w.Cragfont, row.PlaceId);
        Assert.Equal("The upstairs hallway", row.Caption);
    }

    [Fact]
    public async Task An_ordinary_member_of_the_venue_and_a_stranger_see_nothing_and_cannot_decide()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;

        Assert.Empty(await ListAsync(As(w, VenueMember)));
        Assert.Empty(await ListAsync(As(w, Stranger)));
        Assert.IsType<NotFoundResult>(await As(w, Stranger).Decide(w.AtCragfont, new PlaceFileDecision(true), default));
    }

    [Fact]
    public async Task The_venue_approves_a_file_at_its_place_and_cannot_touch_another()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;

        Assert.IsType<NoContentResult>(await As(w, VenueAdmin).Decide(w.AtCragfont, new PlaceFileDecision(true), default));
        Assert.IsType<NotFoundResult>(await As(w, VenueAdmin).Decide(w.AtElsewhere, new PlaceFileDecision(true), default));

        await using var db = await w.Db.Factory.CreateDbContextAsync();
        Assert.Equal(FeedMediaReviewState.Approved, (await db.PlaceEvidence.SingleAsync(e => e.Id == w.AtCragfont)).ReviewState);
        Assert.Equal(FeedMediaReviewState.Pending, (await db.PlaceEvidence.SingleAsync(e => e.Id == w.AtElsewhere)).ReviewState);
    }

    [Fact]
    public async Task Holding_keeps_the_file_with_a_note_and_it_can_be_approved_later()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;
        var mod = As(w, Moderator, RoleNames.Moderator);

        await mod.Decide(w.AtElsewhere, new PlaceFileDecision(false, "Too dark to see anything"), default);
        var held = Assert.Single(await ListAsync(mod), r => r.EvidenceId == w.AtElsewhere);
        Assert.Equal(FeedMediaReviewState.Held, held.State);
        Assert.Equal("Too dark to see anything", held.Note);

        await mod.Decide(w.AtElsewhere, new PlaceFileDecision(true), default);
        Assert.DoesNotContain(await ListAsync(mod), r => r.EvidenceId == w.AtElsewhere);
    }

    [Fact]
    public async Task Waiting_counts_by_place_and_names_the_venue()
    {
        var w = await SeedAsync();
        await using var _ = w.Db;

        var waiting = Assert.IsAssignableFrom<IEnumerable<PlaceFilesWaiting>>(
            Assert.IsType<OkObjectResult>((await As(w, VenueAdmin).Waiting(default)).Result).Value).ToList();
        var only = Assert.Single(waiting);
        Assert.Equal("Cragfont", only.PlaceName);
        Assert.Equal(1, only.Count);
        Assert.NotNull(only.VenueOrganizationId);
    }
}
