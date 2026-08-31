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
        // The shipping screener: approves nothing, routes everything to a person. These suites
        // are about publication, not media, and this is what production actually does.
        => new(db, new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
               NullLogger<FieldSessionPublishController>.Instance)
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

    /// <summary>
    /// Puts this person on an active plan. Retraction is the paid half of the archive's bargain
    /// (Ben, 2026-08-31), so a test about what retraction DOES has to be run by somebody entitled
    /// to do it — see the test below for the free account's answer.
    /// </summary>
    private static async Task GiveAPaidPlanAsync(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Ben.Data.Source.Entities.Organization
        {
            Id = orgId, Name = "Paid", UrlName = $"paid-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new Ben.Data.Source.Entities.OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = Ben.Data.Common.Enums.OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationSubscriptions.Add(new Ben.Data.Source.Entities.OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Status = Ben.Data.Common.Enums.SubscriptionStatus.Active,
            Interval = Ben.Data.Common.Enums.BillingInterval.Monthly,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A free account may publish, and may not un-publish. Publish-then-hide is the whole exploit
    /// the paywall closes — take the credit for a contribution, then remove it from the archive
    /// everybody else's readings are compared against.
    /// </summary>
    [Fact]
    public async Task A_free_account_cannot_retract_what_it_published()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        await Publisher(factory, seed.UserId)
            .Publish(seed.SessionId, new FieldSessionPublishController.PublishRequest(seed.PublicPlaceId), default);

        var refusal = Assert.IsType<ObjectResult>(
            await Publisher(factory, seed.UserId).Retract(seed.SessionId, default));
        Assert.Equal(StatusCodes.Status402PaymentRequired, refusal.StatusCode);

        // Still there, which is the point — a refusal that left it half-retracted would be worse
        // than allowing it.
        Assert.Single((await VisitAsync(factory, seed.PublicPlaceId)).Sessions!);
    }

    [Fact]
    public async Task Retracting_removes_it_from_the_archive_but_keeps_where_it_happened()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        await GiveAPaidPlanAsync(factory, seed.UserId);
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

/// <summary>
/// The picker: offering the archive that already exists rather than letting somebody start a
/// second one beside it.
/// </summary>
/// <remarks>
/// String matching catches the easy duplicates and missed a real one on this feature's first
/// three-person run. A person shown "Bell Witch Cave · 3 sessions · 300 ft away" needs no
/// cleverness at all, which is why the picker — not the matcher — is the real answer.
/// </remarks>
public sealed class ArchiveCandidateTests
{
    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FieldSessionPublishController Controller(
        IDbContextFactory<BenDataContext> db, Guid userId)
        => new(db, new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
               NullLogger<FieldSessionPublishController>.Instance)
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

    private static async Task<Guid> SeedAsync(
        IDbContextFactory<BenDataContext> factory, string name, PlaceKind kind,
        decimal lat, decimal lon, int publishedSessions = 0)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = $"{userId}@t.com", NormalizedUserName = $"{userId}@T.COM".ToUpperInvariant(),
            Email = $"{userId}@t.com", DisplayName = "Contributor", DateCreated = DateTime.UtcNow,
        });
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = name, City = "Adams", State = "TN", Kind = kind,
            Latitude = lat, Longitude = lon, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        for (var i = 0; i < publishedSessions; i++)
        {
            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, FileName = "d.json", StoredFileName = $"{fileId}.json",
                ContentType = "application/json", AppUserId = userId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = Guid.NewGuid(), SubmittedByAppUserId = userId, DeviceSessionId = Guid.NewGuid(),
                DocumentUploadFileId = fileId, DeviceModel = "iPhone", StartedAt = DateTime.UtcNow,
                PlaceId = placeId, PublishedAtUtc = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }
        await db.SaveChangesAsync();
        return placeId;
    }

    private static async Task<IReadOnlyList<FieldSessionPublishController.ArchivePlaceCandidate>> AskAsync(
        IDbContextFactory<BenDataContext> factory, decimal? lat, decimal? lon)
    {
        var result = await Controller(factory, Guid.NewGuid()).Candidates(lat, lon, null, default);
        return (IReadOnlyList<FieldSessionPublishController.ArchivePlaceCandidate>)
            Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    [Fact]
    public async Task The_place_you_are_standing_in_is_offered_with_the_archive_it_already_has()
    {
        var factory = Db();
        await SeedAsync(factory, "Bell Witch Cave", PlaceKind.PublicLocation, 36.5806m, -87.0644m,
            publishedSessions: 3);

        var candidate = Assert.Single(await AskAsync(factory, 36.5807m, -87.0645m));
        Assert.Equal("Bell Witch Cave", candidate.Name);
        Assert.Equal(3, candidate.PublishedSessions);   // the reason to pick it
        Assert.True(candidate.Miles < 0.05);
    }

    [Fact]
    public async Task Somebody_s_home_is_never_offered_however_close_it_is()
    {
        // The safety hinge again, one layer earlier: a private residence must not even appear as
        // a suggestion, or the refusal at publish time reads as the app changing its mind.
        var factory = Db();
        await SeedAsync(factory, "A House", PlaceKind.PrivateResidence, 36.5806m, -87.0644m);

        Assert.Empty(await AskAsync(factory, 36.5806m, -87.0644m));
    }

    [Fact]
    public async Task Nearest_first_and_nothing_beyond_the_radius()
    {
        var factory = Db();
        await SeedAsync(factory, "Right Here", PlaceKind.PublicLocation, 36.5806m, -87.0644m);
        await SeedAsync(factory, "Half A Mile", PlaceKind.PublicLocation, 36.5878m, -87.0644m);
        // ~5 miles north: inside the coarse database box, outside the real radius — which is
        // exactly the case the in-memory distance pass exists to reject.
        await SeedAsync(factory, "Next Town", PlaceKind.PublicLocation, 36.6530m, -87.0644m);

        var candidates = await AskAsync(factory, 36.5806m, -87.0644m);
        Assert.Equal(2, candidates.Count);
        Assert.Equal("Right Here", candidates[0].Name);
        Assert.Equal("Half A Mile", candidates[1].Name);
    }

    [Fact]
    public async Task Without_coordinates_it_offers_nothing_rather_than_refusing()
    {
        // A session recorded with location declined is an ordinary session. Its owner should
        // meet "name where you were", not an error.
        var factory = Db();
        await SeedAsync(factory, "Bell Witch Cave", PlaceKind.PublicLocation, 36.5806m, -87.0644m);

        Assert.Empty(await AskAsync(factory, null, null));
    }
}

