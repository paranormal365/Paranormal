using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for ExperienceCategory and ExperienceType entities: DB persistence,
/// cascade delete behaviour, approval flags, and sort order.
/// </summary>
public class ExperienceTaxonomyTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<AppUser> SeedUserAsync(BenDataContext db)
    {
        var user = new AppUser
        {
            Id          = Guid.NewGuid(),
            UserName    = "admin@example.com",
            Email       = "admin@example.com",
            DisplayName = "Admin",
            DateCreated = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static ExperienceCategory MakeCategory(Guid userId, string name = "Audible",
        int sort = 1, bool isApproved = true) => new ExperienceCategory
    {
        Id                   = Guid.NewGuid(),
        Name                 = name,
        Description          = "Sounds with no natural source.",
        ColorClass           = "text-warning",
        SortOrder            = sort,
        IsActive             = true,
        IsApproved           = isApproved,
        ApprovedByAppUserId  = isApproved ? userId : null,
        DateApproved         = isApproved ? DateTime.UtcNow : null,
        DateCreated          = DateTime.UtcNow,
        CreatedByAppUserId   = userId,
    };

    private static ExperienceType MakeType(Guid categoryId, Guid userId,
        string name = "Knocking", int sort = 1, bool isApproved = true) => new ExperienceType
    {
        Id                   = Guid.NewGuid(),
        ExperienceCategoryId = categoryId,
        Name                 = name,
        SortOrder            = sort,
        IsActive             = true,
        IsApproved           = isApproved,
        ApprovedByAppUserId  = isApproved ? userId : null,
        DateApproved         = isApproved ? DateTime.UtcNow : null,
        DateCreated          = DateTime.UtcNow,
        CreatedByAppUserId   = userId,
    };

    // ── ExperienceCategory ────────────────────────────────────────────────────

    [Fact]
    public async Task ExperienceCategory_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        var cat = MakeCategory(user.Id, "Audible");
        db.ExperienceCategories.Add(cat);
        await db.SaveChangesAsync();

        var loaded = await db.ExperienceCategories.AsNoTracking().FirstAsync(c => c.Id == cat.Id);
        Assert.Equal("Audible", loaded.Name);
        Assert.Equal("text-warning", loaded.ColorClass);
        Assert.True(loaded.IsApproved);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task ExperienceCategory_PendingApproval_IsStoredCorrectly()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        // Simulate an org-proposed category (not yet approved)
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Test Org", UrlName = "test-org",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var cat = MakeCategory(user.Id, isApproved: false);
        cat.ProposedByOrganizationId = org.Id;
        db.ExperienceCategories.Add(cat);
        await db.SaveChangesAsync();

        var loaded = await db.ExperienceCategories.AsNoTracking().FirstAsync(c => c.Id == cat.Id);
        Assert.False(loaded.IsApproved);
        Assert.Null(loaded.ApprovedByAppUserId);
        Assert.Null(loaded.DateApproved);
        Assert.Equal(org.Id, loaded.ProposedByOrganizationId);
    }

    [Fact]
    public async Task ExperienceCategory_SortOrder_IsPreserved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        db.ExperienceCategories.Add(MakeCategory(user.Id, "Audible",    sort: 1));
        db.ExperienceCategories.Add(MakeCategory(user.Id, "Visual",     sort: 2));
        db.ExperienceCategories.Add(MakeCategory(user.Id, "Physical",   sort: 3));
        await db.SaveChangesAsync();

        var cats = await db.ExperienceCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        Assert.Equal(3, cats.Count);
        Assert.Equal("Audible",  cats[0].Name);
        Assert.Equal("Visual",   cats[1].Name);
        Assert.Equal("Physical", cats[2].Name);
    }

    // ── ExperienceType ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExperienceType_CanBeSavedAndLinkedToCategory()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        var cat = MakeCategory(user.Id);
        db.ExperienceCategories.Add(cat);
        await db.SaveChangesAsync();

        db.ExperienceTypes.Add(MakeType(cat.Id, user.Id, "Knocking"));
        db.ExperienceTypes.Add(MakeType(cat.Id, user.Id, "Whispering", sort: 2));
        await db.SaveChangesAsync();

        var types = await db.ExperienceTypes
            .AsNoTracking()
            .Where(t => t.ExperienceCategoryId == cat.Id)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();

        Assert.Equal(2, types.Count);
        Assert.All(types, t => Assert.Equal(cat.Id, t.ExperienceCategoryId));
        Assert.Equal("Knocking",   types[0].Name);
        Assert.Equal("Whispering", types[1].Name);
    }

    [Fact]
    public async Task ExperienceType_CascadeDeletesWhenCategoryDeleted()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        var cat = MakeCategory(user.Id);
        db.ExperienceCategories.Add(cat);
        await db.SaveChangesAsync();

        db.ExperienceTypes.Add(MakeType(cat.Id, user.Id, "Knocking"));
        db.ExperienceTypes.Add(MakeType(cat.Id, user.Id, "Whispering", sort: 2));
        await db.SaveChangesAsync();

        // Delete the category — types should cascade
        var toDelete = await db.ExperienceCategories.FindAsync(cat.Id);
        db.ExperienceCategories.Remove(toDelete!);
        await db.SaveChangesAsync();

        var remaining = await db.ExperienceTypes
            .AsNoTracking()
            .Where(t => t.ExperienceCategoryId == cat.Id)
            .ToListAsync();

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task ExperienceType_PendingApproval_IsStoredCorrectly()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        var cat = MakeCategory(user.Id);
        db.ExperienceCategories.Add(cat);
        await db.SaveChangesAsync();

        var t = MakeType(cat.Id, user.Id, isApproved: false);
        db.ExperienceTypes.Add(t);
        await db.SaveChangesAsync();

        var loaded = await db.ExperienceTypes.AsNoTracking().FirstAsync(x => x.Id == t.Id);
        Assert.False(loaded.IsApproved);
        Assert.Null(loaded.ApprovedByAppUserId);
        Assert.Null(loaded.DateApproved);
    }

    [Fact]
    public async Task ExperienceCategory_FilterApproved_OnlyReturnsApprovedActive()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = await SeedUserAsync(db);

        db.ExperienceCategories.Add(MakeCategory(user.Id, "Approved Active",   isApproved: true));
        db.ExperienceCategories.Add(MakeCategory(user.Id, "Pending",           isApproved: false));
        // inactive + approved
        var inactive = MakeCategory(user.Id, "Inactive Approved", isApproved: true, sort: 3);
        inactive.IsActive = false;
        db.ExperienceCategories.Add(inactive);
        await db.SaveChangesAsync();

        var publicVisible = await db.ExperienceCategories
            .AsNoTracking()
            .Where(c => c.IsApproved && c.IsActive)
            .ToListAsync();

        Assert.Single(publicVisible);
        Assert.Equal("Approved Active", publicVisible[0].Name);
    }
}
