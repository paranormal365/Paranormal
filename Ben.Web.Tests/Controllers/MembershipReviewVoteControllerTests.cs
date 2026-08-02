using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>Tests for the vote-specific endpoints in OrganizationMembershipRequestController.</summary>
public class MembershipReviewVoteControllerTests
{
    private static OrganizationMembershipRequestController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid userId,
        bool isSuperAdmin = false,
        bool hasPermission = true)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<OrganizationMembershipRequestRecord>(It.IsAny<object>()))
              .Returns<object>(src =>
              {
                  if (src is OrganizationMembershipRequest r)
                      return new OrganizationMembershipRequestRecord
                      {
                          Id = r.Id, OrganizationId = r.OrganizationId,
                          OrganizationName = string.Empty, AppUserId = r.AppUserId,
                          ApplicantDisplayName = string.Empty, ApplicantEmail = string.Empty,
                          Status = r.Status,
                          IsUnderReview = r.IsUnderReview, VoteDeadline = r.VoteDeadline,
                      };
                  return new OrganizationMembershipRequestRecord
                  {
                      OrganizationName = string.Empty, ApplicantDisplayName = string.Empty, ApplicantEmail = string.Empty,
                  };
              });
        mapper.Setup(m => m.Map<MembershipReviewVoteRecord>(It.IsAny<object>()))
              .Returns<object>(src =>
              {
                  if (src is MembershipReviewVote v)
                      return new MembershipReviewVoteRecord
                      {
                          Id = v.Id, OrganizationMembershipRequestId = v.OrganizationMembershipRequestId,
                          VoterAppUserId = v.VoterAppUserId, VoteType = v.VoteType,
                          Comment = v.Comment, DateVoted = v.DateVoted,
                      };
                  return new MembershipReviewVoteRecord();
              });
        mapper.Setup(m => m.Map<IEnumerable<MembershipReviewVoteRecord>>(It.IsAny<object>()))
              .Returns<object>(src =>
              {
                  if (src is not IEnumerable<MembershipReviewVote> votes) return [];
                  return votes.Select(v => new MembershipReviewVoteRecord
                  {
                      Id = v.Id, OrganizationMembershipRequestId = v.OrganizationMembershipRequestId,
                      VoterAppUserId = v.VoterAppUserId, VoteType = v.VoteType,
                      Comment = v.Comment, DateVoted = v.DateVoted,
                  });
              });

        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                                              It.IsAny<OrganizationSecurityTable>(),
                                              It.IsAny<OrganizationSecurityAction>(),
                                              It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasPermission);

        var claims = new List<Claim>
        {
            new("app_user_id", userId.ToString()),
        };
        if (isSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));

        var ctrl = new OrganizationMembershipRequestController(
            factory, mapper.Object, security.Object, Mock.Of<IAuditLogService>());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
            },
        };
        return ctrl;
    }

    private static async Task<Guid> SeedVoterAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var voterId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser { Id = voterId, UserName = $"{voterId}@test.com", NormalizedUserName = voterId.ToString().ToUpperInvariant() });
        await db.SaveChangesAsync();
        return voterId;
    }

    private static async Task<(Organization org, OrganizationMembershipRequest req)> SeedRequestAsync(
        IDbContextFactory<BenDataContext> factory,
        OrganizationMembershipRequestStatus status = OrganizationMembershipRequestStatus.Pending,
        bool isUnderReview = false,
        DateTime? voteDeadline = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Org", UrlName = $"org-{Guid.NewGuid():N}",
            IsAcceptingApplications = true, CreatedByAppUserId = Guid.NewGuid(),
        };
        var req = new OrganizationMembershipRequest
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            AppUserId = Guid.NewGuid(), Status = status,
            IsUnderReview = isUnderReview, VoteDeadline = voteDeadline,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = org.CreatedByAppUserId,
        };
        db.Organizations.Add(org);
        db.OrganizationMembershipRequests.Add(req);
        await db.SaveChangesAsync();
        return (org, req);
    }

    // ── OpenVote ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenVote_ValidPendingRequest_SetsIsUnderReview()
    {
        var factory = TestDbFactory.Create();
        var userId  = Guid.NewGuid();
        var (org, req) = await SeedRequestAsync(factory);

        var futureDeadline = DateTime.UtcNow.AddDays(7);
        var result = await Build(factory, userId)
            .OpenVote(org.Id, req.Id, new OpenVoteRequest(futureDeadline), CancellationToken.None);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var resp = Assert.IsType<OrganizationMembershipRequestRecord>(ok.Value);
        Assert.True(resp.IsUnderReview);
    }

    [Fact]
    public async Task OpenVote_PastDeadline_Returns400()
    {
        var factory = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory);

        var result = await Build(factory, Guid.NewGuid())
            .OpenVote(org.Id, req.Id, new OpenVoteRequest(DateTime.UtcNow.AddDays(-1)), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OpenVote_AlreadyAccepted_Returns400()
    {
        var factory = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory, OrganizationMembershipRequestStatus.Accepted);

        var result = await Build(factory, Guid.NewGuid())
            .OpenVote(org.Id, req.Id, new OpenVoteRequest(DateTime.UtcNow.AddDays(3)), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OpenVote_RequestNotFound_Returns404()
    {
        var factory = TestDbFactory.Create();
        var (org, _) = await SeedRequestAsync(factory);

        var result = await Build(factory, Guid.NewGuid())
            .OpenVote(org.Id, Guid.NewGuid(), new OpenVoteRequest(DateTime.UtcNow.AddDays(3)), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── CastVote ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CastVote_NewVote_Returns200()
    {
        var factory    = TestDbFactory.Create();
        var userId     = await SeedVoterAsync(factory);
        var (org, req) = await SeedRequestAsync(factory, isUnderReview: true,
            voteDeadline: DateTime.UtcNow.AddDays(7));

        var result = await Build(factory, userId)
            .CastVote(org.Id, req.Id, new CastVoteRequest(MembershipVoteType.Approve, "Looks good"), CancellationToken.None);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var vote = Assert.IsType<MembershipReviewVoteRecord>(ok.Value);
        Assert.Equal(MembershipVoteType.Approve, vote.VoteType);
        Assert.Equal(userId, vote.VoterAppUserId);
    }

    [Fact]
    public async Task CastVote_ExistingVote_UpdatesVoteType()
    {
        var factory    = TestDbFactory.Create();
        var userId     = await SeedVoterAsync(factory);
        var (org, req) = await SeedRequestAsync(factory, isUnderReview: true,
            voteDeadline: DateTime.UtcNow.AddDays(7));

        // First vote
        await Build(factory, userId)
            .CastVote(org.Id, req.Id, new CastVoteRequest(MembershipVoteType.Approve, null), CancellationToken.None);

        // Update vote
        var result = await Build(factory, userId)
            .CastVote(org.Id, req.Id, new CastVoteRequest(MembershipVoteType.Deny, "Reconsidered"), CancellationToken.None);
        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var vote = Assert.IsType<MembershipReviewVoteRecord>(ok.Value);
        Assert.Equal(MembershipVoteType.Deny, vote.VoteType);

        // Confirm only one vote in DB
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.MembershipReviewVotes.CountAsync(v => v.OrganizationMembershipRequestId == req.Id && v.VoterAppUserId == userId));
    }

    [Fact]
    public async Task CastVote_NotUnderReview_Returns400()
    {
        var factory    = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory); // isUnderReview = false

        var result = await Build(factory, Guid.NewGuid())
            .CastVote(org.Id, req.Id, new CastVoteRequest(MembershipVoteType.Approve, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CastVote_DeadlinePassed_Returns400()
    {
        var factory    = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory, isUnderReview: true,
            voteDeadline: DateTime.UtcNow.AddDays(-1)); // deadline in the past

        var result = await Build(factory, Guid.NewGuid())
            .CastVote(org.Id, req.Id, new CastVoteRequest(MembershipVoteType.Approve, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CastVote_RequestNotFound_Returns404()
    {
        var factory  = TestDbFactory.Create();
        var (org, _) = await SeedRequestAsync(factory, isUnderReview: true,
            voteDeadline: DateTime.UtcNow.AddDays(7));

        var result = await Build(factory, Guid.NewGuid())
            .CastVote(org.Id, Guid.NewGuid(), new CastVoteRequest(MembershipVoteType.Approve, null), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── GetVotes ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVotes_NoVotesCast_ReturnsEmptyList()
    {
        var factory    = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory, isUnderReview: true,
            voteDeadline: DateTime.UtcNow.AddDays(7));

        var result = await Build(factory, Guid.NewGuid())
            .GetVotes(org.Id, req.Id, CancellationToken.None);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<MembershipReviewVoteRecord>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetVotes_WithVotesCast_ReturnsAll()
    {
        var factory    = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory, isUnderReview: true,
            voteDeadline: DateTime.UtcNow.AddDays(7));

        var userId = await SeedVoterAsync(factory);
        var ctrl   = Build(factory, userId);

        await ctrl.CastVote(org.Id, req.Id, new CastVoteRequest(MembershipVoteType.Approve, null), CancellationToken.None);

        var result = await ctrl.GetVotes(org.Id, req.Id, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<MembershipReviewVoteRecord>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.Equal(userId, list[0].VoterAppUserId);
        Assert.Equal(MembershipVoteType.Approve, list[0].VoteType);
    }

    [Fact]
    public async Task GetVotes_NoPermission_ReturnsForbid()
    {
        var factory    = TestDbFactory.Create();
        var (org, req) = await SeedRequestAsync(factory);

        var result = await Build(factory, Guid.NewGuid(), hasPermission: false)
            .GetVotes(org.Id, req.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
