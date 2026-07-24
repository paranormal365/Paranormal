using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>Tests for Phase 5 Investigation and EvidenceVote entities.</summary>
public class Phase5EntityTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org, Case c)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "u@o.com", Email = "u@o.com", DisplayName = "User", DateCreated = DateTime.UtcNow };
        var org  = new Organization { Id = Guid.NewGuid(), Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        var c    = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Smith, Nashville TN",
            CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Active,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.AppUsers.Add(user);
        db.Organizations.Add(org);
        db.Cases.Add(c);
        await db.SaveChangesAsync();
        return (user, org, c);
    }

    // ── Investigation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Investigation_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _, c) = await SeedAsync(db);

        var inv = new Investigation
        {
            Id = Guid.NewGuid(), CaseId = c.Id,
            Title = "Night Investigation #1",
            ScheduledDateTime = DateTime.UtcNow.AddDays(7),
            Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.Investigations.Add(inv);
        await db.SaveChangesAsync();

        var loaded = await db.Investigations.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
        Assert.Equal("Night Investigation #1", loaded.Title);
        Assert.Equal(InvestigationStatus.Scheduled, loaded.Status);
        Assert.Null(loaded.Notes);
    }

    [Fact]
    public async Task Investigation_StatusCanProgress()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _, c) = await SeedAsync(db);

        var inv = new Investigation
        {
            Id = Guid.NewGuid(), CaseId = c.Id, Title = "Inv",
            ScheduledDateTime = DateTime.UtcNow, Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.Investigations.Add(inv);
        await db.SaveChangesAsync();

        inv.Status  = InvestigationStatus.Completed;
        inv.Notes   = "<p>No unusual activity detected.</p>";
        await db.SaveChangesAsync();

        var loaded = await db.Investigations.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
        Assert.Equal(InvestigationStatus.Completed, loaded.Status);
        Assert.Contains("No unusual", loaded.Notes);
    }

    [Fact]
    public async Task Investigation_CascadeDeletesWithCase()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _, c) = await SeedAsync(db);

        db.Investigations.Add(new Investigation
        {
            Id = Guid.NewGuid(), CaseId = c.Id, Title = "Inv",
            ScheduledDateTime = DateTime.UtcNow, Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.Cases.Remove(c);
        await db.SaveChangesAsync();

        var remaining = await db.Investigations.AsNoTracking()
            .Where(i => i.CaseId == c.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task InvestigationAttendee_CanBeAddedAndTracksAttendance()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _, c) = await SeedAsync(db);

        var inv = new Investigation
        {
            Id = Guid.NewGuid(), CaseId = c.Id, Title = "Inv",
            ScheduledDateTime = DateTime.UtcNow, Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.Investigations.Add(inv);
        await db.SaveChangesAsync();

        var attendee = new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = inv.Id, AppUserId = user.Id,
            AssignedRole = "Lead Investigator",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.InvestigationAttendees.Add(attendee);
        await db.SaveChangesAsync();

        var loaded = await db.InvestigationAttendees.AsNoTracking().FirstAsync(a => a.Id == attendee.Id);
        Assert.Equal("Lead Investigator", loaded.AssignedRole);
        Assert.Null(loaded.DidAttend); // not yet determined

        attendee.DidAttend = true;
        await db.SaveChangesAsync();

        var confirmed = await db.InvestigationAttendees.AsNoTracking().FirstAsync(a => a.Id == attendee.Id);
        Assert.True(confirmed.DidAttend);
    }

    [Fact]
    public async Task InvestigationAttendee_UniqueIndex_IsConfiguredOnModel()
    {
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("inv-attendee-model").Options);
        var et = db.Model.FindEntityType(typeof(InvestigationAttendee));
        Assert.NotNull(et);
        var idx = et!.GetIndexes().FirstOrDefault(i => i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(InvestigationAttendee.InvestigationId)) &&
            i.Properties.Any(p => p.Name == nameof(InvestigationAttendee.AppUserId)));
        Assert.NotNull(idx);
    }

    // ── EvidenceVote ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EvidenceVote_CanBeCastOnUploadFile()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _, _) = await SeedAsync(db);

        var ft = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Evidence", IsActive = true, IsPublic = true,
            AllowAllExtensions = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(ft);
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = ft.Id, AppUserId = user.Id,
            FileName = "evidence.jpg", StoredFileName = "e.jpg", ContentType = "image/jpeg",
            FileSize = 100, IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFiles.Add(file);
        await db.SaveChangesAsync();

        var vote = new EvidenceVote
        {
            Id = Guid.NewGuid(), UploadFileId = file.Id, VoterAppUserId = user.Id,
            VoteType = EvidenceVoteType.Confirms, Comment = "Definitely paranormal!",
            DateVoted = DateTime.UtcNow,
        };
        db.EvidenceVotes.Add(vote);
        await db.SaveChangesAsync();

        var loaded = await db.EvidenceVotes.AsNoTracking().FirstAsync(v => v.Id == vote.Id);
        Assert.Equal(EvidenceVoteType.Confirms, loaded.VoteType);
        Assert.Equal("Definitely paranormal!", loaded.Comment);
    }

    [Fact]
    public async Task EvidenceVote_AllVoteTypes_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _, _) = await SeedAsync(db);

        var ft = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Ev", IsActive = true, IsPublic = true,
            AllowAllExtensions = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(ft);

        foreach (var vt in Enum.GetValues<EvidenceVoteType>())
        {
            var f = new UploadFile
            {
                Id = Guid.NewGuid(), UploadFileTypeId = ft.Id, AppUserId = user.Id,
                FileName = "f.jpg", StoredFileName = "s.jpg", ContentType = "image/jpeg",
                FileSize = 1, IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
            };
            db.UploadFiles.Add(f);
            db.EvidenceVotes.Add(new EvidenceVote
            {
                Id = Guid.NewGuid(), UploadFileId = f.Id, VoterAppUserId = user.Id,
                VoteType = vt, DateVoted = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        foreach (var vt in Enum.GetValues<EvidenceVoteType>())
            Assert.True(await db.EvidenceVotes.AnyAsync(v => v.VoteType == vt));
    }

    [Fact]
    public async Task EvidenceVote_UniqueIndex_IsConfiguredOnModel()
    {
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("ev-vote-model").Options);
        var et = db.Model.FindEntityType(typeof(EvidenceVote));
        Assert.NotNull(et);
        var idx = et!.GetIndexes().FirstOrDefault(i => i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(EvidenceVote.UploadFileId)) &&
            i.Properties.Any(p => p.Name == nameof(EvidenceVote.VoterAppUserId)));
        Assert.NotNull(idx);
    }
}
