using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The public place archive: field sessions published to a location so visits become comparable.
/// </summary>
/// <remarks>
/// <para>The feature's value and its danger are the same fact — these recordings become readable
/// by anybody. So the tests that matter most are the refusals: private residences are never
/// archivable, nobody publishes somebody else's night, and an unpublished session is invisible to
/// a visitor however much it is attached to a place.</para>
/// </remarks>
public sealed class FieldSessionArchiveTests
{
    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FieldSessionPublishController Publisher(
        IDbContextFactory<BenDataContext> db, Guid userId)
        => new(db, NullLogger<FieldSessionPublishController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    private sealed record Seeded(Guid UserId, Guid OtherUserId, Guid PublicPlaceId, Guid HomeId, Guid SessionId);

    private static async Task<Seeded> SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        foreach (var (id, name) in new[] { (userId, "Emma Rodriguez"), (otherId, "A Stranger") })
            db.AppUsers.Add(new AppUser
            {
                Id = id, UserName = $"{id}@t.com", NormalizedUserName = $"{id}@T.COM".ToUpperInvariant(),
                Email = $"{id}@t.com", DisplayName = name, DateCreated = DateTime.UtcNow,
            });

        var publicPlaceId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = publicPlaceId, Name = "Bell Witch Cave", City = "Adams", State = "TN",
            Kind = PlaceKind.PublicLocation, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        var homeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = homeId, Name = "Belmont Boulevard Residence", City = "Nashville", State = "TN",
            Kind = PlaceKind.PrivateResidence, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, FileName = "data.json", StoredFileName = "x.json", ContentType = "application/json",
            AppUserId = userId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        var sessionId = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = userId, DeviceSessionId = Guid.NewGuid(),
            DocumentUploadFileId = fileId, DeviceModel = "iPhone17,2",
            LocationLabel = "Cellar stairs", StartedAt = DateTime.UtcNow.AddHours(-2),
            EndedAt = DateTime.UtcNow.AddHours(-1), ReadingCount = 812, MarkerCount = 3,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return new Seeded(userId, otherId, publicPlaceId, homeId, sessionId);
    }

    private static async Task<PublicPlaceResponse> VisitAsync(
        IDbContextFactory<BenDataContext> factory, Guid placeId)
    {
        var result = await new PublicPlaceController(factory).GetById(placeId, default);
        return (PublicPlaceResponse)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    // ── the refusals, first ───────────────────────────────────────────────────

    [Fact]
    public async Task A_session_at_somebody_s_home_can_never_be_archived()
    {
        // The safety hinge of the whole feature. PlaceKind defaults to PrivateResidence, so this
        // refusal is what stands between the archive and publishing sensor readings, timings and
        // coordinates taken inside a stranger's house.
        var factory = Db();
        var seed = await SeedAsync(factory);

        var result = await Publisher(factory, seed.UserId)
            .Publish(seed.SessionId, new FieldSessionPublishController.PublishRequest(seed.HomeId), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("public locations", refusal.Value!.ToString(), StringComparison.OrdinalIgnoreCase);

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.SingleAsync();
        Assert.Null(session.PublishedAtUtc);
        Assert.Null(session.PlaceId);
    }

    [Fact]
    public async Task Nobody_publishes_somebody_else_s_night()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);

        var result = await Publisher(factory, seed.OtherUserId)
            .Publish(seed.SessionId, new FieldSessionPublishController.PublishRequest(seed.PublicPlaceId), default);

        // NotFound rather than Forbid: whether somebody else's session exists is not a thing to
        // let an outsider probe for.
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task An_unpublished_session_is_invisible_however_attached_to_a_place_it_is()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Place set, publication never performed — the exact state a session is in between
            // "I know where I was" and "I want the world to see it".
            var session = await db.FieldSessionUploads.SingleAsync();
            session.PlaceId = seed.PublicPlaceId;
            await db.SaveChangesAsync();
        }

