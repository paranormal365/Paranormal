using AutoMapper;
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

/// <summary>Tests for <see cref="UploadFileVoteController"/>: summary, upsert, and remove.</summary>
public class UploadFileVoteControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<UploadFileVoteRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is not UploadFileVote v ? new UploadFileVoteRecord() : new UploadFileVoteRecord
         {
             Id = v.Id, UploadFileId = v.UploadFileId, AppUserId = v.AppUserId, Score = v.Score,
             DateCreated = v.DateCreated, DateUpdated = v.DateUpdated,
         });
        return m.Object;
    }

    private static UploadFileVoteController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new UploadFileVoteController(factory, CreateMapper(), new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid FileId)> SeedFileAsync()
    {
        var factory = CreateFactory();
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "evidence.jpg", StoredFileName = "s.jpg", ContentType = "image/jpeg",
            FileSize = 100, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (factory, fileId);
    }

    // ── UpsertMyVote ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertMyVote_FirstVote_Returns201()
    {
        var (factory, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.UpsertMyVote(fileId, new UpsertVoteRequest(1), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var vote    = Assert.IsType<UploadFileVoteRecord>(created.Value);
        Assert.Equal(1, vote.Score);
    }

    [Fact]
    public async Task UpsertMyVote_SecondCallBySameUser_UpdatesInPlace()
    {
        var (factory, fileId) = await SeedFileAsync();
        var userId = Guid.NewGuid();
        var ctrl   = Build(factory, userId);

        await ctrl.UpsertMyVote(fileId, new UpsertVoteRequest(1), default);
        var result = await ctrl.UpsertMyVote(fileId, new UpsertVoteRequest(-1), default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var vote = Assert.IsType<UploadFileVoteRecord>(ok.Value);
        Assert.Equal(-1, vote.Score);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Single(await verify.UploadFileVotes.Where(v => v.UploadFileId == fileId && v.AppUserId == userId).ToListAsync());
    }

    [Fact]
    public async Task UpsertMyVote_FileNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result = await ctrl.UpsertMyVote(Guid.NewGuid(), new UpsertVoteRequest(1), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpsertMyVote_ConcurrentFirstVotesBySameUser_BothSucceedWithExactlyOneRow()
    {
        // Regression for the check-then-insert race on (UploadFileId, AppUserId): two concurrent
        // first-time votes from the same user used to be able to both pass the "no existing vote"
        // check, so the loser hit an unhandled DbUpdateException (raw 500) from the unique index.
        // The fix catches that and reconciles onto the winning row instead of erroring.
        var (factory, fileId) = await SeedFileAsync();
        var userId = Guid.NewGuid();
        var ctrl1  = Build(factory, userId);
        var ctrl2  = Build(factory, userId);

        var results = await Task.WhenAll(
            ctrl1.UpsertMyVote(fileId, new UpsertVoteRequest(1), default),
            ctrl2.UpsertMyVote(fileId, new UpsertVoteRequest(-1), default));

        Assert.All(results, r => Assert.True(r.Result is CreatedAtActionResult or OkObjectResult));

        await using var verify = await factory.CreateDbContextAsync();
        var votes = await verify.UploadFileVotes
            .Where(v => v.UploadFileId == fileId && v.AppUserId == userId)
            .ToListAsync();
        Assert.Single(votes);
    }

    // ── GetSummary ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_AggregatesScoresCorrectly()
    {
        var (factory, fileId) = await SeedFileAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileVotes.AddRange(
                new UploadFileVote { Id = Guid.NewGuid(), UploadFileId = fileId, AppUserId = Guid.NewGuid(), Score = 1, DateCreated = DateTime.UtcNow },
                new UploadFileVote { Id = Guid.NewGuid(), UploadFileId = fileId, AppUserId = Guid.NewGuid(), Score = 1, DateCreated = DateTime.UtcNow },
                new UploadFileVote { Id = Guid.NewGuid(), UploadFileId = fileId, AppUserId = Guid.NewGuid(), Score = -1, DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetSummary(fileId, default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<UploadFileVoteSummary>(ok.Value);
        Assert.Equal(2, summary.UpvoteCount);
        Assert.Equal(1, summary.DownvoteCount);
        Assert.Equal(1, summary.TotalScore);
        Assert.Equal(3, summary.TotalVotes);
        Assert.Null(summary.UserScore);
    }

    // ── RemoveMyVote ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMyVote_RemovesExistingVote()
    {
        var (factory, fileId) = await SeedFileAsync();
        var userId = Guid.NewGuid();
        var ctrl   = Build(factory, userId);
        await ctrl.UpsertMyVote(fileId, new UpsertVoteRequest(1), default);

        var result = await ctrl.RemoveMyVote(fileId, default);

        Assert.IsType<NoContentResult>(result);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Empty(await verify.UploadFileVotes.Where(v => v.UploadFileId == fileId && v.AppUserId == userId).ToListAsync());
    }

    [Fact]
    public async Task RemoveMyVote_NoExistingVote_StillReturnsNoContent()
    {
        var (factory, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.RemoveMyVote(fileId, default);

        Assert.IsType<NoContentResult>(result);
    }
}