/// <summary>
/// Media in the archive, which is the part that fails closed or fails badly.
/// </summary>
/// <remarks>
/// Readings carry no abuse risk; strangers' photographs on a public page do. So the rule is that
/// media is invisible until a person has approved the night — and the screener that ships approves
/// nothing, which means publishing today shares the numbers and leaves the pictures waiting. That
/// is the intended shape until somebody is actually working the queue.
/// </remarks>
public sealed class ArchiveMediaTests
{
    private sealed class Screener(FeedMediaReviewState state, bool throws = false)
        : Ben.Data.WebApi.Services.Feed.IFeedMediaScreener
    {
        public bool IsAutomatic => true;
        public Task<Ben.Data.WebApi.Services.Feed.FeedMediaVerdict> ScreenAsync(
            string storagePath, string? contentType, CancellationToken ct)
            => throws
                ? throw new InvalidOperationException("the classifier fell over")
                : Task.FromResult(new Ben.Data.WebApi.Services.Feed.FeedMediaVerdict(state, null));
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FieldSessionPublishController Controller(
        IDbContextFactory<BenDataContext> db, Guid userId,
        Ben.Data.WebApi.Services.Feed.IFeedMediaScreener screener)
        => new(db, screener, NullLogger<FieldSessionPublishController>.Instance)
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

    private sealed record Seeded(Guid UserId, Guid PlaceId, Guid SessionId);

    private static async Task<Seeded> SeedAsync(
        IDbContextFactory<BenDataContext> factory, int mediaFiles)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = "c@t.com", NormalizedUserName = "C@T.COM",
            Email = "c@t.com", DisplayName = "Contributor", DateCreated = DateTime.UtcNow,
        });
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Bell Witch Cave", City = "Adams", State = "TN",
            Kind = PlaceKind.PublicLocation, Latitude = 36.5806m, Longitude = -87.0644m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        var docId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = docId, FileName = "data.json", StoredFileName = "d.json",
            ContentType = "application/json", AppUserId = userId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        var sessionId = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = userId, DeviceSessionId = Guid.NewGuid(),
            DocumentUploadFileId = docId, DeviceModel = "iPhone", StartedAt = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        for (var i = 0; i < mediaFiles; i++)
        {
            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, FileName = $"photo{i}.jpg", StoredFileName = $"{fileId}.jpg",
                ContentType = "image/jpeg", StoragePath = $"x/{fileId}.jpg", AppUserId = userId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
            {
                Id = Guid.NewGuid(), FieldSessionUploadId = sessionId, UploadFileId = fileId,
                RelativePath = $"media/photo{i}.jpg", DateCreated = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return new Seeded(userId, placeId, sessionId);
    }

    private static async Task PublishAsync(
        IDbContextFactory<BenDataContext> factory, Seeded seed,
        Ben.Data.WebApi.Services.Feed.IFeedMediaScreener screener)
        => await Controller(factory, seed.UserId, screener).Publish(
            seed.SessionId,
            new FieldSessionPublishController.PublishRequest(seed.PlaceId), default);

    private static async Task<int> ApprovedMediaCountAsync(
        IDbContextFactory<BenDataContext> factory, Guid placeId)
    {
        var result = await new Ben.Data.WebApi.Controllers.Public.PublicPlaceController(factory)
            .GetById(placeId, default);
        var response = (Ben.Data.WebApi.Controllers.Public.PublicPlaceResponse)
            Assert.IsType<OkObjectResult>(result.Result).Value!;
        return response.Sessions!.Single().ApprovedMediaCount;
    }

    /// <summary>The one that ships: examines nothing, defers to a person.</summary>
    private sealed class ManualScreener : Ben.Data.WebApi.Services.Feed.IFeedMediaScreener
    {
        public bool IsAutomatic => false;
        public Task<Ben.Data.WebApi.Services.Feed.FeedMediaVerdict> ScreenAsync(
            string storagePath, string? contentType, CancellationToken ct)
            => Task.FromResult(new Ben.Data.WebApi.Services.Feed.FeedMediaVerdict(
                FeedMediaReviewState.Pending, null));
    }

    [Fact]
    public async Task With_no_automatic_screener_publishing_wins_rather_than_waiting_forever()
    {
        // Post-moderation, Ben's call: deferring to the manual screener would leave every night
        // waiting on a person before anything appeared, and a solo operator either staffs that
        // queue daily or the archive silently never fills. The flag path below is what pays for
        // it — the cost moves onto the rare bad case instead of onto every good one.
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 2);

        await PublishAsync(factory, seed, new ManualScreener());

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.SingleAsync();
        Assert.NotNull(session.PublishedAtUtc);
        Assert.Equal(FeedMediaReviewState.Approved, session.MediaReviewState);
        Assert.Equal(2, await ApprovedMediaCountAsync(factory, seed.PlaceId));
    }

    [Fact]
    public async Task One_flag_hides_the_media_at_once_and_keeps_the_readings()
    {
        // The trade that makes publish-by-default safe: the flag ACTS, then a person decides.
        // Waiting for a moderator before hiding leaves the objected-to thing up for however long
        // that takes, which is the failure a report exists to prevent.
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 2);
        await PublishAsync(factory, seed, new ManualScreener());
        Assert.Equal(2, await ApprovedMediaCountAsync(factory, seed.PlaceId));

        Assert.IsType<NoContentResult>(await Controller(factory, Guid.NewGuid(), new ManualScreener())
            .Flag(seed.SessionId,
                  new FieldSessionPublishController.FlagRequest("that is my house"), default));

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.SingleAsync();
        Assert.Equal(FeedMediaReviewState.Held, session.MediaReviewState);
        Assert.Contains("that is my house", session.MediaReviewNote);
        Assert.Null(session.MediaReviewedUtc);   // shows in the queue as awaiting a decision

        // The pictures are gone; the contribution is not. Magnetic-field numbers cannot be
        // objectionable, and one flag must not erase somebody's session from the archive.
        Assert.Equal(0, await ApprovedMediaCountAsync(factory, seed.PlaceId));
        Assert.NotNull(session.PublishedAtUtc);
    }

    [Fact]
    public async Task Flagging_twice_is_one_flag_and_an_unpublished_session_cannot_be_flagged()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 1);

        // Nothing published — there is nothing public to object to.
        Assert.IsType<NotFoundResult>(await Controller(factory, Guid.NewGuid(), new ManualScreener())
            .Flag(seed.SessionId, null, default));

        await PublishAsync(factory, seed, new ManualScreener());
        await Controller(factory, Guid.NewGuid(), new ManualScreener()).Flag(seed.SessionId, null, default);
        DateTime? afterFirst;
        await using (var db = await factory.CreateDbContextAsync())
            afterFirst = (await db.FieldSessionUploads.SingleAsync()).DateUpdated;

        Assert.IsType<NoContentResult>(await Controller(factory, Guid.NewGuid(), new ManualScreener())
            .Flag(seed.SessionId, null, default));

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(afterFirst, (await after.FieldSessionUploads.SingleAsync()).DateUpdated);
    }

    [Fact]
    public async Task An_automatic_screener_still_gets_to_hold_the_obvious_cases()
    {
        // Post-moderation is the DEFAULT, not a refusal to screen. Wire a real classifier up and
        // it still pre-holds what it recognises; only the manual no-op is overridden.
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 1);

        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Held));

        Assert.Equal(0, await ApprovedMediaCountAsync(factory, seed.PlaceId));
    }

    [Fact]
    public async Task One_held_file_holds_the_whole_night()
    {
        // A page showing four of somebody's five photographs invites exactly the question the
        // fifth was withheld to avoid.
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 3);

        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Held));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(FeedMediaReviewState.Held, (await db.FieldSessionUploads.SingleAsync()).MediaReviewState);
        Assert.Equal(0, await ApprovedMediaCountAsync(factory, seed.PlaceId));
    }

    [Fact]
    public async Task A_screener_that_falls_over_leaves_the_media_private()
    {
        // An outage must never publish a photograph.
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 1);

        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Approved, throws: true));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(FeedMediaReviewState.Pending, (await db.FieldSessionUploads.SingleAsync()).MediaReviewState);
    }

    [Fact]
    public async Task A_session_with_no_media_never_enters_the_queue()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 0);

        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Pending));

        await using var db = await factory.CreateDbContextAsync();
        // Approved is honest for a night containing nothing to review, and keeps a readings-only
        // session out of a queue it would only clutter.
        Assert.Equal(FeedMediaReviewState.Approved, (await db.FieldSessionUploads.SingleAsync()).MediaReviewState);
    }

    [Fact]
    public async Task Republishing_never_undoes_a_reviewer_s_decision()
    {
        // Otherwise held media becomes public by its owner pressing publish again.
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 1);
        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Held));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var held = await db.FieldSessionUploads.SingleAsync();
            held.MediaReviewedUtc = DateTime.UtcNow;      // a person decided
            await db.SaveChangesAsync();
        }

        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Approved));

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(FeedMediaReviewState.Held, (await after.FieldSessionUploads.SingleAsync()).MediaReviewState);
    }

    [Fact]
    public async Task Once_approved_the_count_reaches_the_public_page()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, mediaFiles: 2);

        await PublishAsync(factory, seed, new Screener(FeedMediaReviewState.Approved));

        Assert.Equal(2, await ApprovedMediaCountAsync(factory, seed.PlaceId));
    }
}

