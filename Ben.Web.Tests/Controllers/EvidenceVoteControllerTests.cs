using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class EvidenceVoteControllerTests
{
    private static EvidenceVoteController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid? userId = null)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<IEnumerable<EvidenceVoteRecord>>(It.IsAny<object>()))
              .Returns((object src) =>
              {
                  if (src is not IEnumerable<EvidenceVote> votes) return [];
                  return votes.Select(v => new EvidenceVoteRecord
                  {
                      Id = v.Id, UploadFileId = v.UploadFileId,
                      VoterAppUserId = v.VoterAppUserId,
                      VoterDisplayName = v.VoterAppUser?.DisplayName,
                      VoterOrganizationId = v.VoterOrganizationId,
                      VoteType = v.VoteType, Comment = v.Comment,
                      IsPublicVoter = v.IsPublicVoter, DateVoted = v.DateVoted,
                  });
              });

        var ctrl = new EvidenceVoteController(factory, mapper.Object);

        var claims = new List<Claim>();
        if (userId.HasValue)
            claims.Add(new Claim("app_user_id", userId.Value.ToString()));

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
            },
        };
        return ctrl;
    }

    private static async Task<Guid> SeedFileAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), FileName = "evidence.mp3",
            UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
            StoragePath = "/files/evidence.mp3",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        };
        db.UploadFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static async Task<Guid> SeedVoterAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var voterId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser { Id = voterId, UserName = $"{voterId}@test.com", NormalizedUserName = voterId.ToString().ToUpperInvariant() });
        await db.SaveChangesAsync();
        return voterId;
    }

    private static async Task SeedVoteAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, Guid voterId, EvidenceVoteType voteType)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.EvidenceVotes.Add(new EvidenceVote
        {
            Id = Guid.NewGuid(), UploadFileId = fileId, VoterAppUserId = voterId,
            VoteType = voteType, IsPublicVoter = true,
            DateVoted = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── GetSummary ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_NoVotes_ReturnsZeroCounts()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);

        var result  = await Build(factory).GetSummary(fileId, CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<EvidenceVoteSummary>(ok.Value);

        Assert.Equal(fileId, summary.UploadFileId);
        Assert.Equal(0, summary.TotalVotes);
        Assert.Equal(0, summary.ConfirmsCount);
        Assert.Equal(0, summary.DisputesCount);
        Assert.Equal(0, summary.InconclusiveCount);
        Assert.Null(summary.CurrentUserVote);
    }

    [Fact]
    public async Task GetSummary_WithVotes_ReturnsCorrectCounts()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);

        await SeedVoteAsync(factory, fileId, Guid.NewGuid(), EvidenceVoteType.Confirms);
        await SeedVoteAsync(factory, fileId, Guid.NewGuid(), EvidenceVoteType.Confirms);
        await SeedVoteAsync(factory, fileId, Guid.NewGuid(), EvidenceVoteType.Disputes);

        var result  = await Build(factory).GetSummary(fileId, CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<EvidenceVoteSummary>(ok.Value);

        Assert.Equal(3, summary.TotalVotes);
        Assert.Equal(2, summary.ConfirmsCount);
        Assert.Equal(1, summary.DisputesCount);
        Assert.Equal(0, summary.InconclusiveCount);
    }

    [Fact]
    public async Task GetSummary_AuthenticatedVoter_IncludesCurrentUserVote()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);
        var userId  = Guid.NewGuid();
        await SeedVoteAsync(factory, fileId, userId, EvidenceVoteType.Inconclusive);

        var result  = await Build(factory, userId).GetSummary(fileId, CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<EvidenceVoteSummary>(ok.Value);

        Assert.Equal(EvidenceVoteType.Inconclusive, summary.CurrentUserVote);
    }

    [Fact]
    public async Task GetSummary_AuthenticatedUserNotVoted_CurrentUserVoteIsNull()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);
        await SeedVoteAsync(factory, fileId, Guid.NewGuid(), EvidenceVoteType.Confirms);

        var result  = await Build(factory, Guid.NewGuid()).GetSummary(fileId, CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<EvidenceVoteSummary>(ok.Value);

        Assert.Null(summary.CurrentUserVote);
    }

    [Fact]
    public void GetSummary_ResponseType_DoesNotExposeVoterIdentity()
    {
        var props = typeof(EvidenceVoteSummary).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("VoterAppUserId",    props);
        Assert.DoesNotContain("VoterDisplayName",  props);
        Assert.DoesNotContain("VoterOrganizationId", props);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsAllVotesForFile()
    {
        var factory  = TestDbFactory.Create();
        var fileId   = await SeedFileAsync(factory);
        var voter1   = await SeedVoterAsync(factory);
        var voter2   = await SeedVoterAsync(factory);
        await SeedVoteAsync(factory, fileId, voter1, EvidenceVoteType.Confirms);
        await SeedVoteAsync(factory, fileId, voter2, EvidenceVoteType.Disputes);

        var result = await Build(factory, Guid.NewGuid()).GetAll(fileId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<EvidenceVoteRecord>>(ok.Value).ToList();

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetAll_OnlyReturnsVotesForRequestedFile()
    {
        var factory  = TestDbFactory.Create();
        var fileId1  = await SeedFileAsync(factory);
        var fileId2  = await SeedFileAsync(factory);
        var voter1   = await SeedVoterAsync(factory);
        var voter2   = await SeedVoterAsync(factory);
        await SeedVoteAsync(factory, fileId1, voter1, EvidenceVoteType.Confirms);
        await SeedVoteAsync(factory, fileId2, voter2, EvidenceVoteType.Disputes);

        var result = await Build(factory, Guid.NewGuid()).GetAll(fileId1, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<EvidenceVoteRecord>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.All(list, v => Assert.Equal(fileId1, v.UploadFileId));
    }

    // ── CastVote ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CastVote_FileNotFound_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory, Guid.NewGuid())
            .CastVote(Guid.NewGuid(), new CastEvidenceVoteRequest(EvidenceVoteType.Confirms, null), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CastVote_NewVote_Returns200WithUpdatedSummary()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);
        var userId  = Guid.NewGuid();

        var result  = await Build(factory, userId)
            .CastVote(fileId, new CastEvidenceVoteRequest(EvidenceVoteType.Confirms, "Very convincing"), CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<EvidenceVoteSummary>(ok.Value);

        Assert.Equal(1, summary.TotalVotes);
        Assert.Equal(1, summary.ConfirmsCount);
        Assert.Equal(EvidenceVoteType.Confirms, summary.CurrentUserVote);
    }

    [Fact]
    public async Task CastVote_ExistingVote_UpdatesVoteTypeInPlace()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);
        var userId  = Guid.NewGuid();

        // First vote
        await Build(factory, userId)
            .CastVote(fileId, new CastEvidenceVoteRequest(EvidenceVoteType.Confirms, null), CancellationToken.None);

        // Update to Disputes
        var result  = await Build(factory, userId)
            .CastVote(fileId, new CastEvidenceVoteRequest(EvidenceVoteType.Disputes, null), CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<EvidenceVoteSummary>(ok.Value);

        // Still 1 vote total (upserted, not doubled)
        Assert.Equal(1, summary.TotalVotes);
        Assert.Equal(0, summary.ConfirmsCount);
        Assert.Equal(1, summary.DisputesCount);
        Assert.Equal(EvidenceVoteType.Disputes, summary.CurrentUserVote);
    }

    // ── RemoveVote ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveVote_VoteExists_Returns204()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);
        var userId  = Guid.NewGuid();
        await SeedVoteAsync(factory, fileId, userId, EvidenceVoteType.Confirms);

        var result = await Build(factory, userId).RemoveVote(fileId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        // Verify deleted
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.EvidenceVotes.CountAsync(v => v.UploadFileId == fileId && v.VoterAppUserId == userId));
    }

    [Fact]
    public async Task RemoveVote_VoteNotFound_Returns404()
    {
        var factory = TestDbFactory.Create();
        var fileId  = await SeedFileAsync(factory);

        var result = await Build(factory, Guid.NewGuid()).RemoveVote(fileId, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }
}
