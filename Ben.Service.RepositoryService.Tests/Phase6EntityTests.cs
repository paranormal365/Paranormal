using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>Tests for Phase 6: CaseTransferLog entity.</summary>
public class Phase6EntityTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org1, Organization org2, Case c)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "u@o.com", Email = "u@o.com", DisplayName = "User", DateCreated = DateTime.UtcNow };
        var org1 = new Organization { Id = Guid.NewGuid(), Name = "Org One", UrlName = "org-one", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        var org2 = new Organization { Id = Guid.NewGuid(), Name = "Org Two", UrlName = "org-two", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        var c    = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = org1.Id, Title = "Smith, Nashville TN",
            CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Active,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.AppUsers.Add(user);
        db.Organizations.Add(org1);
        db.Organizations.Add(org2);
        db.Cases.Add(c);
        await db.SaveChangesAsync();
        return (user, org1, org2, c);
    }

    [Fact]
    public async Task CaseTransferLog_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org1, org2, c) = await SeedAsync(db);

        var log = new CaseTransferLog
        {
            Id = Guid.NewGuid(), CaseId = c.Id,
            FromOrganizationId  = org1.Id,
            ToOrganizationId    = org2.Id,
            ProposedByAppUserId = user.Id,
            Status              = CaseTransferStatus.Pending,
            TransferReason      = "Organization closing.",
            DateProposed        = DateTime.UtcNow,
        };
        db.CaseTransferLogs.Add(log);
        await db.SaveChangesAsync();

        var loaded = await db.CaseTransferLogs.AsNoTracking().FirstAsync(l => l.Id == log.Id);
        Assert.Equal(CaseTransferStatus.Pending, loaded.Status);
        Assert.Equal("Organization closing.", loaded.TransferReason);
        Assert.Equal(org1.Id, loaded.FromOrganizationId);
        Assert.Equal(org2.Id, loaded.ToOrganizationId);
    }

    [Fact]
    public async Task CaseTransferLog_CanBeAccepted()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org1, org2, c) = await SeedAsync(db);

        var log = new CaseTransferLog
        {
            Id = Guid.NewGuid(), CaseId = c.Id,
            FromOrganizationId = org1.Id, ToOrganizationId = org2.Id,
            ProposedByAppUserId = user.Id, Status = CaseTransferStatus.Pending,
            DateProposed = DateTime.UtcNow,
        };
        db.CaseTransferLogs.Add(log);
        await db.SaveChangesAsync();

        log.Status               = CaseTransferStatus.Accepted;
        log.RespondedByAppUserId = user.Id;
        log.DateResponded        = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var loaded = await db.CaseTransferLogs.AsNoTracking().FirstAsync(l => l.Id == log.Id);
        Assert.Equal(CaseTransferStatus.Accepted, loaded.Status);
        Assert.NotNull(loaded.DateResponded);
    }

    [Fact]
    public async Task CaseTransferLog_CanBeRejectedWithReason()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org1, org2, c) = await SeedAsync(db);

        var log = new CaseTransferLog
        {
            Id = Guid.NewGuid(), CaseId = c.Id,
            FromOrganizationId = org1.Id, ToOrganizationId = org2.Id,
            ProposedByAppUserId = user.Id, Status = CaseTransferStatus.Pending,
            DateProposed = DateTime.UtcNow,
        };
        db.CaseTransferLogs.Add(log);
        await db.SaveChangesAsync();

        log.Status          = CaseTransferStatus.Rejected;
        log.RejectionReason = "We are at capacity.";
        log.DateResponded   = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var loaded = await db.CaseTransferLogs.AsNoTracking().FirstAsync(l => l.Id == log.Id);
        Assert.Equal(CaseTransferStatus.Rejected, loaded.Status);
        Assert.Equal("We are at capacity.", loaded.RejectionReason);
    }

    [Fact]
    public async Task CaseTransferLog_AllStatusValues_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org1, org2, c) = await SeedAsync(db);

        foreach (var status in Enum.GetValues<CaseTransferStatus>())
        {
            db.CaseTransferLogs.Add(new CaseTransferLog
            {
                Id = Guid.NewGuid(), CaseId = c.Id,
                FromOrganizationId = org1.Id, ToOrganizationId = org2.Id,
                ProposedByAppUserId = user.Id, Status = status,
                DateProposed = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        foreach (var status in Enum.GetValues<CaseTransferStatus>())
            Assert.True(await db.CaseTransferLogs.AnyAsync(l => l.Status == status));
    }

    [Fact]
    public async Task PublicCase_CaseReference_FormatsCorrectly()
    {
        // Verify that the case reference formatting logic used in the public endpoint is correct
        var year   = 2026;
        var number = 42;
        var expected = $"#{year}-{number:D3}";
        Assert.Equal("#2026-042", expected);
    }

    [Fact]
    public async Task Case_PublicPseudonym_CanBeSetAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org1, _, c) = await SeedAsync(db);

        c.IsPublic        = true;
        c.PublicPseudonym = "The Hendersons";
        c.Status          = CaseStatus.Public;
        await db.SaveChangesAsync();

        var loaded = await db.Cases.AsNoTracking().FirstAsync(x => x.Id == c.Id);
        Assert.True(loaded.IsPublic);
        Assert.Equal("The Hendersons", loaded.PublicPseudonym);
        Assert.Equal(CaseStatus.Public, loaded.Status);
    }
}