/// <summary>
/// Folding two records of one place into one.
/// </summary>
/// <remarks>
/// Matching prevents most duplicates and the picker prevents most of the rest, but neither heals
/// what already exists — one afternoon's testing left three "Bell Witch Cave" records on the live
/// site, which is exactly the mess the archive promises not to make. These pin that a merge moves
/// EVERYTHING, because a row left pointing at a deleted place is somebody's work orphaned.
/// </remarks>
public sealed class PlaceMergeTests
{
    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                // The merge opens a transaction; the in-memory store has none. See the note in
                // AccountClosureTests — the transaction is right, it is just not what is tested.
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

    private static Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController Controller(
        IDbContextFactory<BenDataContext> db, Guid userId)
        => new(db, new Moq.Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object,
               NullLogger<Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController>.Instance)
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

    [Fact]
    public async Task Everything_moves_and_the_duplicate_disappears()
    {
        var factory = Db();
        var userId = Guid.NewGuid();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser
            {
                Id = userId, UserName = "a@t.com", NormalizedUserName = "A@T.COM",
                Email = "a@t.com", DisplayName = "Admin", DateCreated = DateTime.UtcNow,
            });
            foreach (var (id, name) in new[] { (keep, "Bell Witch Cave"), (drop, "Bell Witch Cave, Adams") })
                db.Places.Add(new Place
                {
                    Id = id, Name = name, City = "Adams", State = "TN",
                    Kind = PlaceKind.PublicLocation, Latitude = 36.5806m, Longitude = -87.0644m,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                });

            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, FileName = "d.json", StoredFileName = "d.json",
                ContentType = "application/json", AppUserId = userId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = Guid.NewGuid(), SubmittedByAppUserId = userId, DeviceSessionId = Guid.NewGuid(),
                DocumentUploadFileId = fileId, DeviceModel = "iPhone", StartedAt = DateTime.UtcNow,
                PlaceId = drop, PublishedAtUtc = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Controller(factory, userId).Merge(
            drop, new Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController.MergeRequest(keep), default);
        var merged = (Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController.MergeResult)
            Assert.IsType<OkObjectResult>(result.Result).Value!;
        Assert.Equal(1, merged.FieldSessions);

