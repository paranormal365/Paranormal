using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The Under-Review flow (Ben, 2026-08-26): marking a request Under Review messages the group's
/// eligible members with a link to vote; every offered group can read all the submitted
/// materials; the first group to accept wins; the losers and the client are told.
/// </summary>
public class RequestReviewFlowTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F,
        Guid RequestId, Guid ClientId,
        Guid OrgA, Guid OrgAAdmin, Guid OrgAMember,
        Guid OrgB, Guid OrgBAdmin,
        Guid FileId);

    /// <summary>One request, two candidate groups, a photo attached by the client.</summary>
    private static async Task<World> SeedAsync(
        ClientOrgRequestStatus orgAStatus = ClientOrgRequestStatus.Pending,
        ClientOrgRequestStatus orgBStatus = ClientOrgRequestStatus.Pending)
    {
        var f = CreateFactory();
        Guid requestId = Guid.NewGuid(), clientId = Guid.NewGuid(), fileId = Guid.NewGuid();
        Guid orgA = Guid.NewGuid(), orgAAdmin = Guid.NewGuid(), orgAMember = Guid.NewGuid();
        Guid orgB = Guid.NewGuid(), orgBAdmin = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        foreach (var (id, name) in new[] {
            (clientId, "Client"), (orgAAdmin, "A-Admin"), (orgAMember, "A-Member"), (orgBAdmin, "B-Admin") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t.com", DisplayName = name, DateCreated = DateTime.UtcNow });

        db.Organizations.Add(new Organization { Id = orgA, Name = "Alpha", UrlName = $"a-{orgA:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = orgAAdmin });
        db.Organizations.Add(new Organization { Id = orgB, Name = "Bravo", UrlName = $"b-{orgB:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = orgBAdmin });

        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgA, AppUserId = orgAAdmin, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = orgAAdmin },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgA, AppUserId = orgAMember, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = orgAAdmin },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgB, AppUserId = orgBAdmin, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = orgBAdmin });

        // The ordinary member reviews through a Case.Read grant, the world after IH-03.
        db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
        {
            Id = Guid.NewGuid(), OrganizationId = orgA, AppUserId = orgAMember,
            TableName = OrganizationSecurityTable.Case, Actions = OrganizationSecurityAction.Read,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = orgAAdmin,
        });

        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = clientId, Status = ClientRequestStatus.Submitted,
            StreetAddress1 = "13 Elm", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            Description = "Footsteps upstairs every night at 3am.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.ClientRequestOrganizations.AddRange(
            new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = requestId, OrganizationId = orgA, Status = orgAStatus, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId },
            new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = requestId, OrganizationId = orgB, Status = orgBStatus, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });

        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, AppUserId = clientId, FileName = "attic.jpg", ContentType = "image/jpeg",
            FileSize = 1234, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.ClientRequestFiles.Add(new ClientRequestFile
        {
            ClientRequestId = requestId, UploadFileId = fileId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();
        return new World(f, requestId, clientId, orgA, orgAAdmin, orgAMember, orgB, orgBAdmin, fileId);
    }

    private static T WithUser<T>(T ctrl, Guid userId) where T : ControllerBase
    {
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

    private static ClientRequestReviewController Review(World w, Guid userId)
        => WithUser(new ClientRequestReviewController(
            w.F, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F)), userId);

    private static CaseController Cases(World w, Guid userId)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<Ben.Service.Models.Entities.CaseRecord>(It.IsAny<object>()))
              .Returns(new Ben.Service.Models.Entities.CaseRecord { Title = "x" });
        return WithUser(new CaseController(w.F, mapper.Object,
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(w.F),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
            new RequestReviewNotifier(w.F, new PlatformMessageService(w.F)),
            Ben.Web.Tests.TestMailer.Quiet()), userId);
    }

    private static async Task<List<string>> SubjectsToAsync(World w, Guid userId)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await db.UserMessageTos
            .Where(t => t.ToAppUserId == userId)
            .Join(db.UserMessages, t => t.MessageId, m => m.Id, (t, m) => m.MessageSubject!)
            .ToListAsync();
    }

    // ── the message that opens the vote ──────────────────────────────────────

    [Fact]
    public async Task Marking_UnderReview_messages_the_reviewers_with_the_link()
    {
        var w = await SeedAsync();

        var result = await Cases(w, w.OrgAAdmin).UpdateRequestStatus(
            w.OrgA, w.RequestId,
            new UpdateRequestStatusRequest(ClientOrgRequestStatus.UnderReview), default);
        Assert.IsType<NoContentResult>(result);

        // Both the admin and the granted member can open the link, so both are told.
        Assert.Contains(await SubjectsToAsync(w, w.OrgAAdmin), s => s.StartsWith("Vote:"));
        Assert.Contains(await SubjectsToAsync(w, w.OrgAMember), s => s.StartsWith("Vote:"));
        // The other group was not marked Under Review by this org; its people hear nothing.
        Assert.Empty(await SubjectsToAsync(w, w.OrgBAdmin));

        await using var db = await w.F.CreateDbContextAsync();
        var body = await db.UserMessages.Select(m => m.MessageBody!).FirstAsync();
        Assert.Contains($"/organizations/{w.OrgA}/request-review/{w.RequestId}", body);
    }

    [Fact]
    public async Task Resaving_UnderReview_does_not_message_twice()
    {
        var w = await SeedAsync();
        var ctrl = Cases(w, w.OrgAAdmin);
        var req = new UpdateRequestStatusRequest(ClientOrgRequestStatus.UnderReview);

        await ctrl.UpdateRequestStatus(w.OrgA, w.RequestId, req, default);
        await ctrl.UpdateRequestStatus(w.OrgA, w.RequestId, req, default);

        Assert.Single(await SubjectsToAsync(w, w.OrgAAdmin));
    }

    // ── the review page's door ───────────────────────────────────────────────

    [Fact]
    public async Task A_reviewer_sees_the_whole_submission_including_files()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.UnderReview);

        var ok = Assert.IsType<OkObjectResult>(
            (await Review(w, w.OrgAMember).Get(w.OrgA, w.RequestId, default)).Result);
        var detail = Assert.IsType<RequestReviewDetail>(ok.Value);

        Assert.Equal("Footsteps upstairs every night at 3am.", detail.Description);
        var file = Assert.Single(detail.Files);
        Assert.Equal("attic.jpg", file.FileName);
    }

    [Fact]
    public async Task A_member_without_the_grant_is_refused()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.UnderReview);
        var plain = Guid.NewGuid();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = plain, UserName = "p@t.com", DateCreated = DateTime.UtcNow });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgA, AppUserId = plain,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OrgAAdmin,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<ForbidResult>(
            (await Review(w, plain).Get(w.OrgA, w.RequestId, default)).Result);
    }

    [Fact]
    public async Task A_group_that_lost_the_race_can_no_longer_open_the_review()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.Cancelled);

        Assert.IsType<NotFoundResult>(
            (await Review(w, w.OrgAAdmin).Get(w.OrgA, w.RequestId, default)).Result);
    }

    // ── the ballot ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Voting_twice_is_one_changed_ballot_not_two()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.UnderReview);
        var ctrl = Review(w, w.OrgAMember);

        await ctrl.CastVote(w.OrgA, w.RequestId, new CastReviewVoteRequest(true, "yes"), default);
        await ctrl.CastVote(w.OrgA, w.RequestId, new CastReviewVoteRequest(false, "changed my mind"), default);

        await using var db = await w.F.CreateDbContextAsync();
        var vote = await db.ClientRequestReviewVotes.SingleAsync();
        Assert.False(vote.InFavor);
        Assert.Equal("changed my mind", vote.Comment);
    }

    [Fact]
    public async Task Voting_needs_the_request_to_be_UnderReview()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.Viewed);

        var result = await Review(w, w.OrgAMember)
            .CastVote(w.OrgA, w.RequestId, new CastReviewVoteRequest(true, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── the client's materials travel to every candidate group ───────────────

    [Fact]
    public async Task A_reviewing_orgs_member_may_view_the_clients_file()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.UnderReview);
        await using var db = await w.F.CreateDbContextAsync();

        Assert.True(await FileAudienceAccess.CanViewFileAsync(db, w.FileId, w.OrgAMember, default));
        Assert.True(await FileAudienceAccess.CanViewFileAsync(db, w.FileId, w.OrgBAdmin, default));
    }

    [Fact]
    public async Task A_dead_application_takes_the_file_door_with_it()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.Cancelled,
                                orgBStatus: ClientOrgRequestStatus.Rejected);
        await using var db = await w.F.CreateDbContextAsync();

        Assert.False(await FileAudienceAccess.CanViewFileAsync(db, w.FileId, w.OrgAMember, default));
        Assert.False(await FileAudienceAccess.CanViewFileAsync(db, w.FileId, w.OrgBAdmin, default));
    }

    // ── first to accept wins ─────────────────────────────────────────────────

    [Fact]
    public async Task Accepting_cancels_a_rival_mid_vote_and_tells_everyone()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.UnderReview,
                                orgBStatus: ClientOrgRequestStatus.UnderReview);

        var result = await Cases(w, w.OrgAAdmin).AcceptClientRequest(
            w.OrgA, w.RequestId, new AcceptClientRequestAsCaseRequest(null, null), default);
        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await w.F.CreateDbContextAsync();
        var rival = await db.ClientRequestOrganizations
            .SingleAsync(a => a.OrganizationId == w.OrgB);
        // The old code only cancelled Pending — a group mid-vote kept a live application to a
        // request that was already someone's case, and was never told.
        Assert.Equal(ClientOrgRequestStatus.Cancelled, rival.Status);

        Assert.Contains(await SubjectsToAsync(w, w.OrgBAdmin), s => s.StartsWith("No longer available"));
        Assert.Contains(await SubjectsToAsync(w, w.ClientId), s => s.Contains("has taken on your investigation"));

        // The client's message links to the case they can now message their group from.
        var caseId = (await db.Cases.SingleAsync()).Id;
        var clientBody = await db.UserMessageTos.Where(t => t.ToAppUserId == w.ClientId)
            .Join(db.UserMessages, t => t.MessageId, m => m.Id, (t, m) => m.MessageBody!)
            .SingleAsync();
        Assert.Contains($"/my-cases/{caseId}", clientBody);
    }

    [Fact]
    public async Task The_second_group_to_accept_is_refused()
    {
        var w = await SeedAsync(orgAStatus: ClientOrgRequestStatus.UnderReview,
                                orgBStatus: ClientOrgRequestStatus.UnderReview);

        Assert.IsType<CreatedAtActionResult>((await Cases(w, w.OrgAAdmin).AcceptClientRequest(
            w.OrgA, w.RequestId, new AcceptClientRequestAsCaseRequest(null, null), default)).Result);

        var second = await Cases(w, w.OrgBAdmin).AcceptClientRequest(
            w.OrgB, w.RequestId, new AcceptClientRequestAsCaseRequest(null, null), default);

        Assert.IsType<BadRequestObjectResult>(second.Result);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.Cases.CountAsync());   // one home, one case, one group
    }
}
