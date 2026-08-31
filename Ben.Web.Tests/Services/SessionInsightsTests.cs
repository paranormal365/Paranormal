using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Archive;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What everybody else's visits say about one person's night (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>This is the individual's reason to hold a plan. A group buys people, cases and privacy;
/// somebody investigating alone has none of those to buy, and their recordings are already
/// private. What they cannot get at any price on their own is context.</para>
///
/// <para>The tests that carry the design are the ones about what is NOT withheld and what is NOT
/// counted. A paywall that hides figures already published on the place's page teaches people it
/// is arbitrary; an aggregate that quietly includes unpublished sessions leaks private work as a
/// number somebody can read out.</para>
/// </remarks>
public sealed class SessionInsightsTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid PlaceId, Guid MeId, Guid MySessionId);

    private static async Task<World> SeedAsync(
        int myMarkers = 4, double myHours = 2,
        PlaceKind kind = PlaceKind.PublicLocation)
    {
        var f = CreateFactory();
        Guid meId = Guid.NewGuid(), placeId = Guid.NewGuid(), mySessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = meId, UserName = "me@t.com", Email = "me@t.com",
            DisplayName = "Me", DateCreated = now,
        });
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Bell Witch Cave", Kind = kind,
            DateCreated = now, CreatedByAppUserId = meId,
        });
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = mySessionId, SubmittedByAppUserId = meId, PlaceId = placeId,
            StartedAt = now.AddHours(-myHours), EndedAt = now,
            MarkerCount = myMarkers, ReadingCount = 900,
            DocumentUploadFileId = Guid.NewGuid(), DeviceModel = "iPhone 17",
            DateCreated = now, CreatedByAppUserId = meId,
        });
        await db.SaveChangesAsync();

        return new World(f, placeId, meId, mySessionId);
    }

    /// <summary>Somebody else's visit here.</summary>
    private static async Task AddOtherAsync(
        World w, int markers, double hours, bool published = true, Guid? asUser = null)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var userId = asUser ?? Guid.NewGuid();
        var now = DateTime.UtcNow;

        if (!await db.AppUsers.AnyAsync(u => u.Id == userId))
        {
            db.AppUsers.Add(new AppUser
            {
                Id = userId, UserName = $"{userId}@t.com", Email = $"{userId}@t.com",
                DisplayName = "Another", DateCreated = now,
            });
        }

        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = Guid.NewGuid(), SubmittedByAppUserId = userId, PlaceId = w.PlaceId,
            StartedAt = now.AddHours(-hours), EndedAt = now,
            MarkerCount = markers, ReadingCount = 500,
            PublishedAtUtc = published ? now : null,
            DocumentUploadFileId = Guid.NewGuid(), DeviceModel = "Pixel",
            DateCreated = now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SessionInsights?> AskAsync(World w, bool detailed)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await SessionInsightsService.ForSessionAsync(db, w.MySessionId, w.MeId, detailed, default);
    }

    // ── the headline the archive exists to produce ───────────────────────────

    /// <summary>"Eleven of twelve people flagged something on these stairs."</summary>
    [Fact]
    public async Task It_counts_the_people_who_recorded_here_and_how_many_flagged_something()
    {
        var w = await SeedAsync();
        await AddOtherAsync(w, markers: 3, hours: 2);
        await AddOtherAsync(w, markers: 1, hours: 1);
        await AddOtherAsync(w, markers: 0, hours: 2);

        var insights = await AskAsync(w, detailed: true);

        Assert.NotNull(insights);
        Assert.Equal("Bell Witch Cave", insights!.PlaceName);
        Assert.Equal(3, insights.OthersWhoRecordedHere);
        Assert.Equal(2, insights.OthersWhoFlaggedSomething);
    }

    /// <summary>
    /// PEOPLE, not sessions. Twelve visits by one enthusiast is not a body of evidence, and
    /// counting sessions would let one person manufacture a consensus.
    /// </summary>
    [Fact]
    public async Task One_persons_many_visits_count_as_one_person()
    {
        var w = await SeedAsync();
        var them = Guid.NewGuid();
        await AddOtherAsync(w, markers: 5, hours: 1, asUser: them);
        await AddOtherAsync(w, markers: 5, hours: 1, asUser: them);
        await AddOtherAsync(w, markers: 5, hours: 1, asUser: them);

        var insights = await AskAsync(w, detailed: true);

        Assert.Equal(1, insights!.OthersWhoRecordedHere);
    }

    /// <summary>
    /// Unpublished work is private work. Letting it into the aggregate would leak it as a number
    /// somebody can read out — the quietest kind of disclosure and the hardest to notice.
    /// </summary>
    [Fact]
    public async Task Unpublished_sessions_are_not_counted()
    {
        var w = await SeedAsync();
        await AddOtherAsync(w, markers: 9, hours: 1, published: false);

        var insights = await AskAsync(w, detailed: true);

        Assert.Equal(0, insights!.OthersWhoRecordedHere);
        Assert.Null(insights.PlaceMedianMarkersPerHour);
    }

    // ── the comparison ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_much_busier_night_than_this_place_usually_gives_stands_out()
    {
        var w = await SeedAsync(myMarkers: 20, myHours: 2);        // 10/hour
        await AddOtherAsync(w, markers: 2, hours: 2);              // 1/hour
        await AddOtherAsync(w, markers: 4, hours: 2);              // 2/hour

        var insights = await AskAsync(w, detailed: true);

        Assert.Equal(10, insights!.YourMarkersPerHour);
        Assert.Equal(1.5, insights.PlaceMedianMarkersPerHour);
        Assert.True(insights.StandsOut);
    }

    /// <summary>
    /// The archive's most valuable answer is the deflating one: no, that is just this building.
    /// A feature that only ever confirms is astrology.
    /// </summary>
    [Fact]
    public async Task An_ordinary_night_for_this_place_does_not_stand_out()
    {
        var w = await SeedAsync(myMarkers: 4, myHours: 2);         // 2/hour
        await AddOtherAsync(w, markers: 4, hours: 2);              // 2/hour
        await AddOtherAsync(w, markers: 6, hours: 2);              // 3/hour

        var insights = await AskAsync(w, detailed: true);

        Assert.False(insights!.StandsOut);
    }

    /// <summary>
    /// "You did not stand out" and "nobody else has been here" are different answers. Reporting
    /// the first for the second would tell a place's first visitor their night was unremarkable
    /// against no evidence whatsoever.
    /// </summary>
    [Fact]
    public async Task The_first_person_at_a_place_is_told_nothing_rather_than_unremarkable()
    {
        var w = await SeedAsync();

        var insights = await AskAsync(w, detailed: true);

        Assert.Equal(0, insights!.OthersWhoRecordedHere);
        Assert.Null(insights.PlaceMedianMarkersPerHour);
        Assert.Null(insights.StandsOut);
    }

    /// <summary>A median, not a mean: one long vigil must not drag the typical night with it.</summary>
    [Fact]
    public async Task One_extraordinary_visit_does_not_move_what_is_typical_here()
    {
        var w = await SeedAsync(myMarkers: 4, myHours: 2);         // 2/hour
        await AddOtherAsync(w, markers: 2, hours: 2);              // 1/hour
        await AddOtherAsync(w, markers: 2, hours: 2);              // 1/hour
        await AddOtherAsync(w, markers: 600, hours: 2);            // 300/hour

        var insights = await AskAsync(w, detailed: true);

        // A mean would be over 100 and would call this person's ordinary night a quiet one.
        Assert.Equal(1, insights!.PlaceMedianMarkersPerHour);
    }

    // ── the paywall's shape ──────────────────────────────────────────────────

    /// <summary>
    /// The counts are on the place's public page for anybody. Hiding them behind a plan would be
    /// theatre, and the kind that teaches people the paywall is arbitrary.
    /// </summary>
    [Fact]
    public async Task A_free_account_still_sees_what_is_public_anyway()
    {
        var w = await SeedAsync();
        await AddOtherAsync(w, markers: 3, hours: 2);
        await AddOtherAsync(w, markers: 0, hours: 2);

        var insights = await AskAsync(w, detailed: false);

        Assert.False(insights!.Detailed);
        Assert.Equal(2, insights.OthersWhoRecordedHere);
        Assert.Equal(1, insights.OthersWhoFlaggedSomething);
        Assert.Equal("Bell Witch Cave", insights.PlaceName);
    }

    /// <summary>And the comparison — the part a plan actually buys — is withheld.</summary>
    [Fact]
    public async Task A_free_account_does_not_get_the_comparison()
    {
        var w = await SeedAsync();
        await AddOtherAsync(w, markers: 3, hours: 2);

        var insights = await AskAsync(w, detailed: false);

        Assert.Null(insights!.YourMarkersPerHour);
        Assert.Null(insights.PlaceMedianMarkersPerHour);
        Assert.Null(insights.StandsOut);
    }

    // ── what has nothing to say ──────────────────────────────────────────────

    [Fact]
    public async Task A_private_residence_has_no_archive_to_compare_against()
    {
        var w = await SeedAsync(kind: PlaceKind.PrivateResidence);

        Assert.Null(await AskAsync(w, detailed: true));
    }

    /// <summary>Somebody else's session is not yours to ask about.</summary>
    [Fact]
    public async Task Another_persons_session_answers_nothing()
    {
        var w = await SeedAsync();

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Null(await SessionInsightsService.ForSessionAsync(
            db, w.MySessionId, Guid.NewGuid(), detailed: true, default));
    }

    /// <summary>
    /// A thirty-second recording with one mark is 120 an hour. Dividing by it would make the
    /// noisiest thing in the archive an accident.
    /// </summary>
    [Fact]
    public async Task A_session_too_short_to_rate_reports_no_rate()
    {
        var w = await SeedAsync(myMarkers: 1, myHours: 0.0001);

        var insights = await AskAsync(w, detailed: true);

        Assert.Null(insights!.YourMarkersPerHour);
        Assert.Null(insights.StandsOut);
    }
}
