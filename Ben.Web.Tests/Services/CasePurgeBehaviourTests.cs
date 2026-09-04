using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The case purge actually running, against a real relational database with foreign keys
/// enforced (item 183).
/// </summary>
/// <remarks>
/// <para><b>Why these are not in the controller test class.</b> The purge is built from
/// <c>ExecuteDeleteAsync</c> and <c>ExecuteUpdateAsync</c>, which the InMemory provider does not
/// implement, so the tests that watch a delete happen need <see cref="SqliteTestDb"/>. The
/// preview and the typed-title refusal are covered next door on InMemory, where they belong.</para>
///
/// <para><b>What a failure here means.</b> Foreign keys are on. A purge that deletes rows in the
/// wrong order does not quietly leave orphans — the database refuses it, the transaction rolls
/// back, and <c>PurgeAsync</c> returns the refusal as its error. That is the exact failure the
/// group purge hit on production twice, and it is now catchable before a release rather than in
/// front of Ben.</para>
/// </remarks>
public sealed class CasePurgeBehaviourTests
{
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid OwnerId  = Guid.NewGuid();
    private static readonly Guid OrgId    = Guid.NewGuid();
    private static readonly Guid CaseId   = Guid.NewGuid();
    private static readonly Guid FileType = Guid.NewGuid();

    private const string CaseTitle = "Henderson, Franklin TN";

    private sealed record Seeded(Guid InvestigationId, Guid SessionId, Guid CopyFileId, Guid OriginalFileId, Guid PostId);

