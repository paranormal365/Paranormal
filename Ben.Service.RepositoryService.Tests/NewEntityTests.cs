using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for new entities added in 2026-08-02 session:
/// CaseClientAccess, ScheduleProposalSlot, InvestigationScheduleProposal, CaseResearchEntry, CaseMessage.
/// </summary>
public class NewEntityTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    // ── CaseClientAccess ──────────────────────────────────────────────────────

    [Fact]
    public async Task CaseClientAccess_CanBeCreatedAndQueried()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "O", UrlName = "o", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.Cases.Add(new Case { Id = caseId, OrganizationId = orgId, Title = "C", CaseYear = 2026, OrgCaseNumber = 1, StreetAddress1 = "1 St", City = "X", State = "TN", ZipCode = "00000", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.CaseClientAccesses.Add(new CaseClientAccess { Id = Guid.NewGuid(), CaseId = caseId, AppUserId = guestId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        var count = await verifyDb.CaseClientAccesses.CountAsync(a => a.CaseId == caseId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CaseClientAccess_DeleteRemovesRow()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        var accessId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "O", UrlName = "o2", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.Cases.Add(new Case { Id = caseId, OrganizationId = orgId, Title = "C", CaseYear = 2026, OrgCaseNumber = 2, StreetAddress1 = "1 St", City = "X", State = "TN", ZipCode = "00000", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.CaseClientAccesses.Add(new CaseClientAccess { Id = accessId, CaseId = caseId, AppUserId = guestId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var access = await db.CaseClientAccesses.FindAsync([accessId]);
            db.CaseClientAccesses.Remove(access!);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(0, await verifyDb.CaseClientAccesses.CountAsync(a => a.CaseId == caseId));
    }

    // ── CaseResearchEntry ─────────────────────────────────────────────────────

    [Fact]
    public void CaseResearchEntry_DefaultSortOrder_IsZero()
    {
        var e = new CaseResearchEntry();
        Assert.Equal(0, e.SortOrder);
        Assert.Equal(CaseResearchType.Note, e.ResearchType);
    }

    // ── InvestigationScheduleProposal ─────────────────────────────────────────

    [Fact]
    public void InvestigationScheduleProposal_DefaultStatus_IsPending()
    {
        var p = new InvestigationScheduleProposal();
        Assert.Equal(ScheduleProposalStatus.Pending, p.Status);
        Assert.Empty(p.Slots);
    }

    [Fact]
    public void ScheduleProposalSlot_HoldsDateFields()
    {
        var start = DateTime.UtcNow.AddDays(7);
        var s = new ScheduleProposalSlot { StartDateTime = start, SortOrder = 10 };
        Assert.Equal(start, s.StartDateTime);
        Assert.Null(s.EndDateTime);
    }

    // ── CaseMessage ───────────────────────────────────────────────────────────

    [Fact]
    public void CaseMessage_DefaultReadFlags()
    {
        var m = new CaseMessage();
        Assert.False(m.IsReadByClient);
        Assert.False(m.IsReadByOrg);
    }
}
