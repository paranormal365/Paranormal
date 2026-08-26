using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 111: attendees submit, a member accepts, and only acceptance opens the bytes.
/// </summary>
/// <remarks>
/// The privacy edges matter most here because the submitters are strangers: a stranger who did
/// NOT attend must not get the door, a pending submission must not be public, and a private
/// event's accepted evidence must stay unreachable anonymously — each of those failing is a
/// stranger's upload (or a group's event) leaking the wrong way.
/// </remarks>
public sealed class EventEvidenceTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid OrgId, Guid EventId,
        Guid MemberId, Guid AttendeeId, Guid StrangerId);

    private static async Task<World> SeedAsync(bool eventIsPublic = true)
    {
        var f = Factory();
        var orgId = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var member = Guid.NewGuid(); var attendee = Guid.NewGuid(); var stranger = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        foreach (var (id, name) in new[] { (member, "m"), (attendee, "a"), (stranger, "s") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{name}@t.com", Email = $"{name}@t.com", DisplayName = name, DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = member });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = member,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = member,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = eventId, OrganizationId = orgId, Title = "Open night",
            IsPublic = eventIsPublic,
            StartDateTime = DateTime.UtcNow.AddDays(-1), EndDateTime = DateTime.UtcNow.AddDays(-1).AddHours(3),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = member,
        });
        db.EventAttendanceInvites.Add(new EventAttendanceInvite
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = eventId, Email = "a@t.com",
            DateConfirmed = DateTime.UtcNow.AddDays(-2), ConfirmedByAppUserId = attendee,
            DateExpires = DateTime.UtcNow.AddDays(30),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = attendee,
        });
        await db.SaveChangesAsync();
        // Reviewing a submission decides whether an attendee's file becomes public, so it asks for
        // the Calendar grant now rather than bare membership. The suite's subject is the review
        // flow, so its member is seeded able to review.
        await TestSeeds.GrantAsync(f, orgId, member, OrganizationSecurityTable.OrgCalendar,
            OrganizationSecurityAction.Read | OrganizationSecurityAction.Update);
        return new World(f, orgId, eventId, member, attendee, stranger);
    }

    /// <summary>An active member of the group holding no grant of any kind.</summary>
    private static async Task<Guid> PlainMemberAsync(World w)
    {
        var plainId = Guid.NewGuid();
        await using var db = await w.F.CreateDbContextAsync();
        db.Users.Add(new AppUser
        {
            Id = plainId, UserName = "plain@t.com", Email = "plain@t.com",
            DisplayName = "Plain", DateCreated = DateTime.UtcNow,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrgId, AppUserId = plainId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.MemberId,
        });
        await db.SaveChangesAsync();
        return plainId;
    }

    /// <summary>
    /// Belonging to the group is not permission to publish a stranger's upload.
    /// </summary>
    /// <remarks>
    /// Accepting a submission at a public event makes the attendee's file public. That was gated on
    /// bare active membership, so every member of the group could publish an attendee's photo in
    /// the group's name regardless of what they had been granted — found in the sweep of
    /// 2026-08-26 and gated on the Calendar area, where events live.
    /// </remarks>
    [Fact]
    public async Task A_member_without_the_calendar_grant_cannot_decide_a_submission()
    {
        var w = await SeedAsync();
        var plainId = await PlainMemberAsync(w);
        var submissionId = await SubmitAsync(w, w.AttendeeId);

        var result = await Controller(w.F, plainId)
            .Review(w.OrgId, submissionId, new EventEvidenceController.ReviewEvidenceRequest(true, null), default);

        Assert.IsType<ForbidResult>(result.Result);

        await using var db = await w.F.CreateDbContextAsync();
        var submission = await db.EventEvidenceSubmissions.Include(x => x.UploadFile)
            .SingleAsync(x => x.Id == submissionId);
        Assert.Equal(EvidenceSubmissionStatus.Pending, submission.Status);
        Assert.False(submission.UploadFile.IsPublic);
    }

    /// <summary>And cannot read the queue of what is waiting to be decided.</summary>
    [Fact]
    public async Task A_member_without_the_calendar_grant_cannot_read_the_queue()
    {
        var w = await SeedAsync();
        var plainId = await PlainMemberAsync(w);
        await SubmitAsync(w, w.AttendeeId);

        var result = await Controller(w.F, plainId).Queue(w.OrgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    private static EventEvidenceController Controller(IDbContextFactory<BenDataContext> f, Guid? userId,
        bool isSuperAdmin = false)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(x => x.OrgFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((o, n) => $"/tmp/{o}/{n}");

        var claims = userId is { } id
            ? new ClaimsIdentity(
                isSuperAdmin
                    ? [new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                       new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin)]
                    : [new Claim(ClaimTypes.NameIdentifier, id.ToString())],
                "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role)
            : new ClaimsIdentity();

        return new EventEvidenceController(f, storage.Object, new PlatformMessageService(f), Ben.Web.Tests.TestMedia.Ingest(), Ben.Web.Tests.TestMedia.Stripper(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f))
        {
            ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(claims) } }
        };
    }

    private static IFormFile SmallFile() =>
        new FormFile(new MemoryStream("evp"u8.ToArray()), 0, 3, "file", "whisper.wav")
        { Headers = new HeaderDictionary(), ContentType = "audio/wav" };

    private static async Task<Guid> SubmitAsync(World w, Guid asUser)
    {
        var result = await Controller(w.F, asUser).Submit(w.EventId, "heard at 2am", SmallFile(), default);
        var record = Assert.IsType<EventEvidenceController.EvidenceSubmissionRecord>(
            ((OkObjectResult)result.Result!).Value);
        return record.Id;
    }

    // ── the door ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_confirmed_attendee_can_submit_and_a_stranger_cannot()
    {
        var w = await SeedAsync();

        await SubmitAsync(w, w.AttendeeId);   // succeeds or throws the assert inside

        var refused = await Controller(w.F, w.StrangerId).Submit(w.EventId, null, SmallFile(), default);
        Assert.IsType<BadRequestObjectResult>(refused.Result);
    }

    [Fact]
    public async Task A_member_who_attended_uses_the_same_door()
    {
        var w = await SeedAsync();
        await SubmitAsync(w, w.MemberId);
    }

    [Fact]
    public async Task A_private_event_takes_no_visitor_evidence()
    {
        var w = await SeedAsync(eventIsPublic: false);

        var refused = await Controller(w.F, w.AttendeeId).Submit(w.EventId, null, SmallFile(), default);
        Assert.IsType<BadRequestObjectResult>(refused.Result);
    }

    // ── the review ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Acceptance_marks_the_file_public_and_messages_the_submitter()
    {
        var w = await SeedAsync();
        var id = await SubmitAsync(w, w.AttendeeId);

        var result = await Controller(w.F, w.MemberId).Review(w.OrgId, id,
            new EventEvidenceController.ReviewEvidenceRequest(true, null), default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.EventEvidenceSubmissions.Include(s => s.UploadFile).SingleAsync();
        Assert.Equal(EvidenceSubmissionStatus.Accepted, sub.Status);
        Assert.True(sub.UploadFile.IsPublic);
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.AttendeeId));
    }

    [Fact]
    public async Task Rejection_requires_a_reason_and_keeps_the_file_private()
    {
        var w = await SeedAsync();
        var id = await SubmitAsync(w, w.AttendeeId);
        var ctrl = Controller(w.F, w.MemberId);

        var bare = await ctrl.Review(w.OrgId, id,
            new EventEvidenceController.ReviewEvidenceRequest(false, null), default);
        Assert.IsType<BadRequestObjectResult>(bare.Result);

        var reasoned = await ctrl.Review(w.OrgId, id,
            new EventEvidenceController.ReviewEvidenceRequest(false, "Too much wind noise to assess."), default);
        Assert.IsType<OkObjectResult>(reasoned.Result);

        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.EventEvidenceSubmissions.Include(s => s.UploadFile).SingleAsync();
        Assert.Equal(EvidenceSubmissionStatus.Rejected, sub.Status);
        Assert.False(sub.UploadFile.IsPublic);
    }

    [Fact]
    public async Task A_superadmin_who_is_not_a_member_can_review()
    {
        // Same bypass rule as CaseFileController — see its SuperAdmin test for the 2026-08-22 bug.
        var w = await SeedAsync();
        var id = await SubmitAsync(w, w.AttendeeId);

        var result = await Controller(w.F, w.StrangerId, isSuperAdmin: true).Review(w.OrgId, id,
            new EventEvidenceController.ReviewEvidenceRequest(true, null), default);
        Assert.IsNotType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task A_stranger_cannot_review()
    {
        var w = await SeedAsync();
        var id = await SubmitAsync(w, w.AttendeeId);

        var result = await Controller(w.F, w.StrangerId).Review(w.OrgId, id,
            new EventEvidenceController.ReviewEvidenceRequest(true, null), default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── the public record ─────────────────────────────────────────────────────

    [Fact]
    public async Task Only_accepted_submissions_appear_on_the_anonymous_list()
    {
        var w = await SeedAsync();
        var accepted = await SubmitAsync(w, w.AttendeeId);
        await SubmitAsync(w, w.AttendeeId);   // stays pending

        await Controller(w.F, w.MemberId).Review(w.OrgId, accepted,
            new EventEvidenceController.ReviewEvidenceRequest(true, null), default);

        var result = await Controller(w.F, userId: null).Accepted(w.EventId, default);
        var rows = Assert.IsAssignableFrom<IEnumerable<EventEvidenceController.EvidenceSubmissionRecord>>(
            ((OkObjectResult)result.Result!).Value).ToList();

        Assert.Single(rows);
        Assert.Equal(accepted, rows[0].Id);
    }

    /// <summary>The anonymous byte path opens with acceptance and only with it.</summary>
    [Fact]
    public async Task Bytes_serve_anonymously_only_once_accepted()
    {
        var w = await SeedAsync();
        var id = await SubmitAsync(w, w.AttendeeId);

        // give the row blob bytes so the byte path has something to serve without a disk
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var file = await db.UploadFiles.SingleAsync();
            file.StoragePath = null;
            file.FileData = [1, 2, 3];
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>(await Controller(w.F, null).FileBytes(w.EventId, id, default));

        await Controller(w.F, w.MemberId).Review(w.OrgId, id,
            new EventEvidenceController.ReviewEvidenceRequest(true, null), default);

        Assert.IsType<FileContentResult>(await Controller(w.F, null).FileBytes(w.EventId, id, default));
    }

    [Fact]
    public async Task A_private_events_accepted_evidence_stays_unreachable_anonymously()
    {
        var w = await SeedAsync(eventIsPublic: false);

        // seed a submission directly — the door refuses private events, but rows could predate
        // an event being made private again, and the gate must hold regardless of how they got in
        Guid subId = Guid.NewGuid();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var file = new UploadFile
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = w.AttendeeId,
                FileName = "x.wav", StoredFileName = "x", ContentType = "audio/wav", FileSize = 3,
                FileData = [1, 2, 3], IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AttendeeId,
            };
            db.UploadFiles.Add(file);
            db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
            {
                Id = subId, OrgCalendarEventId = w.EventId, SubmittedByAppUserId = w.AttendeeId,
                UploadFileId = file.Id, Status = EvidenceSubmissionStatus.Accepted,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AttendeeId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Controller(w.F, null).Accepted(w.EventId, default)).Result);
        Assert.IsType<NotFoundResult>(await Controller(w.F, null).FileBytes(w.EventId, subId, default));
    }
}