    /// <summary>
    /// A case with one of everything that has to die, and one of everything that has to survive.
    /// Parent rows are real because the database insists — which is the point of using it.
    /// </summary>
    private static async Task<Seeded> SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        db.Users.Add(new AppUser
        {
            Id = AdminId, Email = "admin@example.com", UserName = "admin@example.com",
            DisplayName = "The Admin", DateCreated = DateTime.UtcNow,
        });
        db.Users.Add(new AppUser
        {
            Id = OwnerId, Email = "owner@example.com", UserName = "owner@example.com",
            DisplayName = "Sam Recorder", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = FileType, Name = "Evidence", DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.Cases.Add(new Case
        {
            Id = CaseId, OrganizationId = OrgId, Title = CaseTitle,
            CaseYear = 2026, OrgCaseNumber = 42, Status = CaseStatus.Active,
            StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        // ── dies with the case ────────────────────────────────────────────────
        db.CaseNotes.Add(new CaseNote
        {
            Id = Guid.NewGuid(), CaseId = CaseId, AuthorAppUserId = AdminId, Body = "First visit booked.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        var entryId = Guid.NewGuid();
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = entryId, CaseId = CaseId, AuthorAppUserId = AdminId, Title = "Knocking",
            EntryType = CaseTimelineEntryType.InvestigatorNote, EventDateTime = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.CaseVotes.Add(new CaseVote
        {
            Id = Guid.NewGuid(), CaseId = CaseId, VoterAppUserId = AdminId,
            VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow,
        });

        var investigationId = Guid.NewGuid();
        db.Investigations.Add(new Investigation
        {
            Id = investigationId, OrganizationId = OrgId, CaseId = CaseId, Title = "First visit",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = investigationId, AppUserId = AdminId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        // ── the case's own copy, and the original it was copied from ─────────
        var originalId = Guid.NewGuid();
        var copyId     = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = originalId, UploadFileTypeId = FileType, AppUserId = OwnerId,
            FileName = "orb.jpg", StoredFileName = "orb.jpg", ContentType = "image/jpeg", FileSize = 3,
            StoragePath = "users/owner/orb.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = copyId, UploadFileTypeId = FileType, AppUserId = OwnerId,
            FileName = "orb.jpg", StoredFileName = "copy.jpg", ContentType = "image/jpeg", FileSize = 3,
            StoragePath = "cases/case/copy.jpg", CaseCopyOfUploadFileId = originalId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.CaseFiles.Add(new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = CaseId, UploadFileId = copyId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        // ── survives, whole: somebody's own recording ────────────────────────
        var documentId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = documentId, UploadFileTypeId = FileType, AppUserId = OwnerId,
            FileName = "data.json", StoredFileName = "data.json", ContentType = "application/json", FileSize = 1,
            StoragePath = "users/owner/data.json",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        var sessionId = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = OwnerId, DeviceSessionId = Guid.NewGuid(),
            DeviceModel = "iPhone", DocumentUploadFileId = documentId, InvestigationId = investigationId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        // ── survives, unlinked: a feed post that cites the case ──────────────
        var postId = Guid.NewGuid();
        db.OrgMessages.Add(new OrgMessage
        {
            Id = postId, OrganizationId = OrgId, AuthorAppUserId = OwnerId,
            ChannelType = OrgMessageChannel.PublicFeed, Body = "We looked into this one.",
            CaseId = CaseId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return new Seeded(investigationId, sessionId, copyId, originalId, postId);
    }

    private static (CasePurge Purge, Mock<Ben.Data.Common.Interfaces.IFileStorageService> Storage) Build(SqliteTestDb sqlite)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        return (new CasePurge(sqlite.Factory, storage.Object, NullLogger<CasePurge>.Instance), storage);
    }

    [Fact]
    public async Task The_case_and_everything_that_exists_only_because_of_it_is_gone()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var (purge, _) = Build(sqlite);

        var (result, error) = await purge.PurgeAsync(CaseId, CaseTitle, AdminId);

        // A refusal here is the database rejecting the delete ORDER — the failure this whole
        // test class exists to catch — so the message travels into the assertion.
        Assert.Null(error);
        Assert.NotNull(result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await db.Cases.FindAsync(CaseId));
        Assert.Empty(await db.CaseNotes.ToListAsync());
        Assert.Empty(await db.CaseTimelineEntries.ToListAsync());
        Assert.Empty(await db.CaseVotes.ToListAsync());
        Assert.Empty(await db.CaseFiles.ToListAsync());
        Assert.Empty(await db.Investigations.ToListAsync());
        Assert.Empty(await db.InvestigationAttendees.ToListAsync());
    }

    [Fact]
    public async Task The_recording_goes_back_to_the_person_who_made_it()
    {
        // The rule the design turns on: a field session is not the case's to destroy.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var (purge, _) = Build(sqlite);

        var (result, error) = await purge.PurgeAsync(CaseId, CaseTitle, AdminId);
        Assert.Null(error);

        await using var db = await sqlite.NewContextAsync();
        var session = await db.FieldSessionUploads.FindAsync(seeded.SessionId);
        Assert.NotNull(session);
        Assert.Null(session!.InvestigationId);          // a personal session is exactly this
        Assert.Equal(OwnerId, session.SubmittedByAppUserId);
        Assert.NotNull(await db.UploadFiles.FindAsync(session.DocumentUploadFileId));
        Assert.Equal(1, result!.FieldSessionsDetached);
    }

    [Fact]
    public async Task A_feed_post_survives_and_simply_stops_naming_the_case()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var (purge, _) = Build(sqlite);

        Assert.Null((await purge.PurgeAsync(CaseId, CaseTitle, AdminId)).Error);

        await using var db = await sqlite.NewContextAsync();
        var post = await db.OrgMessages.FindAsync(seeded.PostId);
        Assert.NotNull(post);
        Assert.Null(post!.CaseId);
        Assert.Equal("We looked into this one.", post.Body);
    }

    [Fact]
    public async Task Only_the_cases_own_copy_is_destroyed_and_the_original_is_left_alone()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var (purge, storage) = Build(sqlite);

        var (result, error) = await purge.PurgeAsync(CaseId, CaseTitle, AdminId);
        Assert.Null(error);

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await db.UploadFiles.FindAsync(seeded.CopyFileId));
        Assert.NotNull(await db.UploadFiles.FindAsync(seeded.OriginalFileId));

        storage.Verify(s => s.DeleteAsync("cases/case/copy.jpg", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.DeleteAsync("users/owner/orb.jpg", It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, result!.Files);
    }

    [Fact]
    public async Task The_cases_storage_directory_is_removed()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var (purge, storage) = Build(sqlite);

        Assert.Null((await purge.PurgeAsync(CaseId, CaseTitle, AdminId)).Error);

        storage.Verify(s => s.DeleteDirectoryAsync($"cases/{CaseId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_mistyped_title_deletes_nothing_at_all()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var (purge, storage) = Build(sqlite);

        var (result, error) = await purge.PurgeAsync(CaseId, "henderson, franklin tn", AdminId);

        Assert.Null(result);
        Assert.Contains(CaseTitle, error);

        await using var db = await sqlite.NewContextAsync();
        Assert.NotNull(await db.Cases.FindAsync(CaseId));
        Assert.Equal(1, await db.CaseNotes.CountAsync());
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.DeleteDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_result_reports_what_it_actually_removed()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var (purge, _) = Build(sqlite);

        var (result, _) = await purge.PurgeAsync(CaseId, CaseTitle, AdminId);

        Assert.NotNull(result);
        Assert.Equal(CaseTitle, result!.Title);
        Assert.Equal("#2026-042", result.CaseReference);
        Assert.Equal(1, result.TimelineEntries);
        Assert.Equal(1, result.Investigations);
        Assert.Equal(1, result.FieldSessionsDetached);
    }

    [Fact]
    public async Task A_case_with_nothing_in_it_deletes_cleanly()
    {
        // The duplicate this feature exists for. An empty case must not need a fixture's worth of
        // rows present for the purge to survive its own queries.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = AdminId, Email = "admin@example.com", UserName = "admin@example.com",
                DisplayName = "The Admin", DateCreated = DateTime.UtcNow,
            });
            db.Organizations.Add(new Organization
            {
                Id = OrgId, Name = "Night Watch", UrlName = "night-watch",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            db.Cases.Add(new Case
            {
                Id = CaseId, OrganizationId = OrgId, Title = "Opened twice by mistake",
                CaseYear = 2026, OrgCaseNumber = 43, Status = CaseStatus.Proposed,
                StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
            });
            await db.SaveChangesAsync();
        }
        var (purge, _) = Build(sqlite);

        var (result, error) = await purge.PurgeAsync(CaseId, "Opened twice by mistake", AdminId);

        Assert.Null(error);
        Assert.NotNull(result);
        await using var verify = await sqlite.NewContextAsync();
        Assert.Empty(await verify.Cases.ToListAsync());
    }
}
