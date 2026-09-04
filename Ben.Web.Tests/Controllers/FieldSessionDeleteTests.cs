using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Deleting your own field session (item 218), against a real relational database with foreign
/// keys enforced.
/// </summary>
/// <remarks>
/// <para><b>Why a real provider.</b> The delete is built from <c>ExecuteDeleteAsync</c>, which the
/// InMemory provider does not implement, and the whole risk here is order: a share link naming one
/// recording holds it with a NoAction key, so sweeping the files first is refused by the database.
/// <see cref="SqliteTestDb"/> (item 219) is what makes that catchable.</para>
///
/// <para><b>The three refusals are the feature.</b> A session recorded for an investigation is the
/// group's, a session a report cites is load-bearing, and a published session is subject to the
/// retraction rule — deleting it is retraction by another door, and that door must not be the way
/// around a paid plan.</para>
/// </remarks>
public sealed class FieldSessionDeleteTests
{
    private static readonly Guid OwnerId   = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();
    private static readonly Guid OrgId     = Guid.NewGuid();
    private static readonly Guid FileType  = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    private sealed record Seeded(Guid DocumentId, Guid MediaId, Guid FileRowId, Guid ShareLinkId);

    private static async Task<Seeded> SeedAsync(
        SqliteTestDb sqlite,
        Guid? investigationId = null,
        DateTime? publishedAtUtc = null,
        bool paidPlan = false,
        bool citedByAReport = false)
    {
        await using var db = await sqlite.NewContextAsync();

        db.Users.Add(new AppUser
        {
            Id = OwnerId, Email = "owner@example.com", UserName = "owner@example.com",
            DisplayName = "Sam Recorder", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = FileType, Name = "Evidence", DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        if (paidPlan)
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = OwnerId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, Status = SubscriptionStatus.Active,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
        }

        if (investigationId is { } investigation)
        {
            db.Investigations.Add(new Investigation
            {
                Id = investigation, OrganizationId = OrgId, Title = "A visit",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
        }

        var documentId = Guid.NewGuid();
        var mediaId    = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = documentId, UploadFileTypeId = FileType, AppUserId = OwnerId,
            FileName = "data.json", StoredFileName = "data.json", ContentType = "application/json",
            FileSize = 1, StoragePath = "users/sam/data.json",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = mediaId, UploadFileTypeId = FileType, AppUserId = OwnerId,
            FileName = "a.m4a", StoredFileName = "a.m4a", ContentType = "audio/mp4",
            FileSize = 2, StoragePath = "users/sam/a.m4a",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = SessionId, SubmittedByAppUserId = OwnerId, DeviceSessionId = Guid.NewGuid(),
            DeviceModel = "iPhone", DocumentUploadFileId = documentId,
            InvestigationId = investigationId, PublishedAtUtc = publishedAtUtc,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var fileRowId = Guid.NewGuid();
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = fileRowId, FieldSessionUploadId = SessionId, UploadFileId = mediaId,
            RelativePath = "media/a.m4a", DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        // A link naming that one recording. Its key to the file row is NoAction, so it has to be
        // swept before the file rows or the database refuses the whole delete.
        var shareLinkId = Guid.NewGuid();
        db.FieldSessionShareLinks.Add(new FieldSessionShareLink
        {
            Id = shareLinkId, Token = Guid.NewGuid().ToString("N"),
            FieldSessionUploadId = SessionId, FieldSessionUploadFileId = fileRowId,
            CreatedByAppUserId = OwnerId, ExpiresUtc = DateTime.UtcNow.AddDays(7),
            DateCreated = DateTime.UtcNow,
        });

        if (citedByAReport)
        {
            var caseId = Guid.NewGuid();
            var reportId = Guid.NewGuid();
            var sectionId = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = OrgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
                Status = CaseStatus.Active,
                StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.CaseReports.Add(new CaseReport
            {
                Id = reportId, CaseId = caseId, Title = "Final report", DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.CaseReportSections.Add(new CaseReportSection
            {
                Id = sectionId, CaseReportId = reportId, Title = "What we recorded", DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.CaseReportSectionFieldSessions.Add(new CaseReportSectionFieldSession
            {
                Id = Guid.NewGuid(), CaseReportSectionId = sectionId, FieldSessionUploadId = SessionId,
            });
        }

        await db.SaveChangesAsync();
        return new Seeded(documentId, mediaId, fileRowId, shareLinkId);
    }

    private static (FieldSessionUploadController Ctrl, Mock<Ben.Data.Common.Interfaces.IFileStorageService> Storage)
        Build(SqliteTestDb sqlite, Guid callerId)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var ctrl = new FieldSessionUploadController(
            sqlite.Factory, storage.Object,
            new Mock<Ben.Data.WebApi.Services.IMediaIngestService>().Object,
            NullLogger<FieldSessionUploadController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, callerId.ToString())], "Bearer")),
                },
            },
        };
        return (ctrl, storage);
    }

