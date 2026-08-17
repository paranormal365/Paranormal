using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Ben.Web.Library.Services;
using Ben.Web.WebApp.Services.WebApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class PublicCaseVoteControllerTests
{
    private static PublicCaseVoteController Build(
        IDbContextFactory<BenDataContext> factory, Guid? userId = null)
    {
        var ctrl = new PublicCaseVoteController(factory);
        var claims = new List<Claim>();
        if (userId.HasValue)
            claims.Add(new Claim("app_user_id", userId.Value.ToString()));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId.HasValue ? "test" : "")),
            },
        };
        return ctrl;
    }

    private static async Task<(Organization org, Case c)> SeedPublicCaseAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var creatorId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = creatorId,
            UserName = $"{creatorId}@test.com",
            NormalizedUserName = creatorId.ToString().ToUpperInvariant(),
        });
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Ghost Crew", UrlName = "ghost-crew",
            CreatedByAppUserId = creatorId,
        };
        var @case = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Test Case",
            Status = CaseStatus.Public, IsPublic = true,
            StreetAddress1 = "123 Elm", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            CaseYear = 2026, OrgCaseNumber = 1,
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = creatorId,
        };
        db.Organizations.Add(org);
        db.Cases.Add(@case);
        await db.SaveChangesAsync();
        return (org, @case);
    }

    private static async Task<Guid> SeedVoterAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = id,
            UserName = $"{id}@test.com",
            NormalizedUserName = id.ToString().ToUpperInvariant(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ── GetSummary ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_NoVotes_ReturnsZeroCounts()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);
        var ctrl     = Build(factory);

        var result = await ctrl.GetSummary(cas.Id, default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<CaseVoteSummary>(ok.Value);
        Assert.Equal(0, summary.TotalVotes);
        Assert.Equal(0, summary.ConfirmsCount);
        Assert.Null(summary.CurrentUserVote);
    }

    [Fact]
    public async Task GetSummary_NotPublicCase_Returns404()
    {
        var factory = TestDbFactory.Create();
        var ctrl    = Build(factory);

        var result = await ctrl.GetSummary(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetSummary_WithVotes_ReturnsCorrectCounts()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);
        var v1       = await SeedVoterAsync(factory);
        var v2       = await SeedVoterAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseVotes.Add(new CaseVote { Id = Guid.NewGuid(), CaseId = cas.Id, VoterAppUserId = v1, VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow });
            db.CaseVotes.Add(new CaseVote { Id = Guid.NewGuid(), CaseId = cas.Id, VoterAppUserId = v2, VoteType = EvidenceVoteType.Disputes, DateVoted = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var result  = await Build(factory, v1).GetSummary(cas.Id, default);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<CaseVoteSummary>(ok.Value);

        Assert.Equal(2, summary.TotalVotes);
        Assert.Equal(1, summary.ConfirmsCount);
        Assert.Equal(1, summary.DisputesCount);
        Assert.Equal(EvidenceVoteType.Confirms, summary.CurrentUserVote);
        // One each way cancels — and the endpoint has to actually carry the number, not leave the
        // record's default sitting there. (Item #81.)
        Assert.Equal(0, summary.Score);
    }

    /// <summary>
    /// The score reaches the wire, and it is not the counts in disguise: three confirms against one
    /// dispute is +2, a value nothing else in the payload happens to equal.
    /// </summary>
    [Fact]
    public async Task GetSummary_CarriesTheSignedScore()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var voteType in new[]
                     {
                         EvidenceVoteType.Confirms, EvidenceVoteType.Confirms, EvidenceVoteType.Confirms,
                         EvidenceVoteType.Disputes,
                         EvidenceVoteType.Inconclusive,
                     })
                db.CaseVotes.Add(new CaseVote
                {
                    Id = Guid.NewGuid(), CaseId = cas.Id,
                    VoterAppUserId = await SeedVoterAsync(factory),
                    VoteType = voteType, DateVoted = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var result  = await Build(factory).GetSummary(cas.Id, default);
        var summary = Assert.IsType<CaseVoteSummary>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, summary.Score);
        Assert.Equal(5, summary.TotalVotes);   // the inconclusive vote counts, but does not lean
    }

    // ── CastVote ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CastVote_NewVote_IsPersisted()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);
        var userId   = await SeedVoterAsync(factory);
        var ctrl     = Build(factory, userId);

        var result = await ctrl.CastVote(cas.Id, new CastCaseVoteRequest(EvidenceVoteType.Confirms, null), default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<CaseVoteSummary>(ok.Value);
        Assert.Equal(1, summary.ConfirmsCount);
        Assert.Equal(EvidenceVoteType.Confirms, summary.CurrentUserVote);
    }

    [Fact]
    public async Task CastVote_ExistingVote_UpdatesInPlace()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);
        var userId   = await SeedVoterAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseVotes.Add(new CaseVote
            {
                Id = Guid.NewGuid(), CaseId = cas.Id, VoterAppUserId = userId,
                VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).CastVote(
            cas.Id, new CastCaseVoteRequest(EvidenceVoteType.Disputes, null), default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<CaseVoteSummary>(ok.Value);
        Assert.Equal(0, summary.ConfirmsCount);
        Assert.Equal(1, summary.DisputesCount);

        await using var db2 = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db2.CaseVotes.CountAsync(v => v.CaseId == cas.Id));
    }

    // ── RemoveVote ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveVote_VoteExists_IsDeleted()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);
        var userId   = await SeedVoterAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseVotes.Add(new CaseVote
            {
                Id = Guid.NewGuid(), CaseId = cas.Id, VoterAppUserId = userId,
                VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).RemoveVote(cas.Id, default);
        Assert.IsType<NoContentResult>(result);

        await using var db2 = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db2.CaseVotes.CountAsync(v => v.CaseId == cas.Id));
    }

    [Fact]
    public async Task RemoveVote_NoVote_Returns404()
    {
        var factory  = TestDbFactory.Create();
        var (_, cas) = await SeedPublicCaseAsync(factory);
        var userId   = await SeedVoterAsync(factory);

        var result = await Build(factory, userId).RemoveVote(cas.Id, default);
        Assert.IsType<NotFoundResult>(result);
    }

    // ── Privacy / record shape ────────────────────────────────────────────────

    [Fact]
    public void CaseVoteSummary_DoesNotExposeVoterIdentity()
    {
        var props = typeof(CaseVoteSummary).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("VoterAppUserId", props);
        Assert.DoesNotContain("VoterOrganizationId", props);
        Assert.Contains("TotalVotes",   props);
        Assert.Contains("ConfirmsCount", props);
    }
}