        var visit = await VisitAsync(factory, seed.PublicPlaceId);
        Assert.Empty(visit.Sessions!);
    }

    // ── what the archive is for ───────────────────────────────────────────────

    [Fact]
    public async Task A_published_session_reaches_a_visitor_with_what_makes_visits_comparable()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);

        Assert.IsType<NoContentResult>(await Publisher(factory, seed.UserId)
            .Publish(seed.SessionId, new FieldSessionPublishController.PublishRequest(seed.PublicPlaceId), default));

        // Anonymous — no user on the controller at all, which is the whole point.
        var row = Assert.Single((await VisitAsync(factory, seed.PublicPlaceId)).Sessions!);

        Assert.Equal("Emma Rodriguez", row.ContributorName);
        Assert.Equal("Cellar stairs", row.LocationLabel);
        Assert.Equal(812, row.ReadingCount);
        // The number the archive exists to make comparable across visits.
        Assert.Equal(3, row.MarkerCount);
        Assert.Equal("iPhone17,2", row.DeviceModel);
    }

    [Fact]
    public async Task Many_visits_by_many_people_accumulate_on_the_place()
    {
        // The feature in one assertion: independent contributors, one location, one archive.
        var factory = Db();
        var seed = await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var file = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = file, FileName = "data.json", StoredFileName = "y.json",
                ContentType = "application/json", AppUserId = seed.OtherUserId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.OtherUserId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = Guid.NewGuid(), SubmittedByAppUserId = seed.OtherUserId,
                DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = file,
                DeviceModel = "Pixel 9", LocationLabel = "Cellar stairs",
                StartedAt = DateTime.UtcNow.AddDays(-400), ReadingCount = 640, MarkerCount = 2,
                PlaceId = seed.PublicPlaceId, PublishedAtUtc = DateTime.UtcNow.AddDays(-399),
                DateCreated = DateTime.UtcNow.AddDays(-400), CreatedByAppUserId = seed.OtherUserId,
            });
            await db.SaveChangesAsync();
        }
        await Publisher(factory, seed.UserId)
            .Publish(seed.SessionId, new FieldSessionPublishController.PublishRequest(seed.PublicPlaceId), default);

        var sessions = (await VisitAsync(factory, seed.PublicPlaceId)).Sessions!;
        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, sessions.Select(s => s.ContributorAppUserId).Distinct().Count());
        // Newest first — a place reads as a history, most recent visit at the top.
        Assert.True(sessions[0].StartedAt > sessions[1].StartedAt);
        Assert.Equal(5, sessions.Sum(s => s.MarkerCount));
    }

    // ── taking it back ────────────────────────────────────────────────────────

    [Fact]
    public async Task Retracting_removes_it_from_the_archive_but_keeps_where_it_happened()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        await Publisher(factory, seed.UserId)
            .Publish(seed.SessionId, new FieldSessionPublishController.PublishRequest(seed.PublicPlaceId), default);

        Assert.IsType<NoContentResult>(await Publisher(factory, seed.UserId).Retract(seed.SessionId, default));

        Assert.Empty((await VisitAsync(factory, seed.PublicPlaceId)).Sessions!);

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.SingleAsync();
        Assert.Null(session.PublishedAtUtc);
        // Where it happened is a fact about the recording, not a consequence of having shared it.
        Assert.Equal(seed.PublicPlaceId, session.PlaceId);
    }

    [Fact]
    public async Task Publishing_twice_does_not_move_the_date_it_became_public()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        var request = new FieldSessionPublishController.PublishRequest(seed.PublicPlaceId);

        await Publisher(factory, seed.UserId).Publish(seed.SessionId, request, default);
        DateTime first;
        await using (var db = await factory.CreateDbContextAsync())
            first = (await db.FieldSessionUploads.SingleAsync()).PublishedAtUtc!.Value;

        await Publisher(factory, seed.UserId).Publish(seed.SessionId, request, default);

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(first, (await after.FieldSessionUploads.SingleAsync()).PublishedAtUtc);
    }
}