        await using var after = await factory.CreateDbContextAsync();
        Assert.Null(await after.Places.FindAsync(drop));
        // The session followed rather than being orphaned at a place that no longer exists.
        Assert.Equal(keep, (await after.FieldSessionUploads.SingleAsync()).PlaceId);
    }

    [Fact]
    public async Task A_place_cannot_be_merged_into_itself_or_into_nothing()
    {
        var factory = Db();
        var id = Guid.NewGuid();

        Assert.IsType<BadRequestObjectResult>((await Controller(factory, Guid.NewGuid())
            .Merge(id, new Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController.MergeRequest(id), default)).Result);

        Assert.IsType<NotFoundObjectResult>((await Controller(factory, Guid.NewGuid())
            .Merge(id, new Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController.MergeRequest(Guid.NewGuid()), default)).Result);
    }

    // ── The finder ────────────────────────────────────────────────────────────

    private static void SeedPlace(
        BenDataContext db, Guid userId, Guid id, string name,
        decimal lat, decimal lon, int published = 0, DateTime? created = null)
    {
        db.Places.Add(new Place
        {
            Id = id, Name = name, City = "Adams", State = "TN",
            Kind = PlaceKind.PublicLocation, Latitude = lat, Longitude = lon,
            DateCreated = created ?? DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        for (var i = 0; i < published; i++)
        {
            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, FileName = "d.json", StoredFileName = "d.json",
                ContentType = "application/json", AppUserId = userId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = Guid.NewGuid(), SubmittedByAppUserId = userId, DeviceSessionId = Guid.NewGuid(),
                DocumentUploadFileId = fileId, DeviceModel = "iPhone17,2", PlaceId = id,
                StartedAt = DateTime.UtcNow.AddHours(-2), EndedAt = DateTime.UtcNow.AddHours(-1),
                PublishedAtUtc = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }
    }

    private static async Task<IReadOnlyList<Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController.DuplicatePlaceGroup>>
        FindAsync(IDbContextFactory<BenDataContext> factory)
    {
        var response = await Controller(factory, Guid.NewGuid()).GetDuplicates(default);
        return Assert.IsType<List<Ben.Data.WebApi.Controllers.Admin.AdminPlaceMergeController.DuplicatePlaceGroup>>(
            Assert.IsType<OkObjectResult>(response.Result).Value);
    }

    [Fact]
    public async Task Places_far_apart_are_not_offered_as_duplicates()
    {
        var factory = Db();
        var userId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Adams and Nashville: the same name, forty miles apart, and genuinely two places.
            SeedPlace(db, userId, Guid.NewGuid(), "Bell Witch Cave", 36.5806m, -87.0644m);
            SeedPlace(db, userId, Guid.NewGuid(), "Bell Witch Cave", 36.1627m, -86.7816m);
            await db.SaveChangesAsync();
        }

        Assert.Empty(await FindAsync(factory));
    }

    [Fact]
    public async Task Near_neighbours_are_grouped_and_the_busiest_record_is_offered_first()
    {
        var factory = Db();
        var userId = Guid.NewGuid();
        var quiet = Guid.NewGuid();
        var busy = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            // The quiet one is created first, so ordering by age would put it first: what puts
            // the busy record at the top is the published work, which is the whole point.
            SeedPlace(db, userId, quiet, "Bell Witch Cave, Adams", 36.5806m, -87.0644m,
                published: 0, created: DateTime.UtcNow.AddDays(-10));
            SeedPlace(db, userId, busy, "Bell Witch Cave", 36.5807m, -87.0645m,
                published: 3, created: DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var group = Assert.Single(await FindAsync(factory));
        Assert.Equal(2, group.Places.Count);
        Assert.Equal(busy, group.Places[0].Id);
        Assert.Equal(3, group.Places[0].PublishedSessions);
        Assert.Equal(quiet, group.Places[1].Id);
    }

    [Fact]
    public async Task A_chain_of_near_neighbours_becomes_one_group()
    {
        var factory = Db();
        var userId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            // A is near B and B is near C, but A and C are not within the radius of each other.
            // Three records of one landmark is still one decision, not two.
            SeedPlace(db, userId, Guid.NewGuid(), "A", 36.5800m, -87.0644m);
            SeedPlace(db, userId, Guid.NewGuid(), "B", 36.5810m, -87.0644m);
            SeedPlace(db, userId, Guid.NewGuid(), "C", 36.5820m, -87.0644m);
            await db.SaveChangesAsync();
        }

        var group = Assert.Single(await FindAsync(factory));
        Assert.Equal(3, group.Places.Count);
    }

    [Fact]
    public async Task Places_without_coordinates_are_left_alone()
    {
        var factory = Db();
        var userId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Nothing to measure. Offering them would be guessing, and the merge is irreversible.
            foreach (var name in new[] { "Bell Witch Cave", "Bell Witch Cave" })
                db.Places.Add(new Place
                {
                    Id = Guid.NewGuid(), Name = name, City = "Adams", State = "TN",
                    Kind = PlaceKind.PublicLocation, DateCreated = DateTime.UtcNow,
                    CreatedByAppUserId = userId,
                });
            await db.SaveChangesAsync();
        }

        Assert.Empty(await FindAsync(factory));
    }
}
