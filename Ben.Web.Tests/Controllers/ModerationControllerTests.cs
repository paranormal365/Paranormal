using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Authorization;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The moderator's desk (item 186 F5): who may open it, and what deciding actually does.
/// </summary>
/// <remarks>
/// The claim these tests exist to protect is Ben's: nothing a member posts reaches the public feed
/// until somebody or something has looked at it. Everything else here — who holds the role, what a
/// decision records — is in service of that one.
/// </remarks>
public sealed class ModerationControllerTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ModerationController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new ModerationController(factory, new ManualReviewScreener(),
            new FeedLearningService(
                TestMedia.StorageOnDisk(Path.Combine(Path.GetTempPath(), "moderation-tests")),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FeedLearningService>.Instance));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid PostId, Guid AuthorId)>
        SeedPostWithMediaAsync(FeedMediaReviewState state = FeedMediaReviewState.Pending)
    {
        var factory = CreateFactory();
        Guid authorId = Guid.NewGuid(), postId = Guid.NewGuid(), fileId = Guid.NewGuid();

        await using var db = factory.CreateDbContext();
        db.Users.Add(new AppUser
        {
            Id = authorId, UserName = "a@t.com", NormalizedUserName = "A@T.COM",
            Email = "a@t.com", NormalizedEmail = "A@T.COM", DisplayName = "Sarah Mitchell",
            DateCreated = DateTime.UtcNow,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = authorId,
            FileName = "porch.jpg", StoredFileName = "porch.jpg", ContentType = "image/jpeg",
            FileSize = 10, StoragePath = "/tmp/porch.jpg", IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.OrgMessages.Add(new OrgMessage
        {
            Id = postId, AuthorAppUserId = authorId, ChannelType = OrgMessageChannel.PublicFeed,
            Body = "The landing at 3am.", IsPublic = true,
            MediaUploadFileId = fileId, MediaReviewState = state,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        await db.SaveChangesAsync();

        return (factory, postId, authorId);
    }

    // ── Who may moderate ──────────────────────────────────────────────────────

    /// <summary>
    /// The moderation surface is behind the moderator policy, not the administrator one.
    /// </summary>
    /// <remarks>
    /// Structural, because the failure it guards against is silent: an endpoint added here without
    /// an attribute is open to every signed-in account, and nothing else in the suite would notice.
    /// </remarks>
    [Fact]
    public void Every_moderation_endpoint_is_behind_the_moderator_policy()
    {
        var classPolicy = typeof(ModerationController)
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .ToList();

        Assert.Contains(AuthPolicyNames.Moderator, classPolicy);

        var unguarded = typeof(ModerationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            .Select(m => m.Name)
            .ToList();

        Assert.True(unguarded.Count == 0,
            "These moderation endpoints are anonymous, which would publish unscreened media to "
            + "anybody who guessed the URL: " + string.Join(", ", unguarded));
    }

    /// <summary>
    /// A SuperAdmin moderates without holding a second role.
    /// </summary>
    /// <remarks>
    /// Pinned in both places at once — the server handler and the token parser the browser uses —
    /// because two answers to "may this person moderate" would drift, and the visible symptom
    /// would be a menu item that leads to a 403.
    /// </remarks>
    [Fact]
    public void A_super_admin_satisfies_the_moderator_rule_implicitly()
    {
        Assert.Contains(RoleNames.SuperAdmin, RoleNames.Moderators);
        Assert.Contains(RoleNames.Moderator, RoleNames.Moderators);

        // Admin deliberately does NOT moderate: that role grants almost nothing by design, and
        // widening it here would be the unreviewed privilege expansion its own remarks warn about.
        Assert.DoesNotContain(RoleNames.Admin, RoleNames.Moderators);
    }

    [Fact]
    public async Task The_moderator_handler_accepts_both_roles_and_refuses_everybody_else()
    {
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.Moderator })
        {
            var context = Context(role);
            await new ModeratorHandler(UserManagerStub()).HandleAsync(context);
            Assert.True(context.HasSucceeded, $"{role} should satisfy the moderator requirement");
        }

        var ordinary = Context(RoleNames.Admin);
        await new ModeratorHandler(UserManagerStub()).HandleAsync(ordinary);
        Assert.False(ordinary.HasSucceeded, "Admin must not moderate by virtue of being Admin");

        // Never consulted for the role paths above: the handler answers from the principal's own
        // claims first, and only falls back to the database for Entra callers, who have none.
        static UserManager<AppUser> UserManagerStub()
            => new Mock<UserManager<AppUser>>(
                   Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object;

        static AuthorizationHandlerContext Context(string role)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, role),
                 new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));
            return new AuthorizationHandlerContext([new ModeratorRequirement()], principal, null);
        }
    }

    // ── The queue ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_queue_shows_what_is_waiting_with_the_words_it_was_posted_under()
    {
        var (factory, postId, _) = await SeedPostWithMediaAsync();

        var result = await Build(factory, Guid.NewGuid())
            .GetFeedMedia(null, CancellationToken.None);
        var items = (IReadOnlyList<FeedMediaReviewItem>)Assert.IsType<OkObjectResult>(result.Result).Value!;

        var item = Assert.Single(items);
        Assert.Equal(postId, item.PostId);
        Assert.Equal("Sarah Mitchell", item.AuthorDisplayName);
        Assert.Equal("The landing at 3am.", item.Body);
        Assert.Equal(FeedMediaKind.Image, item.Kind);
    }

    [Fact]
    public async Task Approving_publishes_it_and_records_who_decided()
    {
        var (factory, postId, _) = await SeedPostWithMediaAsync();
        var moderatorId = Guid.NewGuid();

        var result = await Build(factory, moderatorId).ReviewFeedMedia(
            postId, new ReviewFeedMediaRequest(Approve: true, "Looks like a hallway."),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        await using var db = factory.CreateDbContext();
        var post = await db.OrgMessages.SingleAsync(m => m.Id == postId);
        Assert.Equal(FeedMediaReviewState.Approved, post.MediaReviewState);
        Assert.Equal(moderatorId, post.MediaReviewedByAppUserId);
        Assert.NotNull(post.MediaReviewedUtc);
        Assert.Equal("Looks like a hallway.", post.MediaReviewNote);
    }

    /// <summary>Holding keeps the file. Moderating is not deleting.</summary>
    [Fact]
    public async Task Holding_keeps_the_file_and_can_be_undone()
    {
        var (factory, postId, _) = await SeedPostWithMediaAsync();
        var moderatorId = Guid.NewGuid();

        await Build(factory, moderatorId).ReviewFeedMedia(
            postId, new ReviewFeedMediaRequest(Approve: false), CancellationToken.None);

        await using (var db = factory.CreateDbContext())
        {
            var post = await db.OrgMessages.SingleAsync(m => m.Id == postId);
            Assert.Equal(FeedMediaReviewState.Held, post.MediaReviewState);
            // The file is still there: a decision can be revisited, and a mistake undone.
            Assert.NotNull(post.MediaUploadFileId);
            Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == post.MediaUploadFileId));
        }

        await Build(factory, moderatorId).ReviewFeedMedia(
            postId, new ReviewFeedMediaRequest(Approve: true), CancellationToken.None);

        await using (var check = factory.CreateDbContext())
            Assert.Equal(FeedMediaReviewState.Approved,
                (await check.OrgMessages.SingleAsync(m => m.Id == postId)).MediaReviewState);
    }

    [Fact]
    public async Task The_summary_counts_each_pile_and_reports_that_screening_is_manual()
    {
        var (factory, _, _) = await SeedPostWithMediaAsync();

        var result = await Build(factory, Guid.NewGuid()).GetSummary(CancellationToken.None);
        var summary = (FeedModerationSummary)Assert.IsType<OkObjectResult>(result.Result).Value!;

        Assert.Equal(1, summary.MediaAwaitingReview);
        Assert.Equal(0, summary.MediaHeld);
        // The honest answer while no classifier is configured — the queue depends on a person.
        Assert.False(summary.ScreeningIsAutomatic);
    }

    // ── The screener contract ─────────────────────────────────────────────────

    /// <summary>
    /// The shipped screener approves NOTHING by itself.
    /// </summary>
    /// <remarks>
    /// The obvious placeholder — wave everything through until a classifier arrives — is the one
    /// implementation that could put the site in the state Ben asked to avoid, and it would do it
    /// silently. This test is what stops somebody writing it.
    /// </remarks>
    [Fact]
    public async Task The_shipped_screener_never_approves_anything_on_its_own()
    {
        var screener = new ManualReviewScreener();

        var verdict = await screener.ScreenAsync("/tmp/anything.jpg", "image/jpeg", CancellationToken.None);

        Assert.NotEqual(FeedMediaReviewState.Approved, verdict.State);
        Assert.Equal(FeedMediaReviewState.Pending, verdict.State);
        Assert.False(screener.IsAutomatic);
    }

    // ── The place archive's queue (2026-09-17 audit) ─────────────────────────────────────────
    //
    // The property these hold: Held is not terminal. Both flag paths — a reader flagging a
    // published field session from the place page, a reader flagging event evidence — set Held,
    // and the public archive serves only Approved. The release endpoints existed and had no
    // caller anywhere, so one flag from one signed-in stranger permanently removed somebody's
    // contribution with no queue, no reviewer and no appeal.

    private static async Task<(Guid SessionId, Guid PlaceId)> SeedPublishedSessionAsync(
        IDbContextFactory<BenDataContext> factory, FeedMediaReviewState state)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        db.Users.Add(new AppUser
        {
            Id = actor, UserName = "contrib@t", Email = "contrib@t",
            DisplayName = "A Contributor", DateCreated = DateTime.UtcNow,
        });
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Bell Witch Cave", City = "Adams", State = "TN",
            Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = actor, PlaceId = placeId,
            LocationLabel = "The back chamber", DeviceModel = "iPhone 17 Pro",
            StartedAt = DateTime.UtcNow.AddDays(-2),
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1),
            MediaReviewState = state,
            MediaReviewNote = state == FeedMediaReviewState.Held ? "Flagged by a reader." : null,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = sessionId,
            UploadFileId = Guid.NewGuid(), RelativePath = "audio/track-1.wav",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        await db.SaveChangesAsync();
        return (sessionId, placeId);
    }

    [Fact]
    public async Task A_held_session_is_listed_so_somebody_can_release_it()
    {
        var factory = CreateFactory();
        await SeedPublishedSessionAsync(factory, FeedMediaReviewState.Held);

        var rows = Assert.IsType<OkObjectResult>(
            (await Build(factory, Guid.NewGuid()).GetArchiveMedia(includeHeld: true, default)).Result).Value
            as IReadOnlyList<ArchiveMediaReviewRow>;

        var row = Assert.Single(rows!);
        Assert.Equal(FeedMediaReviewState.Held, row.State);
        // The reason the flag wrote, which until this queue existed nothing ever read.
        Assert.Equal("Flagged by a reader.", row.Note);
        Assert.Equal("Bell Witch Cave", row.PlaceName);
    }

    [Fact]
    public async Task Approving_a_held_session_puts_its_media_back_on_the_archive()
    {
        var factory = CreateFactory();
        var (sessionId, _) = await SeedPublishedSessionAsync(factory, FeedMediaReviewState.Held);

        await Build(factory, Guid.NewGuid())
            .ReviewArchiveMedia(sessionId, new ReviewFeedMediaRequest(true, "Looked at it, it is fine."), default);

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.FirstAsync(s => s.Id == sessionId);

        // Approved is the ONLY state ArchiveMediaPublication serves, so this assertion is the
        // whole fix: before it, nothing in the codebase could reach this line.
        Assert.Equal(FeedMediaReviewState.Approved, session.MediaReviewState);
        Assert.Equal("Looked at it, it is fine.", session.MediaReviewNote);
    }

    [Fact]
    public async Task A_pending_session_is_waiting_even_when_held_is_not_shown()
    {
        var factory = CreateFactory();
        await SeedPublishedSessionAsync(factory, FeedMediaReviewState.Pending);

        var rows = Assert.IsType<OkObjectResult>(
            (await Build(factory, Guid.NewGuid()).GetArchiveMedia(includeHeld: false, default)).Result).Value
            as IReadOnlyList<ArchiveMediaReviewRow>;

        Assert.Single(rows!);
    }

    private static async Task<Guid> SeedPublishedEvidenceAsync(
        IDbContextFactory<BenDataContext> factory, FeedMediaReviewState state)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        db.Users.Add(new AppUser
        {
            Id = actor, UserName = "guest@t", Email = "guest@t",
            DisplayName = "A Guest", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Host Group", UrlName = "host-group",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", City = "Franklin", State = "TN",
            Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = eventId, OrganizationId = orgId, PlaceId = placeId,
            Title = "A night at the mill", UrlName = "night-at-the-mill",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
        {
            Id = submissionId, OrgCalendarEventId = eventId,
            SubmittedByAppUserId = actor, UploadFileId = Guid.NewGuid(),
            Note = "Something in the doorway",
            PublishedToPlaceAtUtc = DateTime.UtcNow.AddDays(-1),
            ArchiveReviewState = state,
            ArchiveReviewNote = state == FeedMediaReviewState.Held ? "Flagged: that's my kid." : null,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        await db.SaveChangesAsync();
        return submissionId;
    }

    [Fact]
    public async Task Held_event_evidence_is_listed_with_the_reason_it_was_held()
    {
        var factory = CreateFactory();
        await SeedPublishedEvidenceAsync(factory, FeedMediaReviewState.Held);

        var rows = Assert.IsType<OkObjectResult>(
            (await Build(factory, Guid.NewGuid()).GetArchiveEvidence(includeHeld: true, default)).Result).Value
            as IReadOnlyList<ArchiveEvidenceReviewRow>;

        var row = Assert.Single(rows!);
        Assert.Equal("Flagged: that's my kid.", row.Note);
        Assert.Equal("A night at the mill", row.EventTitle);
        Assert.Equal("The Old Mill", row.PlaceName);
        // The photographer's own caption: the same picture reads differently without it.
        Assert.Equal("Something in the doorway", row.Caption);
    }

    [Fact]
    public async Task Approving_held_event_evidence_puts_it_back_on_the_place()
    {
        var factory = CreateFactory();
        var submissionId = await SeedPublishedEvidenceAsync(factory, FeedMediaReviewState.Held);

        await Build(factory, Guid.NewGuid())
            .ReviewArchiveEvidence(submissionId, new ReviewFeedMediaRequest(true, null), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(
            FeedMediaReviewState.Approved,
            (await db.EventEvidenceSubmissions.FirstAsync(e => e.Id == submissionId)).ArchiveReviewState);
    }

    /// <summary>
    /// Holding keeps the file and the note. The one thing this desk cannot do is destroy.
    /// </summary>
    [Fact]
    public async Task Holding_an_approved_session_is_how_a_mistake_is_undone()
    {
        var factory = CreateFactory();
        var (sessionId, _) = await SeedPublishedSessionAsync(factory, FeedMediaReviewState.Approved);

        await Build(factory, Guid.NewGuid())
            .ReviewArchiveMedia(sessionId, new ReviewFeedMediaRequest(false, "Second look: no."), default);

        await using var db = await factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.FirstAsync(s => s.Id == sessionId);

        Assert.Equal(FeedMediaReviewState.Held, session.MediaReviewState);
        Assert.True(await db.FieldSessionUploadFiles.AnyAsync(f => f.FieldSessionUploadId == sessionId));
    }
}
