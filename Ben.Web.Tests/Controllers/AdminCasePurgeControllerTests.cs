using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Deleting a case (item 183): what the preview promises, and what the confirmation refuses.
/// </summary>
/// <remarks>
/// <para><b>Why the delete itself is not exercised here.</b> The purge is built from
/// <c>ExecuteDeleteAsync</c> and <c>ExecuteUpdateAsync</c>, and the in-memory provider implements
/// neither — it throws "not supported by the current database provider" on the first statement.
/// Probed rather than assumed, and adding a SQLite provider to this project is a package restore
/// this machine cannot currently do. The delete ORDER, which is the part that actually goes wrong,
/// is covered instead by <c>CasePurgeCoverageTests</c>, which derives it from the model.</para>
///
/// <para>What is testable here is everything before the transaction, and that is the half a
/// SuperAdmin reads before pressing the button: the counts, the kept-versus-destroyed split, the
/// client notice, and the typed title.</para>
/// </remarks>
public sealed class AdminCasePurgeControllerTests
{
    private static readonly Guid ActingAdminId = Guid.NewGuid();
    private static readonly Guid OrgId         = Guid.NewGuid();
    private static readonly Guid CaseId        = Guid.NewGuid();
    private static readonly Guid ClientId      = Guid.NewGuid();

    private const string CaseTitle = "Henderson, Franklin TN";

