using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for Phase 3 membership entities: custom questions, applicant answers,
/// committee review votes, and the new fields on OrganizationMembershipRequest.
/// </summary>
public class MembershipPhase3Tests
{
    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "admin@org.com",
            Email = "admin@org.com", DisplayName = "Org Admin",
            DateCreated = DateTime.UtcNow,
        };
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Test Org", UrlName = "test-org",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.AppUsers.Add(user);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (user, org);
    }

    private static async Task<OrganizationMembershipRequest> SeedRequestAsync(
        BenDataContext db, Guid orgId, Guid userId)
    {
        var req = new OrganizationMembershipRequest
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Status = OrganizationMembershipRequestStatus.Pending,
            RequestMessage = "I'd like to join.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrganizationMembershipRequests.Add(req);
        await db.SaveChangesAsync();
        return req;
    }

    // ── OrganizationMembershipQuestion ────────────────────────────────────────

    [Fact]
    public async Task MembershipQuestion_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var q = new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            QuestionText = "Why do you want to join?",
            IsRequired = true, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipQuestions.Add(q);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipQuestions.AsNoTracking()
            .FirstAsync(x => x.Id == q.Id);
        Assert.Equal("Why do you want to join?", loaded.QuestionText);
        Assert.True(loaded.IsRequired);
        Assert.Equal(1, loaded.SortOrder);
    }

    [Fact]
    public async Task MembershipQuestion_MultipleQuestionsPerOrg()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        db.OrganizationMembershipQuestions.Add(new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            QuestionText = "Experience?", IsRequired = true, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        db.OrganizationMembershipQuestions.Add(new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            QuestionText = "Availability?", IsRequired = false, SortOrder = 2, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var count = await db.OrganizationMembershipQuestions.AsNoTracking()
            .CountAsync(q => q.OrganizationId == org.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MembershipQuestion_CascadeDeletesWithOrg()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        db.OrganizationMembershipQuestions.Add(new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            QuestionText = "Tell us about yourself.",
            IsRequired = true, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.Organizations.Remove(org);
        await db.SaveChangesAsync();

        var remaining = await db.OrganizationMembershipQuestions.AsNoTracking()
            .Where(q => q.OrganizationId == org.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    // ── OrganizationMembershipAnswer ──────────────────────────────────────────

    [Fact]
    public async Task MembershipAnswer_CanBeLinkedToRequestAndQuestion()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var question = new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            QuestionText = "Why join?", IsRequired = true, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipQuestions.Add(question);
        var req = await SeedRequestAsync(db, org.Id, user.Id);
        await db.SaveChangesAsync();

        var answer = new OrganizationMembershipAnswer
        {
            Id = Guid.NewGuid(),
            OrganizationMembershipRequestId  = req.Id,
            OrganizationMembershipQuestionId = question.Id,
            AnswerText = "<p>I am very passionate about paranormal research.</p>",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipAnswers.Add(answer);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipAnswers.AsNoTracking()
            .FirstAsync(a => a.Id == answer.Id);
        Assert.Equal(req.Id, loaded.OrganizationMembershipRequestId);
        Assert.Equal(question.Id, loaded.OrganizationMembershipQuestionId);
        Assert.Contains("passionate", loaded.AnswerText);
    }

    [Fact]
    public async Task MembershipAnswer_CascadeDeletesWithRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var question = new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            QuestionText = "Q?", IsRequired = true, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipQuestions.Add(question);
        var req = await SeedRequestAsync(db, org.Id, user.Id);
        await db.SaveChangesAsync();

        db.OrganizationMembershipAnswers.Add(new OrganizationMembershipAnswer
        {
            Id = Guid.NewGuid(),
            OrganizationMembershipRequestId  = req.Id,
            OrganizationMembershipQuestionId = question.Id,
            AnswerText = "Answer text.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.OrganizationMembershipRequests.Remove(req);
        await db.SaveChangesAsync();

        var remaining = await db.OrganizationMembershipAnswers.AsNoTracking()
            .Where(a => a.OrganizationMembershipRequestId == req.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public void MembershipAnswer_UniqueIndex_IsConfiguredOnModel()
    {
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("answer-model-check")
                .Options);

        var entityType = db.Model.FindEntityType(typeof(OrganizationMembershipAnswer));
        Assert.NotNull(entityType);

        var idx = entityType!.GetIndexes().FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(OrganizationMembershipAnswer.OrganizationMembershipRequestId)) &&
            i.Properties.Any(p => p.Name == nameof(OrganizationMembershipAnswer.OrganizationMembershipQuestionId)));
        Assert.NotNull(idx);
    }

    // ── MembershipReviewVote ──────────────────────────────────────────────────

    [Fact]
    public async Task MembershipReviewVote_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);
        var req = await SeedRequestAsync(db, org.Id, user.Id);

        var vote = new MembershipReviewVote
        {
            Id = Guid.NewGuid(),
            OrganizationMembershipRequestId = req.Id,
            VoterAppUserId = user.Id,
            VoteType = MembershipVoteType.Approve,
            Comment  = "Looks great!",
            DateVoted = DateTime.UtcNow,
        };
        db.MembershipReviewVotes.Add(vote);
        await db.SaveChangesAsync();

        var loaded = await db.MembershipReviewVotes.AsNoTracking()
            .FirstAsync(v => v.Id == vote.Id);
        Assert.Equal(MembershipVoteType.Approve, loaded.VoteType);
        Assert.Equal("Looks great!", loaded.Comment);
        Assert.Equal(req.Id, loaded.OrganizationMembershipRequestId);
    }

    [Fact]
    public async Task MembershipReviewVote_AllVoteTypes_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        foreach (var voteType in Enum.GetValues<MembershipVoteType>())
        {
            var req = await SeedRequestAsync(db, org.Id, user.Id);
            db.MembershipReviewVotes.Add(new MembershipReviewVote
            {
                Id = Guid.NewGuid(),
                OrganizationMembershipRequestId = req.Id,
                VoterAppUserId = user.Id, VoteType = voteType,
                DateVoted = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        foreach (var voteType in Enum.GetValues<MembershipVoteType>())
            Assert.True(await db.MembershipReviewVotes.AnyAsync(v => v.VoteType == voteType));
    }

    [Fact]
    public async Task MembershipReviewVote_CascadeDeletesWithRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);
        var req = await SeedRequestAsync(db, org.Id, user.Id);

        db.MembershipReviewVotes.Add(new MembershipReviewVote
        {
            Id = Guid.NewGuid(),
            OrganizationMembershipRequestId = req.Id,
            VoterAppUserId = user.Id, VoteType = MembershipVoteType.Deny,
            DateVoted = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        db.OrganizationMembershipRequests.Remove(req);
        await db.SaveChangesAsync();

        var remaining = await db.MembershipReviewVotes.AsNoTracking()
            .Where(v => v.OrganizationMembershipRequestId == req.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public void MembershipReviewVote_UniqueIndex_IsConfiguredOnModel()
    {
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("vote-model-check")
                .Options);

        var entityType = db.Model.FindEntityType(typeof(MembershipReviewVote));
        Assert.NotNull(entityType);

        var idx = entityType!.GetIndexes().FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(MembershipReviewVote.OrganizationMembershipRequestId)) &&
            i.Properties.Any(p => p.Name == nameof(MembershipReviewVote.VoterAppUserId)));
        Assert.NotNull(idx);
    }

    // ── Extended OrganizationMembershipRequest fields ─────────────────────────

    [Fact]
    public async Task MembershipRequest_Phase3Fields_DefaultValues()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);
        var req = await SeedRequestAsync(db, org.Id, user.Id);

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == req.Id);
        Assert.False(loaded.IsUnderReview);
        Assert.Null(loaded.VoteDeadline);
        Assert.Null(loaded.CanReapply);
        Assert.Null(loaded.DenialReason);
    }

    [Fact]
    public async Task MembershipRequest_CanBeMarkedUnderReview()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);
        var req = await SeedRequestAsync(db, org.Id, user.Id);

        var deadline = DateTime.UtcNow.AddDays(7);
        req.IsUnderReview = true;
        req.VoteDeadline  = deadline;
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == req.Id);
        Assert.True(loaded.IsUnderReview);
        Assert.NotNull(loaded.VoteDeadline);
    }

    [Fact]
    public async Task MembershipRequest_DenialMetadata_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);
        var req = await SeedRequestAsync(db, org.Id, user.Id);

        req.Status       = OrganizationMembershipRequestStatus.Denied;
        req.CanReapply   = true;
        req.DenialReason = "Please gain more experience and apply again in 6 months.";
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == req.Id);
        Assert.Equal(OrganizationMembershipRequestStatus.Denied, loaded.Status);
        Assert.True(loaded.CanReapply);
        Assert.Contains("6 months", loaded.DenialReason);
    }
}
