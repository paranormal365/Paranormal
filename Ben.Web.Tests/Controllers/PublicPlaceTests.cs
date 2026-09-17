using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The place page as a visitor sees it.
/// </summary>
/// <remarks>
/// The anonymous surface is the one where a sharing mistake is worst, so these tests are mostly
/// about what must <i>not</i> come back. The endpoint runs the same
/// <c>InvestigationVisibilityFilter</c> as the signed-in one, passed no organizations — the point
/// being that there is no second copy of the rules to fall out of step.
/// </remarks>
public class PublicPlaceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();

    private static PublicPlaceController Build(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            // No user at all, as a visitor.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        PlaceKind kind = PlaceKind.PublicLocation, decimal? lat = 36.5893m, decimal? lon = -87.0625m)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Paranormal365", UrlName = "paranormal365", DateCreated = DateTime.UtcNow });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "Bell Witch Cave", City = "Adams", State = "TN",
            StreetAddress1 = "430 Keysburg Rd", ZipCode = "37010", GeocodeNote = "matched 430 Keysburg Rd",
            Latitude = lat, Longitude = lon, Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task AddAsync(
        IDbContextFactory<BenDataContext> f, string title, InvestigationVisibility visibility, int yearsAgo = 1)
    {
        await using var db = await f.CreateDbContextAsync();
        db.Investigations.Add(new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PlaceId = PlaceId,
            Title = title, Visibility = visibility,
            ScheduledDateTime = DateTime.UtcNow.AddYears(-yearsAgo),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<PublicPlaceResponse> GetAsync(IDbContextFactory<BenDataContext> f)
    {
        var result = await Build(f).GetById(PlaceId, default);
        return Assert.IsType<PublicPlaceResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    /// <summary>
    /// The place page keeps its own copy of the archive's media rule, and it drifted: a recording
    /// inside a session's .ben was left out here while the archive endpoint served it. Same shape
    /// as ArchiveMediaPublicationTests — the id the page hands out is the row's, and the media
    /// route takes it.
    /// </summary>
    [Fact]
    public async Task A_recording_inside_a_published_sessions_file_is_listed_on_the_place_page()
    {
        var f = await SeedAsync();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            db.AppUsers.Add(new AppUser
            {
                Id = userId, UserName = "r@t.com", Email = "r@t.com", DisplayName = "Recorder", DateCreated = now,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = sessionId, SubmittedByAppUserId = userId, RecordedByName = "Recorder",
                PlaceId = PlaceId, PublishedAtUtc = now,
                MediaReviewState = Ben.Data.Common.Enums.FeedMediaReviewState.Approved,
                IsBundle = true, DocumentUploadFileId = Guid.NewGuid(),
                StartedAt = now.AddHours(-2), DeviceModel = "iPhone 17", ReadingCount = 43, MarkerCount = 3,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
            {
                Id = rowId, FieldSessionUploadId = sessionId, UploadFileId = null,
                BundleEntryPath = "media/audio-001.m4a", RelativePath = "media/audio-001.m4a",
                ContentType = "audio/mp4", FileSize = 715_004,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var response = await GetAsync(f);

        var session = Assert.Single(response.Sessions);
        var media = Assert.Single(session.Media!);
        Assert.Equal(rowId, media.UploadFileId);
        Assert.Equal("audio/mp4", media.ContentType);
        Assert.Equal("audio-001.m4a", media.FileName);
    }

    // ── What a visitor must not see ───────────────────────────────────────────

    [Fact]
    public async Task Only_published_investigations_come_back()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published", InvestigationVisibility.Public);
        await AddAsync(f, "Group only", InvestigationVisibility.GroupOnly);
        await AddAsync(f, "Shared with fellow investigators", InvestigationVisibility.PlaceInvestigators);

        var response = await GetAsync(f);

        // PlaceInvestigators must not leak to an anonymous caller: they have investigated nowhere,
        // so they never qualify for that audience however it is worded.
        Assert.Equal("Published", Assert.Single(response.Investigations).Title);
    }

    [Fact]
    public async Task A_place_with_nothing_published_still_loads()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Group only", InvestigationVisibility.GroupOnly);

        var response = await GetAsync(f);

        // The place exists and is worth showing; it simply has no published history. A 404 here
        // would tell a visitor the place is not real, which is a different and wrong statement.
        Assert.Equal("Bell Witch Cave", response.Place.Name);
        Assert.Empty(response.Investigations);
    }

    [Fact]
    public async Task An_unknown_place_is_not_found()
    {
        var f = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Build(f).GetById(Guid.NewGuid(), default)).Result);
    }

    // ── The summary ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_summary_counts_only_what_is_visible()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published one", InvestigationVisibility.Public, yearsAgo: 5);
        await AddAsync(f, "Published two", InvestigationVisibility.Public, yearsAgo: 1);
        await AddAsync(f, "Hidden", InvestigationVisibility.GroupOnly, yearsAgo: 9);

        var summary = (await GetAsync(f)).Summary;

        // Counting everything would quietly tell the visitor how much is being withheld — and the
        // "since" year would give away the date of a visit they cannot see.
        Assert.Equal(2, summary.InvestigationCount);
        Assert.Equal(1, summary.OrganizationCount);
        Assert.Equal(DateTime.UtcNow.AddYears(-5).Year, summary.Since);
    }

    [Fact]
    public async Task The_summary_reports_no_year_when_there_is_nothing_to_show()
    {
        var f = await SeedAsync();

        var summary = (await GetAsync(f)).Summary;

        // Null rather than 0 or this year, so the page can drop the phrase instead of printing
        // something meaningless.
        Assert.Equal(0, summary.InvestigationCount);
        Assert.Null(summary.Since);
    }

    [Fact]
    public async Task Groups_are_counted_once_however_many_visits_they_made()
    {
        var f = await SeedAsync();
        await AddAsync(f, "First", InvestigationVisibility.Public, yearsAgo: 3);
        await AddAsync(f, "Second", InvestigationVisibility.Public, yearsAgo: 2);

        Assert.Equal(1, (await GetAsync(f)).Summary.OrganizationCount);
    }

    // ── What it carries ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_row_carries_the_groups_public_url_name()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published", InvestigationVisibility.Public);

        var row = Assert.Single((await GetAsync(f)).Investigations);

        // So the page can link to the group's public page without a second lookup.
        Assert.Equal("Paranormal365", row.OrganizationName);
        Assert.Equal("paranormal365", row.OrganizationUrlName);
    }

    [Fact]
    public async Task A_place_with_no_coordinates_still_loads()
    {
        var f = await SeedAsync(lat: null, lon: null);
        await AddAsync(f, "Published", InvestigationVisibility.Public);

        var response = await GetAsync(f);

        // The page drops the map rather than failing; an unplaceable place is still a place.
        Assert.Null(response.Place.Latitude);
        Assert.Single(response.Investigations);
    }

    // ── Cases written up here (2026-09-17) ───────────────────────────────────

    /// <summary>Adds a case at the seeded place.</summary>
    private static async Task AddCaseAsync(
        IDbContextFactory<BenDataContext> f, string title, bool isPublic, CaseStatus status,
        bool atThePlace = true, bool privateEngagement = false, string? urlName = "the-old-depot")
    {
        await using var db = await f.CreateDbContextAsync();
        db.Cases.Add(new Case
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId,
            PlaceId = atThePlace ? PlaceId : null,
            Title = title, UrlName = urlName,
            CaseYear = 2026, OrgCaseNumber = await db.Cases.CountAsync() + 1,
            StreetAddress1 = "1 Keysburg Rd", City = "Adams", State = "TN",
            ZipCode = "37010", Country = "US",
            IsPublic = isPublic, Status = status, IsPrivateEngagement = privateEngagement,
            DateCaseOpened = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_published_case_at_this_place_is_listed()
    {
        var f = await SeedAsync();
        await AddCaseAsync(f, "The old depot", isPublic: true, status: CaseStatus.Public);

        var row = Assert.Single((await GetAsync(f)).Cases!);

        Assert.Equal("The old depot", row.Title);
        Assert.Equal("#2026-001", row.CaseReference);
        Assert.Equal(2024, row.OpenedYear);
        // So the row can link to the case and to its group without a second lookup.
        Assert.Equal("the-old-depot", row.UrlName);
        Assert.Equal("paranormal365", row.OrganizationUrlName);
    }

    /// <summary>
    /// The same two conditions the case's own public page uses, and both are load-bearing. A case
    /// merely flagged public is not published, and from 2026-09-17 an unpaid account's cases carry
    /// that flag from birth — so the flag alone would put every one of them on a public page.
    /// </summary>
    [Theory]
    [InlineData(true,  CaseStatus.Proposed,   false)]
    [InlineData(true,  CaseStatus.Accepted,   false)]
    [InlineData(true,  CaseStatus.Active,     false)]
    [InlineData(true,  CaseStatus.Closed,     false)]
    [InlineData(false, CaseStatus.Public,     false)]
    [InlineData(true,  CaseStatus.Public,     true)]
    [InlineData(true,  CaseStatus.Haunted,    true)]
    public async Task Only_a_published_case_reaches_the_place_page(
        bool isPublic, CaseStatus status, bool listed)
    {
        var f = await SeedAsync();
        await AddCaseAsync(f, "The old depot", isPublic, status);

        var cases = (await GetAsync(f)).Cases!;
        Assert.Equal(listed ? 1 : 0, cases.Count);
    }

    [Fact]
    public async Task A_published_case_somewhere_else_is_not_listed()
    {
        var f = await SeedAsync();
        await AddCaseAsync(f, "Elsewhere", isPublic: true, status: CaseStatus.Public, atThePlace: false);

        Assert.Empty((await GetAsync(f)).Cases!);
    }

    /// <summary>
    /// A private-engagement case reaches this list only by being published, which item 184 gates on
    /// the plan — and its prose is redacted on the way out, exactly as on the case's own page. The
    /// title here carries no client name because a roster with nothing in it redacts nothing; the
    /// point of the test is that the redactor is asked at all.
    /// </summary>
    [Fact]
    public async Task A_published_private_engagement_case_goes_through_the_redactor()
    {
        var f = await SeedAsync();
        await AddCaseAsync(f, "A family home", isPublic: true, status: CaseStatus.Public,
                           privateEngagement: true);

        var row = Assert.Single((await GetAsync(f)).Cases!);
        Assert.Equal("A family home", row.Title);
    }

    /// <summary>
    /// A case published before slugs existed still has somewhere to point, rather than a link to
    /// nothing — the same fallback the group's own public case list uses.
    /// </summary>
    [Fact]
    public async Task A_case_with_no_slug_falls_back_to_its_reference()
    {
        var f = await SeedAsync();
        await AddCaseAsync(f, "Before slugs", isPublic: true, status: CaseStatus.Public, urlName: null);

        Assert.Equal("2026-001", Assert.Single((await GetAsync(f)).Cases!).UrlName);
    }

    [Fact]
    public async Task A_place_with_no_cases_answers_an_empty_list_rather_than_null()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published", InvestigationVisibility.Public);

        // Empty, not null: the page branches on Count, and a null would read as "an older server
        // that does not know about cases" rather than "no cases here".
        Assert.NotNull((await GetAsync(f)).Cases);
        Assert.Empty((await GetAsync(f)).Cases!);
    }

    // ── What a residence may tell a stranger (2026-09-17 audit) ──────────────────────────────
    //
    // The leak these hold shut: this endpoint projected straight into PlaceRecord, so the street
    // address, the ZIP and the EXACT coordinates of a client's home went to anybody with the URL.
    // It was the only anonymous surface that never called PublicCoordinates, and the place page
    // became a signed-out destination while the projection stayed as written on 2026-08-15.

    [Fact]
    public async Task A_private_residence_does_not_publish_its_street_address_or_zip()
    {
        var f = await SeedAsync(PlaceKind.PrivateResidence);

        var place = (await GetAsync(f)).Place;

        Assert.Null(place.StreetAddress1);
        Assert.Null(place.ZipCode);
        // The geocoder's note quotes back what it matched, which is the address again.
        Assert.Null(place.GeocodeNote);
    }

    /// <summary>
    /// A residence is routinely named after the family living in it, so the name is withheld
    /// outright rather than redacted — the place page has no single case whose roster would apply.
    /// </summary>
    [Fact]
    public async Task A_private_residence_does_not_publish_its_name()
        => Assert.Null((await GetAsync(await SeedAsync(PlaceKind.PrivateResidence))).Place.Name);

    [Fact]
    public async Task A_private_residence_publishes_an_approximated_position_not_the_real_one()
    {
        var f = await SeedAsync(PlaceKind.PrivateResidence, lat: 36.5893m, lon: -87.0625m);

        var place = (await GetAsync(f)).Place;

        // Present, so the page can still say roughly where this is...
        Assert.NotNull(place.Latitude);
        Assert.NotNull(place.Longitude);
        // ...but never the stored point, which is what a map pin at an address is.
        Assert.NotEqual(36.5893m, place.Latitude);
        Assert.NotEqual(-87.0625m, place.Longitude);
    }

    /// <summary>
    /// City and state stay. They are what a published case already says about its own location, so
    /// withholding them here would take away a fact the rest of the site discloses on purpose and
    /// leave the page unable to say anything at all.
    /// </summary>
    [Fact]
    public async Task A_private_residence_still_publishes_its_city_and_state()
    {
        var place = (await GetAsync(await SeedAsync(PlaceKind.PrivateResidence))).Place;

        Assert.Equal("Adams", place.City);
        Assert.Equal("TN", place.State);
    }

    [Fact]
    public async Task A_public_location_publishes_everything_it_always_did()
    {
        var place = (await GetAsync(await SeedAsync(PlaceKind.PublicLocation))).Place;

        Assert.Equal("Bell Witch Cave", place.Name);
        Assert.Equal("430 Keysburg Rd", place.StreetAddress1);
        Assert.Equal("37010", place.ZipCode);
        // A landmark's own coordinates are the point of the page.
        Assert.Equal(36.5893m, place.Latitude);
        Assert.Equal(-87.0625m, place.Longitude);
    }
}