    private static IDbContextFactory<BenDataContext> Factory() =>
        new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>A case with one of everything, so a count that is wired to the wrong table shows.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        bool withClient = false, bool withInvestigation = false, bool isPublic = false)
    {
        var factory = Factory();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
        });

        Guid? requestId = null;
        if (withClient)
        {
            db.Users.Add(new AppUser
            {
                Id = ClientId, Email = "client@example.com", UserName = "client@example.com",
                DisplayName = "Dana Henderson", DateCreated = DateTime.UtcNow,
            });
            requestId = Guid.NewGuid();
            db.ClientRequests.Add(new ClientRequest
            {
                Id = requestId.Value, AppUserId = ClientId,
                StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ClientId,
            });
        }

        db.Cases.Add(new Case
        {
            Id = CaseId, OrganizationId = OrgId, Title = CaseTitle,
            CaseYear = 2026, OrgCaseNumber = 42, Status = CaseStatus.Active, IsPublic = isPublic,
            ClientRequestId = requestId,
            StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
        });

        db.CaseNotes.Add(new CaseNote
        {
            Id = Guid.NewGuid(), CaseId = CaseId, Body = "First visit booked.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
        });
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = CaseId, AuthorAppUserId = ActingAdminId, Title = "Knocking",
            EntryType = CaseTimelineEntryType.InvestigatorNote, EventDateTime = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
        });
        db.CaseVotes.Add(new CaseVote
        {
            Id = Guid.NewGuid(), CaseId = CaseId, VoterAppUserId = ActingAdminId,
            VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow,
        });

        if (withInvestigation)
        {
            var investigationId = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = OrgId, CaseId = CaseId, Title = "First visit",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
            });
            // A person's own recording, attached to that visit. It must be counted as KEPT.
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = Guid.NewGuid(), SubmittedByAppUserId = ActingAdminId, DeviceSessionId = Guid.NewGuid(),
                DeviceModel = "iPhone", DocumentUploadFileId = Guid.NewGuid(), InvestigationId = investigationId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
            });
        }

        await db.SaveChangesAsync();
        return factory;
    }

    private static AdminCasePurgeController Build(IDbContextFactory<BenDataContext> factory)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var ctrl = new AdminCasePurgeController(
            new CasePurge(factory, storage.Object, NullLogger<CasePurge>.Instance));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ActingAdminId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static async Task<CasePurgePreview> PreviewAsync(IDbContextFactory<BenDataContext> factory)
    {
        var result = await Build(factory).Preview(CaseId, default);
        return Assert.IsType<CasePurgePreview>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ── what the preview says ────────────────────────────────────────────────

    [Fact]
    public async Task A_case_that_does_not_exist_is_a_plain_not_found()
    {
        var factory = await SeedAsync();

        var result = await Build(factory).Preview(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task The_preview_names_the_case_and_counts_what_dies_with_it()
    {
        var factory = await SeedAsync();

        var preview = await PreviewAsync(factory);

        Assert.Equal(CaseTitle, preview.Title);
        Assert.Equal("#2026-042", preview.CaseReference);
        Assert.Equal("Night Watch", preview.OrganizationName);
        Assert.Equal(CaseStatus.Active, preview.Status);
        Assert.Equal(1, preview.Notes);
        Assert.Equal(1, preview.TimelineEntries);
        Assert.Equal(1, preview.Votes);
    }

    [Fact]
    public async Task A_field_session_on_the_cases_investigation_is_counted_as_kept_not_destroyed()
    {
        // The distinction the whole screen turns on: a recording belongs to the person who made
        // it, and goes back to them rather than dying with somebody else's case.
        var factory = await SeedAsync(withInvestigation: true);

        var preview = await PreviewAsync(factory);

        Assert.Equal(1, preview.Investigations);
        Assert.Equal(1, preview.FieldSessionsDetached);
    }

    [Fact]
    public async Task A_case_with_no_investigations_detaches_nothing()
    {
        // The other half of the pair: a count that was always 1 would pass the test above.
        var factory = await SeedAsync();

        var preview = await PreviewAsync(factory);

        Assert.Equal(0, preview.Investigations);
        Assert.Equal(0, preview.FieldSessionsDetached);
    }

    [Fact]
    public async Task A_feed_post_that_cites_the_case_is_counted_as_unlinked_not_destroyed()
    {
        var factory = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrgMessages.Add(new OrgMessage
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AuthorAppUserId = ActingAdminId,
                ChannelType = OrgMessageChannel.PublicFeed, Body = "We looked into this one.",
                CaseId = CaseId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
            });
            await db.SaveChangesAsync();
        }

        var preview = await PreviewAsync(factory);

        Assert.Equal(1, preview.FeedPostsUnlinked);
    }

    [Fact]
    public async Task Only_the_cases_own_copies_count_as_stored_files()
    {
        // Copy-on-attach mints a fresh file per case; the person's original is merely linked and
        // is not this case's to destroy.
        var factory = await SeedAsync();
        var copyId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = copyId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ActingAdminId,
                FileName = "orb.jpg", StoredFileName = "copy.jpg", ContentType = "image/jpeg", FileSize = 1,
                StoragePath = "cases/c/copy.jpg", CaseCopyOfUploadFileId = Guid.NewGuid(),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = originalId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ActingAdminId,
                FileName = "plan.pdf", StoredFileName = "plan.pdf", ContentType = "application/pdf", FileSize = 1,
                StoragePath = "users/u/plan.pdf",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
            });
            foreach (var fileId in new[] { copyId, originalId })
            {
                db.CaseFiles.Add(new CaseFile
                {
                    Id = Guid.NewGuid(), CaseId = CaseId, UploadFileId = fileId,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = ActingAdminId,
                });
            }
            await db.SaveChangesAsync();
        }

        var preview = await PreviewAsync(factory);

        Assert.Equal(2, preview.Files);          // both links go
        Assert.Equal(1, preview.StoredFiles);    // one set of bytes
    }

    [Fact]
    public async Task A_client_on_the_case_is_reported_by_name()
    {
        var factory = await SeedAsync(withClient: true);

        var preview = await PreviewAsync(factory);

        Assert.Equal("Dana Henderson", preview.ClientName);
    }

    [Fact]
    public async Task A_case_with_no_client_reports_none()
    {
        var factory = await SeedAsync();

        var preview = await PreviewAsync(factory);

        Assert.Null(preview.ClientName);
    }

    [Fact]
    public async Task A_public_case_says_so()
    {
        var factory = await SeedAsync(isPublic: true);

        Assert.True((await PreviewAsync(factory)).IsPublic);
        Assert.False((await PreviewAsync(await SeedAsync())).IsPublic);
    }

    // ── the confirmation ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_typed_title_must_match_exactly()
    {
        var factory = await SeedAsync();

        foreach (var typed in new[] { "", "henderson, franklin tn", "Henderson,  Franklin TN", "Henderson" })
        {
            var result = await Build(factory).Purge(
                CaseId, new AdminCasePurgeController.PurgeCaseRequest(typed), default);

            // Checked on the server as well as in the UI: the screen's job is to make an accident
            // hard, the server's is to make one impossible.
            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Contains(CaseTitle, bad.Value?.ToString());
        }

        // And nothing was touched on the way past.
        await using var db = await factory.CreateDbContextAsync();
        Assert.NotNull(await db.Cases.FindAsync(CaseId));
        Assert.Equal(1, await db.CaseNotes.CountAsync());
    }

    [Fact]
    public async Task Deleting_a_case_that_is_already_gone_says_so_rather_than_throwing()
    {
        var factory = await SeedAsync();

        var result = await Build(factory).Purge(
            Guid.NewGuid(), new AdminCasePurgeController.PurgeCaseRequest(CaseTitle), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("no longer exists", bad.Value?.ToString());
    }
}
