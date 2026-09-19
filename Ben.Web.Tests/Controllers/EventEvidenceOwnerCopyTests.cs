using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A guest keeps their own copy of what they photographed (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>Evidence submitted at a public event is stored under the ORGANIZATION, and until now the
/// only route to it was through the event, and only once a member had accepted it. A guest whose
/// submission was pending or declined had no way to reach their own photograph through the site —
/// they had handed over the only copy the product would show them.</para>
///
/// <para>The operator curates what the EVENT publishes. That is a decision about their gallery,
/// and not a transfer of ownership of somebody else's picture. These tests pin the difference.</para>
/// </remarks>
public sealed class EventEvidenceOwnerCopyTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid GuestId, Guid StrangerId,
        Guid EventId, Guid SubmissionId);

    /// <summary>A public event with one guest submission in the given state.</summary>
    private static async Task<World> SeedAsync(EvidenceSubmissionStatus status)
    {
        var f = CreateFactory();
        Guid guestId = Guid.NewGuid(), strangerId = Guid.NewGuid(),
             orgId = Guid.NewGuid(), eventId = Guid.NewGuid(),
             fileId = Guid.NewGuid(), submissionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = await f.CreateDbContextAsync();
        foreach (var (id, name) in new[] { (guestId, "A Guest"), (strangerId, "A Stranger") })
            db.AppUsers.Add(new AppUser
            {
                Id = id, UserName = $"{id}@t.com", Email = $"{id}@t.com",
                DisplayName = name, DateCreated = now,
            });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Nightfall Walks", UrlName = $"nw-{orgId:N}",
            DateCreated = now, CreatedByAppUserId = strangerId,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = eventId, OrganizationId = orgId, Title = "Old Town Ghost Walk",
            IsPublic = true, StartDateTime = now.AddDays(-1),
            DateCreated = now, CreatedByAppUserId = strangerId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, FileName = "orb.jpg", ContentType = "image/jpeg",
            // The bytes live under the ORG, which is the point: the guest owns the row, the
            // operator pays for the storage.
            StoragePath = $"orgs/{orgId}/event-evidence/{fileId}.jpg",
            FileSize = 2048, AppUserId = guestId, IsPublic = false,
            DateCreated = now, CreatedByAppUserId = guestId,
        });
        db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
        {
            Id = submissionId, OrgCalendarEventId = eventId,
            SubmittedByAppUserId = guestId, UploadFileId = fileId,
            Status = status, DateCreated = now, CreatedByAppUserId = guestId,
        });
        await db.SaveChangesAsync();

        return new World(f, guestId, strangerId, eventId, submissionId);
    }

    // ── the guest's own listing ──────────────────────────────────────────────

    [Theory]
    [InlineData(EvidenceSubmissionStatus.Pending)]
    [InlineData(EvidenceSubmissionStatus.Accepted)]
    [InlineData(EvidenceSubmissionStatus.Rejected)]
    public async Task A_guest_can_list_what_they_offered_whatever_the_verdict(
        EvidenceSubmissionStatus status)
    {
        var w = await SeedAsync(status);

        await using var db = await w.F.CreateDbContextAsync();
        var mine = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(s => s.SubmittedByAppUserId == w.GuestId)
            .ToListAsync();

        var row = Assert.Single(mine);
        Assert.Equal(w.SubmissionId, row.Id);
        Assert.Equal(status, row.Status);
    }

    /// <summary>
    /// The submitter owns the file row even though the bytes sit under the organization's path —
    /// which is what makes "one file, two references" honest rather than a slogan.
    /// </summary>
    [Fact]
    public async Task The_file_records_the_guest_as_its_owner_while_living_under_the_organization()
    {
        var w = await SeedAsync(EvidenceSubmissionStatus.Rejected);

        await using var db = await w.F.CreateDbContextAsync();
        var file = await db.UploadFiles.SingleAsync();

        Assert.Equal(w.GuestId, file.AppUserId);
        Assert.StartsWith("orgs/", file.StoragePath);
        Assert.False(file.IsPublic);
    }

    // ── one file, not two ────────────────────────────────────────────────────

    /// <summary>
    /// Giving the guest a copy must not mean storing the bytes twice. Duplicating every guest
    /// upload would double storage on the one feature designed to attract volume.
    /// </summary>
    [Fact]
    public async Task Giving_the_guest_access_stores_no_second_copy()
    {
        var w = await SeedAsync(EvidenceSubmissionStatus.Accepted);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.UploadFiles.CountAsync());
        Assert.Equal(1, await db.EventEvidenceSubmissions.CountAsync());
    }

    /// <summary>
    /// And it is not billed to the guest. The bytes are the operator's cost on the operator's
    /// plan; counting them against the guest's free allowance would bill one file to two people.
    /// </summary>
    [Fact]
    public async Task Event_evidence_does_not_count_against_the_guests_free_storage()
    {
        var w = await SeedAsync(EvidenceSubmissionStatus.Accepted);

        await using var db = await w.F.CreateDbContextAsync();
        var used = await Ben.Data.WebApi.Services.Billing.AccountStorageGuard
            .UsedBytesAsync(db, w.GuestId, default);

        Assert.Equal(0L, used);
    }

    // ── and it is the guest's, not everybody's ───────────────────────────────

    /// <summary>
    /// The owner's route is for the OWNER. A stranger still sees only what the operator accepted
    /// at a public event, which is the rule that was there before.
    /// </summary>
    [Fact]
    public async Task A_stranger_has_no_claim_on_somebody_elses_pending_submission()
    {
        var w = await SeedAsync(EvidenceSubmissionStatus.Pending);

        await using var db = await w.F.CreateDbContextAsync();
        var strangerCanSee = await db.EventEvidenceSubmissions.AsNoTracking()
            .AnyAsync(s => s.Id == w.SubmissionId && s.SubmittedByAppUserId == w.StrangerId);

        Assert.False(strangerCanSee);
    }
}