    // ── the delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_session_of_your_own_goes_with_its_recordings_links_and_bytes()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);
        var (ctrl, storage) = Build(sqlite, OwnerId);

        var result = await ctrl.DeleteSession(SessionId, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await db.FieldSessionUploads.FindAsync(SessionId));
        Assert.Empty(await db.FieldSessionUploadFiles.ToListAsync());
        Assert.Empty(await db.FieldSessionShareLinks.ToListAsync());
        Assert.Null(await db.UploadFiles.FindAsync(seeded.DocumentId));
        Assert.Null(await db.UploadFiles.FindAsync(seeded.MediaId));

        storage.Verify(s => s.DeleteAsync("users/sam/data.json", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.DeleteAsync("users/sam/a.m4a", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Somebody_elses_session_is_a_plain_not_found()
    {
        // Not Forbid: whether a session exists is not a fact a stranger gets from a status code.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var (ctrl, storage) = Build(sqlite, StrangerId);

        var result = await ctrl.DeleteSession(SessionId, default);

        Assert.IsType<NotFoundResult>(result);
        await using var db = await sqlite.NewContextAsync();
        Assert.NotNull(await db.FieldSessionUploads.FindAsync(SessionId));
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── the three refusals ───────────────────────────────────────────────────

    [Fact]
    public async Task A_session_recorded_for_an_investigation_belongs_to_the_group()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, investigationId: Guid.NewGuid());
        var (ctrl, storage) = Build(sqlite, OwnerId);

        var result = await ctrl.DeleteSession(SessionId, default);

        var refusal = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("investigation", refusal.Value?.ToString());

        await using var db = await sqlite.NewContextAsync();
        Assert.NotNull(await db.FieldSessionUploads.FindAsync(SessionId));
        Assert.Single(await db.FieldSessionUploadFiles.ToListAsync());
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_session_a_case_report_cites_is_left_where_it_is()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, citedByAReport: true);
        var (ctrl, _) = Build(sqlite, OwnerId);

        var result = await ctrl.DeleteSession(SessionId, default);

        var refusal = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("report", refusal.Value?.ToString());

        await using var db = await sqlite.NewContextAsync();
        Assert.NotNull(await db.FieldSessionUploads.FindAsync(SessionId));
    }

    [Fact]
    public async Task Deleting_a_published_session_on_a_free_account_is_retraction_and_is_refused()
    {
        // Publish-then-remove is the exploit the retraction rule exists for. A delete that walked
        // around it would be the same exploit by another door.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, publishedAtUtc: DateTime.UtcNow.AddDays(-1));
        var (ctrl, storage) = Build(sqlite, OwnerId);

        var result = await ctrl.DeleteSession(SessionId, default);

        var refusal = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status402PaymentRequired, refusal.StatusCode);

        await using var db = await sqlite.NewContextAsync();
        Assert.NotNull(await db.FieldSessionUploads.FindAsync(SessionId));
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_published_session_on_a_paid_plan_deletes()
    {
        // The other half of the pair: a refusal that fired on every published session would pass
        // the test above and make the feature useless to the people who pay for it.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite, publishedAtUtc: DateTime.UtcNow.AddDays(-1), paidPlan: true);
        var (ctrl, _) = Build(sqlite, OwnerId);

        var result = await ctrl.DeleteSession(SessionId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await db.FieldSessionUploads.FindAsync(SessionId));
    }

    [Fact]
    public async Task An_unpublished_session_deletes_on_a_free_account()
    {
        // Choosing never to publish is not gaming anything, so it is not gated.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var (ctrl, _) = Build(sqlite, OwnerId);

        Assert.IsType<NoContentResult>(await ctrl.DeleteSession(SessionId, default));
    }

    [Fact]
    public async Task The_place_the_session_was_recorded_at_is_left_alone()
    {
        // Retract leaves the place for the same reason: where a session happened is a fact about
        // the recording, not a consequence of having shared it.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var placeId = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.Places.Add(new Place
            {
                Id = placeId, Name = "The Old Mill", Kind = PlaceKind.PublicLocation,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            var session = await db.FieldSessionUploads.FindAsync(SessionId);
            session!.PlaceId = placeId;
            await db.SaveChangesAsync();
        }
        var (ctrl, _) = Build(sqlite, OwnerId);

        Assert.IsType<NoContentResult>(await ctrl.DeleteSession(SessionId, default));

        await using var verify = await sqlite.NewContextAsync();
        Assert.NotNull(await verify.Places.FindAsync(placeId));
    }
}