/// <summary>
/// The archive's JSON, asserted on both sides of the wire.
/// </summary>
/// <remarks>
/// The website restates <c>PublicPlaceSessionRow</c> in <c>BenAdminClientRecords</c> because it
/// cannot reference the WebApi project, and the two are married only by property name. Rename one
/// and both still compile — the place page simply stops showing an archive that is demonstrably
/// there. An invented fixture has shipped exactly that failure here before (item: the iOS
/// contract), so this pins the real serializer against the real client record.
/// </remarks>
public sealed class PublicPlaceSessionWireShapeTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Web =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public void The_server_row_decodes_into_the_website_s_own_record()
    {
        var server = new Ben.Data.WebApi.Controllers.Public.PublicPlaceSessionRow(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Emma Rodriguez",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Cellar stairs",
            new DateTime(2026, 8, 30, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 30, 23, 30, 0, DateTimeKind.Utc),
            812, 3, "iPhone17,2",
            new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var json = System.Text.Json.JsonSerializer.Serialize(server, Web);
        var client = System.Text.Json.JsonSerializer
            .Deserialize<Ben.Web.Services.PublicPlaceSessionRow>(json, Web);

        Assert.NotNull(client);
        Assert.Equal("Emma Rodriguez", client.ContributorName);
        Assert.Equal("Cellar stairs", client.LocationLabel);
        Assert.Equal(812, client.ReadingCount);
        Assert.Equal(3, client.MarkerCount);
        Assert.Equal("iPhone17,2", client.DeviceModel);
        Assert.Equal(server.ContributorAppUserId, client.ContributorAppUserId);
        Assert.Equal(server.StartedAt, client.StartedAt);
    }

    [Fact]
    public void A_response_from_before_the_archive_existed_still_decodes()
    {
        // Sessions is defaulted on both sides, so a cached or older payload reads as "no archive"
        // rather than throwing on a page that is mostly about something else.
        var json = """{"place":null,"investigations":[],"summary":null}""";
        var client = System.Text.Json.JsonSerializer
            .Deserialize<Ben.Web.Services.PublicPlaceResponse>(json, Web);

        Assert.NotNull(client);
        Assert.Null(client.Sessions);
    }
}

/// <summary>
/// The rule that decides whether an archive accumulates or fragments.
/// </summary>
/// <remarks>
/// Found the hard way: three people published to one cave on the first end-to-end run and landed
/// on two places, because one wrote "Keysburg Road" where another wrote "Keysburg Rd" and appended
/// the town to the name. Splitting one location into two pages that each look deserted is the
/// single failure this feature cannot survive, so the looser rule exists — and is confined to
/// public locations, because merging two homes on one street would be a different kind of wrong.
/// </remarks>
public sealed class ArchivePlaceMatchingTests
{
    private static Ben.Data.Source.Entities.Place Cave(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, City = "Adams", State = "TN",
        Latitude = 36.5806m, Longitude = -87.0644m, Kind = PlaceKind.PublicLocation,
    };

    [Theory]
    [InlineData("Bell Witch Cave", "Bell Witch Cave, Adams")]   // the town appended
    [InlineData("Bell Witch Cave, Adams", "Bell Witch Cave")]   // and the other way round
    [InlineData("Bell Witch Cave", "The Bell Witch Cave")]      // the article NormaliseName drops
    [InlineData("Bell Witch Cave", "bell witch cave")]          // case and spacing
    public void One_landmark_described_two_ways_is_one_place(string stored, string typed)
        => Assert.True(Ben.Data.WebApi.Services.Places.PlaceMatcher.IsProbableArchiveMatch(
            Cave(stored), typed, 36.5807m, -87.0645m));

    [Fact]
    public void Two_different_landmarks_at_one_spot_stay_apart()
    {
        // Adjacent buildings share coordinates at this precision; only the name keeps them apart.
        Assert.False(Ben.Data.WebApi.Services.Places.PlaceMatcher.IsProbableArchiveMatch(
            Cave("Bell Witch Cave"), "Adams Museum", 36.5806m, -87.0644m));
    }

    [Fact]
    public void A_place_a_mile_away_is_never_the_same_place()
        => Assert.False(Ben.Data.WebApi.Services.Places.PlaceMatcher.IsProbableArchiveMatch(
            Cave("Bell Witch Cave"), "Bell Witch Cave", 36.60m, -87.10m));

    [Fact]
    public void Without_coordinates_on_both_sides_it_refuses_to_guess()
    {
        // The ordinary matcher treats missing coordinates as "close enough to OFFER", which is
        // right when a person is confirming a candidate and wrong when the match is applied with
        // nobody looking.
        Assert.False(Ben.Data.WebApi.Services.Places.PlaceMatcher.IsProbableArchiveMatch(
            Cave("Bell Witch Cave"), "Bell Witch Cave", null, null));

        var noCoords = Cave("Bell Witch Cave");
        noCoords.Latitude = null; noCoords.Longitude = null;
        Assert.False(Ben.Data.WebApi.Services.Places.PlaceMatcher.IsProbableArchiveMatch(
            noCoords, "Bell Witch Cave", 36.5806m, -87.0644m));
    }
}