// ── Adapter tests ─────────────────────────────────────────────────────────────

public class CaseVoteAdapterTests
{
    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    [Fact]
    public async Task GetCaseVoteSummaryAsync_GetsFromCorrectUrl()
    {
        var caseId  = Guid.NewGuid();
        var api     = ApiMock();
        api.Setup(x => x.GetAnonymousAsync<CaseVoteSummary>(
                $"/api/public/cases/{caseId}/votes", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new CaseVoteSummary(caseId, 3, 1, 0, 4, EvidenceVoteType.Confirms));

        var result = await Build(api).GetCaseVoteSummaryAsync(caseId);

        Assert.NotNull(result);
        Assert.Equal(4, result!.TotalVotes);
        api.Verify(x => x.GetAnonymousAsync<CaseVoteSummary>(
            $"/api/public/cases/{caseId}/votes", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CastCaseVoteAsync_PostsToCorrectUrl()
    {
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PostAsync<object, CaseVoteSummary>(
                $"/api/public/cases/{caseId}/votes", It.IsAny<object>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new CaseVoteSummary(caseId, 1, 0, 0, 1, EvidenceVoteType.Confirms));

        var result = await Build(api).CastCaseVoteAsync(caseId, EvidenceVoteType.Confirms);

        Assert.NotNull(result);
        Assert.Equal(1, result!.ConfirmsCount);
        api.Verify(x => x.PostAsync<object, CaseVoteSummary>(
            $"/api/public/cases/{caseId}/votes", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveCaseVoteAsync_DeletesCorrectUrl()
    {
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.DeleteAsync($"/api/public/cases/{caseId}/votes", It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        var result = await Build(api).RemoveCaseVoteAsync(caseId);

        Assert.True(result);
        api.Verify(x => x.DeleteAsync(
            $"/api/public/cases/{caseId}/votes", It.IsAny<CancellationToken>()), Times.Once);
    }
}
