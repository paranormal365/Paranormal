using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Guest evidence contributed onward to the archive of the place the event was held at.
/// </summary>
/// <remarks>
/// <para>A tour walks the same route every week, which makes public events the one activity that
/// happens repeatedly at fixed locations — exactly what a location-keyed archive needs.</para>
///
/// <para>The tests that carry the design are the two about INDEPENDENCE from the operator's
/// verdict, in both directions: a photograph declined for the event's gallery is still the
/// photographer's to contribute, and one accepted is not thereby published here. Consenting to
/// somebody's gallery is not consent to publish under your own name.</para>
/// </remarks>
public sealed class ArchiveEvidencePublicationTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid PlaceId, Guid EventId, Guid SubmissionId);

    private static async Task<World> SeedAsync(
        EvidenceSubmissionStatus operatorVerdict = EvidenceSubmissionStatus.Rejected,
        bool publishedToPlace = true,
        FeedMediaReviewState archiveState = FeedMediaReviewState.Approved,
        PlaceKind placeKind = PlaceKind.PublicLocation,
        bool eventHasPlace = true)
    {
        var f = CreateFactory();
        Guid guestId = Guid.NewGuid(), orgId = Guid.NewGuid(), placeId = Guid.NewGuid(),
             eventId = Guid.NewGuid(), fileId = Guid.NewGuid(), submissionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = guestId, UserName = "g@t.com", Email = "g@t.com",
            DisplayName = "A Guest", DateCreated = now,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Nightfall Walks", UrlName = $"nw-{orgId:N}",
            DateCreated = now, CreatedByAppUserId = guestId,
        });
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Old Town", Kind = placeKind,
            DateCreated = now, CreatedByAppUserId = guestId,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = eventId, OrganizationId = orgId, Title = "Old Town Ghost Walk",
            IsPublic = true, StartDateTime = now.AddDays(-2),
            PlaceId = eventHasPlace ? placeId : null,
            DateCreated = now, CreatedByAppUserId = guestId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, FileName = "orb.jpg", ContentType = "image/jpeg",
            StoragePath = $"orgs/{orgId}/event-evidence/{fileId}.jpg", FileSize = 2048,
            AppUserId = guestId, DateCreated = now, CreatedByAppUserId = guestId,
        });
        db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
        {
            Id = submissionId, OrgCalendarEventId = eventId,
            SubmittedByAppUserId = guestId, UploadFileId = fileId,
            Status = operatorVerdict,
            PublishedToPlaceAtUtc = publishedToPlace ? now : null,
            ArchiveReviewState = archiveState,
            DateCreated = now, CreatedByAppUserId = guestId,
        });
        await db.SaveChangesAsync();

        return new World(f, placeId, eventId, submissionId);
    }

    private static async Task<(bool MayServe, int OnThePage)> AskAsync(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return (await ArchiveEvidencePublication.MayServeAsync(db, w.SubmissionId, default),
                (await ArchiveEvidencePublication.ForPlaceAsync(db, w.PlaceId, default)).Count);
    }

    // ── independent of the operator, in both directions ──────────────────────

    /// <summary>
    /// The operator curates their event. They do not come to own what somebody else photographed,
    /// so a decline does not stop the photographer contributing it to the place's record.
    /// </summary>
    [Fact]
    public async Task Evidence_the_operator_declined_is_still_the_photographers_to_contribute()
    {
        var (mayServe, onThePage) = await AskAsync(
            await SeedAsync(operatorVerdict: EvidenceSubmissionStatus.Rejected));

        Assert.True(mayServe);
        Assert.Equal(1, onThePage);
    }

    /// <summary>
    /// And the other direction, which matters more: accepting it for the gallery does NOT publish
    /// it here. Consenting to somebody's gallery is not consent to publish under your own name.
    /// </summary>
    [Fact]
    public async Task Evidence_the_operator_accepted_is_not_published_here_by_that_alone()
    {
        var (mayServe, onThePage) = await AskAsync(
            await SeedAsync(operatorVerdict: EvidenceSubmissionStatus.Accepted,
                            publishedToPlace: false));

        Assert.False(mayServe);
        Assert.Equal(0, onThePage);
    }

    // ── the gate's other clauses ─────────────────────────────────────────────

    [Fact]
    public async Task Held_evidence_serves_nothing()
    {
        var (mayServe, onThePage) = await AskAsync(
            await SeedAsync(archiveState: FeedMediaReviewState.Held));

        Assert.False(mayServe);
        Assert.Equal(0, onThePage);
    }

    [Fact]
    public async Task Unscreened_evidence_serves_nothing()
    {
        var (mayServe, onThePage) = await AskAsync(
            await SeedAsync(archiveState: FeedMediaReviewState.Pending));

        Assert.False(mayServe);
        Assert.Equal(0, onThePage);
    }

    /// <summary>
    /// An event at a private address cannot feed a public archive — the same clause that carries
    /// the safety story for field sessions.
    /// </summary>
    [Fact]
    public async Task An_event_at_a_private_residence_serves_nothing()
    {
        var (mayServe, onThePage) = await AskAsync(
            await SeedAsync(placeKind: PlaceKind.PrivateResidence));

        Assert.False(mayServe);
        Assert.Equal(0, onThePage);
    }

    [Fact]
    public async Task An_event_with_no_place_serves_nothing()
    {
        var w = await SeedAsync(eventHasPlace: false);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.False(await ArchiveEvidencePublication.MayServeAsync(db, w.SubmissionId, default));
    }

    // ── what the page carries ────────────────────────────────────────────────

    /// <summary>
    /// The operator's name rides along on a page they did not buy an ad on. The tour that ran the
    /// walk earned the credit, and it is the reason this is worth their while.
    /// </summary>
    [Fact]
    public async Task The_row_credits_both_the_photographer_and_the_operator()
    {
        var w = await SeedAsync();

        await using var db = await w.F.CreateDbContextAsync();
        var row = Assert.Single(await ArchiveEvidencePublication.ForPlaceAsync(db, w.PlaceId, default));

        Assert.Equal("A Guest", row.ContributorName);
        Assert.Equal("Nightfall Walks", row.OrganizationName);
        Assert.Equal("Old Town Ghost Walk", row.EventTitle);
        Assert.Equal(w.EventId, row.OrgCalendarEventId);
    }

    /// <summary>
    /// Retraction takes it off the page. The rule is asked per request rather than cached into a
    /// flag, so taking it back actually takes it back.
    /// </summary>
    [Fact]
    public async Task Retracting_removes_it_from_the_place()
    {
        var w = await SeedAsync();
        Assert.True((await AskAsync(w)).MayServe);

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var submission = await db.EventEvidenceSubmissions.SingleAsync();
            submission.PublishedToPlaceAtUtc = null;
            await db.SaveChangesAsync();
        }

        var (mayServe, onThePage) = await AskAsync(w);
        Assert.False(mayServe);
        Assert.Equal(0, onThePage);
    }
}
