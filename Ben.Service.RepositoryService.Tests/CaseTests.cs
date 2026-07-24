using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for Case, CaseTimelineEntry, and related junction entities.
/// Covers DB persistence, case number sequencing, timeline cascade,
/// experience type tagging, and model index configuration.
/// </summary>
public class CaseTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "user@example.com",
            Email = "user@example.com", DisplayName = "John Smith",
            DateCreated = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Ghost Hunters TN", UrlName = "ght",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (user, org);
    }

    private static Case MakeCase(Guid orgId, Guid userId, int year = 2026, int number = 1)
        => new Case
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Status = CaseStatus.Accepted,
            Title = "Smith, Nashville TN",
            CaseYear = year, OrgCaseNumber = number,
            StreetAddress1 = "123 Haunted Ln", City = "Nashville",
            State = "TN", ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };

    // ── Case persistence ──────────────────────────────────────────────────────

    [Fact]
    public async Task Case_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var c = MakeCase(org.Id, user.Id);
        db.Cases.Add(c);
        await db.SaveChangesAsync();

        var loaded = await db.Cases.AsNoTracking().FirstAsync(x => x.Id == c.Id);
        Assert.Equal("Smith, Nashville TN", loaded.Title);
        Assert.Equal(2026, loaded.CaseYear);
        Assert.Equal(1, loaded.OrgCaseNumber);
        Assert.Equal(CaseStatus.Accepted, loaded.Status);
    }

    [Fact]
    public async Task Case_OrgCaseNumber_IsSequentialPerOrgPerYear()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        // Simulate assigning numbers in sequence
        db.Cases.Add(MakeCase(org.Id, user.Id, 2026, 1));
        db.Cases.Add(MakeCase(org.Id, user.Id, 2026, 2));
        db.Cases.Add(MakeCase(org.Id, user.Id, 2026, 3));
        await db.SaveChangesAsync();

        var max = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == org.Id && c.CaseYear == 2026)
            .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 0;

        Assert.Equal(3, max);
        // Next number should be 4
        Assert.Equal(4, max + 1);
    }

    [Fact]
    public async Task Case_OrgCaseNumber_ResetsForNewYear()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        db.Cases.Add(MakeCase(org.Id, user.Id, 2025, 1));
        db.Cases.Add(MakeCase(org.Id, user.Id, 2025, 2));
        db.Cases.Add(MakeCase(org.Id, user.Id, 2026, 1));
        await db.SaveChangesAsync();

        var max2025 = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == org.Id && c.CaseYear == 2025)
            .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 0;
        var max2026 = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == org.Id && c.CaseYear == 2026)
            .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 0;

        Assert.Equal(2, max2025);
        Assert.Equal(1, max2026);
    }

    [Fact]
    public async Task Case_OrgCaseNumber_IndependentAcrossOrgs()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org1) = await SeedAsync(db);

        // Second org
        var org2 = new Organization
        {
            Id = Guid.NewGuid(), Name = "Org Two", UrlName = "org-two",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();

        db.Cases.Add(MakeCase(org1.Id, user.Id, 2026, 1));
        db.Cases.Add(MakeCase(org1.Id, user.Id, 2026, 2));
        db.Cases.Add(MakeCase(org2.Id, user.Id, 2026, 1));  // org2 starts at 1 independently
        await db.SaveChangesAsync();

        var maxOrg1 = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == org1.Id && c.CaseYear == 2026)
            .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 0;
        var maxOrg2 = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == org2.Id && c.CaseYear == 2026)
            .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 0;

        Assert.Equal(2, maxOrg1);
        Assert.Equal(1, maxOrg2);
    }

    [Fact]
    public void Case_UniqueIndex_IsConfiguredOnModel()
    {
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("case-model-check")
                .Options);

        var entityType = db.Model.FindEntityType(typeof(Case));
        Assert.NotNull(entityType);

        var idx = entityType!.GetIndexes().FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(Case.OrganizationId)) &&
            i.Properties.Any(p => p.Name == nameof(Case.CaseYear)) &&
            i.Properties.Any(p => p.Name == nameof(Case.OrgCaseNumber)));

        Assert.NotNull(idx);
    }

    [Fact]
    public async Task Case_AllStatusValues_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        int num = 1;
        foreach (var status in Enum.GetValues<CaseStatus>())
        {
            var c = MakeCase(org.Id, user.Id, 2026, num++);
            c.Status = status;
            db.Cases.Add(c);
        }
        await db.SaveChangesAsync();

        foreach (var status in Enum.GetValues<CaseStatus>())
            Assert.True(await db.Cases.AnyAsync(c => c.Status == status));
    }

    // ── CaseTimelineEntry ─────────────────────────────────────────────────────

    [Fact]
    public async Task CaseTimelineEntry_CanBeAddedToCase()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var c = MakeCase(org.Id, user.Id);
        db.Cases.Add(c);
        await db.SaveChangesAsync();

        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = c.Id,
            AuthorAppUserId = user.Id,
            EntryType = CaseTimelineEntryType.ClientReport,
            EventDateTime = DateTime.UtcNow.AddDays(-3),
            Title = "Strange knocking",
            Body = "<p>Heard at 2am.</p>",
            IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var entries = await db.CaseTimelineEntries.AsNoTracking()
            .Where(e => e.CaseId == c.Id).ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Strange knocking", entries[0].Title);
        Assert.Equal(CaseTimelineEntryType.ClientReport, entries[0].EntryType);
    }

    [Fact]
    public async Task CaseTimelineEntry_CascadeDeletesWithCase()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var c = MakeCase(org.Id, user.Id);
        db.Cases.Add(c);
        await db.SaveChangesAsync();

        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = c.Id, AuthorAppUserId = user.Id,
            EntryType = CaseTimelineEntryType.Evidence,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.Cases.Remove(c);
        await db.SaveChangesAsync();

        var remaining = await db.CaseTimelineEntries.AsNoTracking()
            .Where(e => e.CaseId == c.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task CaseTimelineEntryExperienceType_CanTagEntry()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        // Seed an experience category + type
        var cat = new ExperienceCategory
        {
            Id = Guid.NewGuid(), Name = "Audible", SortOrder = 1,
            IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        var expType = new ExperienceType
        {
            Id = Guid.NewGuid(), ExperienceCategoryId = cat.Id,
            Name = "Knocking", SortOrder = 1, IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.ExperienceCategories.Add(cat);
        db.ExperienceTypes.Add(expType);

        var c = MakeCase(org.Id, user.Id);
        db.Cases.Add(c);
        await db.SaveChangesAsync();

        var entry = new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = c.Id, AuthorAppUserId = user.Id,
            EntryType = CaseTimelineEntryType.ClientReport,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.CaseTimelineEntries.Add(entry);
        await db.SaveChangesAsync();

        db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
        {
            CaseTimelineEntryId = entry.Id,
            ExperienceTypeId    = expType.Id,
        });
        await db.SaveChangesAsync();

        var tags = await db.CaseTimelineEntryExperienceTypes.AsNoTracking()
            .Where(t => t.CaseTimelineEntryId == entry.Id).ToListAsync();
        Assert.Single(tags);
        Assert.Equal(expType.Id, tags[0].ExperienceTypeId);
    }

    [Fact]
    public async Task CaseTimelineEntryExperienceType_CascadeDeletesWithEntry()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var cat = new ExperienceCategory
        {
            Id = Guid.NewGuid(), Name = "Visual", SortOrder = 2,
            IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        var expType = new ExperienceType
        {
            Id = Guid.NewGuid(), ExperienceCategoryId = cat.Id,
            Name = "Apparition", SortOrder = 1, IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.ExperienceCategories.Add(cat);
        db.ExperienceTypes.Add(expType);

        var c = MakeCase(org.Id, user.Id);
        db.Cases.Add(c);
        await db.SaveChangesAsync();

        var entry = new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = c.Id, AuthorAppUserId = user.Id,
            EntryType = CaseTimelineEntryType.Evidence,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.CaseTimelineEntries.Add(entry);
        await db.SaveChangesAsync();

        db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
        {
            CaseTimelineEntryId = entry.Id, ExperienceTypeId = expType.Id,
        });
        await db.SaveChangesAsync();

        db.CaseTimelineEntries.Remove(entry);
        await db.SaveChangesAsync();

        var remaining = await db.CaseTimelineEntryExperienceTypes.AsNoTracking()
            .Where(t => t.CaseTimelineEntryId == entry.Id).ToListAsync();
        Assert.Empty(remaining);
    }
}
