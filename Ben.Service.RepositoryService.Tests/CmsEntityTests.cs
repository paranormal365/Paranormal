using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Entity-level tests for the CMS data model:
/// OrganizationLogo, OrganizationPage (new fields), CmsSection,
/// OrgMemberGroup, OrgMemberGroupMembership, CmsPagePermission.
/// </summary>
public class CmsEntityTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static Organization MakeOrg() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Org",
        UrlName = "test-org",
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = Guid.NewGuid()
    };

    private static OrganizationPage MakePage(Guid orgId, string urlName = "test-page") => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        IsHome = false,
        PageTitle = "Test Page",
        UrlName = urlName,
        PageHtml = "<p>Summary</p>",
        IsPublished = false,
        IsPublic = false,
        SortOrder = 1,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = Guid.NewGuid()
    };

    private static OrgMemberGroup MakeGroup(Guid orgId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        Name = "Editors",
        IsActive = true,
        SortOrder = 1,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = Guid.NewGuid()
    };

    // ── OrganizationLogo ──────────────────────────────────────────────────────

    [Fact]
    public async Task OrganizationLogo_CanBeCreatedAndRetrieved()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var logoId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrganizationLogos.Add(new OrganizationLogo
            {
                Id             = logoId,
                OrganizationId = org.Id,
                UploadFileId   = Guid.NewGuid(),
                AltText        = "BenCo Logo",
                IsActive       = true,
                SortOrder      = 1,
                DateCreated    = DateTime.UtcNow,
                CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var logo = await db.OrganizationLogos.FindAsync(logoId);
            Assert.NotNull(logo);
            Assert.Equal(org.Id, logo.OrganizationId);
            Assert.Equal("BenCo Logo", logo.AltText);
            Assert.True(logo.IsActive);
        }
    }

    [Fact]
    public async Task OrganizationLogo_IsActive_CanBeFlipped()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var logoId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrganizationLogos.Add(new OrganizationLogo
            {
                Id = logoId, OrganizationId = org.Id, UploadFileId = Guid.NewGuid(),
                IsActive = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var logo = await db.OrganizationLogos.FindAsync(logoId);
            logo!.IsActive = true;
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True((await db.OrganizationLogos.FindAsync(logoId))!.IsActive);
        }
    }

    [Fact]
    public async Task OrganizationLogo_CascadeDeletesWithOrganization()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var logoId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrganizationLogos.Add(new OrganizationLogo
            {
                Id = logoId, OrganizationId = org.Id, UploadFileId = Guid.NewGuid(),
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var o = await db.Organizations.Include(o => o.OrganizationLogos).FirstAsync(o => o.Id == org.Id);
            db.Organizations.Remove(o);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.OrganizationLogos.FindAsync(logoId));
        }
    }

    // ── OrganizationPage — new fields ─────────────────────────────────────────

    [Fact]
    public async Task OrganizationPage_IsPublic_DefaultsFalse()
    {
        var factory = CreateFactory();
        var page    = MakePage(Guid.NewGuid());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.False((await db.OrganizationPages.FindAsync(page.Id))!.IsPublic);
        }
    }

    [Fact]
    public async Task OrganizationPage_IsPublic_CanBeSetTrue()
    {
        var factory = CreateFactory();
        var page    = MakePage(Guid.NewGuid());
        page.IsPublic = true;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True((await db.OrganizationPages.FindAsync(page.Id))!.IsPublic);
        }
    }

    [Fact]
    public async Task OrganizationPage_ParentChild_HierarchyIsPersisted()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var parent  = MakePage(orgId, "parent");
        var child   = MakePage(orgId, "child");
        child.ParentPageId = parent.Id;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.AddRange(parent, child);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = await db.OrganizationPages
                .Include(p => p.ParentPage)
                .FirstAsync(p => p.Id == child.Id);
            Assert.NotNull(c.ParentPage);
            Assert.Equal(parent.Id, c.ParentPage.Id);
        }
    }

    [Fact]
    public async Task OrganizationPage_ChildPages_LoadsMultipleChildren()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var parent  = MakePage(orgId, "parent");
        var child1  = MakePage(orgId, "child-1");
        var child2  = MakePage(orgId, "child-2");
        child1.ParentPageId = parent.Id;
        child2.ParentPageId = parent.Id;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.AddRange(parent, child1, child2);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var p = await db.OrganizationPages
                .Include(p => p.ChildPages)
                .FirstAsync(p => p.Id == parent.Id);
            Assert.Equal(2, p.ChildPages.Count);
        }
    }

    // ── CmsSection ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CmsSection_CanBeCreatedWithRichTextContent()
    {
        var factory    = CreateFactory();
        var page       = MakePage(Guid.NewGuid());
        var sectionId  = Guid.NewGuid();
        var content    = "{\"html\":\"<p>Hello World</p>\"}";

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            db.CmsSections.Add(new CmsSection
            {
                Id = sectionId,
                OrganizationPageId = page.Id,
                SectionType = CmsSectionType.RichText,
                Title = "Introduction",
                ContentJson = content,
                SortOrder = 1,
                IsActive = true,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var s = await db.CmsSections.FindAsync(sectionId);
            Assert.NotNull(s);
            Assert.Equal(CmsSectionType.RichText, s.SectionType);
            Assert.Equal(content, s.ContentJson);
            Assert.Equal("Introduction", s.Title);
            Assert.True(s.IsActive);
        }
    }

    [Fact]
    public async Task CmsSection_AllSectionTypesCanBePersisted()
    {
        var factory    = CreateFactory();
        var page       = MakePage(Guid.NewGuid());
        var types      = Enum.GetValues<CmsSectionType>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            var order = 1;
            foreach (var type in types)
                db.CmsSections.Add(new CmsSection
                {
                    Id = Guid.NewGuid(), OrganizationPageId = page.Id,
                    SectionType = type, ContentJson = "{}", SortOrder = order++,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
                });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var count = await db.CmsSections.CountAsync(s => s.OrganizationPageId == page.Id);
            Assert.Equal(types.Length, count);
        }
    }

    [Fact]
    public async Task CmsSection_SortOrder_AllowsReordering()
    {
        var factory = CreateFactory();
        var page    = MakePage(Guid.NewGuid());
        var idA     = Guid.NewGuid();
        var idB     = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            db.CmsSections.Add(new CmsSection { Id = idA, OrganizationPageId = page.Id, SectionType = CmsSectionType.RichText, ContentJson = "{}", SortOrder = 2, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            db.CmsSections.Add(new CmsSection { Id = idB, OrganizationPageId = page.Id, SectionType = CmsSectionType.ImageBanner, ContentJson = "{}", SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var ordered = await db.CmsSections
                .Where(s => s.OrganizationPageId == page.Id)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();
            Assert.Equal(idB, ordered[0].Id); // SortOrder 1 comes first
            Assert.Equal(idA, ordered[1].Id);
        }
    }

    [Fact]
    public async Task CmsSection_CascadeDeletesWithPage()
    {
        var factory   = CreateFactory();
        var page      = MakePage(Guid.NewGuid());
        var sectionId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            db.CmsSections.Add(new CmsSection { Id = sectionId, OrganizationPageId = page.Id, SectionType = CmsSectionType.CustomHtml, ContentJson = "{}", SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var p = await db.OrganizationPages.Include(p => p.CmsSections).FirstAsync(p => p.Id == page.Id);
            db.OrganizationPages.Remove(p);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.CmsSections.FindAsync(sectionId));
        }
    }

    // ── OrgMemberGroup ────────────────────────────────────────────────────────

    [Fact]
    public async Task OrgMemberGroup_CanBeCreatedAndRetrieved()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var groupId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrgMemberGroups.Add(new OrgMemberGroup
            {
                Id = groupId, OrganizationId = org.Id,
                Name = "Editorial Team", Description = "Members who can edit content",
                IsActive = true, SortOrder = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var g = await db.OrgMemberGroups.FindAsync(groupId);
            Assert.NotNull(g);
            Assert.Equal("Editorial Team", g.Name);
            Assert.Equal("Members who can edit content", g.Description);
            Assert.True(g.IsActive);
        }
    }

    [Fact]
    public async Task OrgMemberGroup_CascadeDeletesWithOrganization()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var groupId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrgMemberGroups.Add(new OrgMemberGroup { Id = groupId, OrganizationId = org.Id, Name = "Writers", IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var o = await db.Organizations.Include(o => o.MemberGroups).FirstAsync(o => o.Id == org.Id);
            db.Organizations.Remove(o);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.OrgMemberGroups.FindAsync(groupId));
        }
    }

    // ── OrgMemberGroupMembership ──────────────────────────────────────────────

    [Fact]
    public async Task OrgMemberGroupMembership_CanLinkGroupAndOrgMembership()
    {
        var factory      = CreateFactory();
        var org          = MakeOrg();
        var userId       = Guid.NewGuid();
        var group        = MakeGroup(org.Id);
        var membershipId = Guid.NewGuid();
        var gmId         = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrgMemberGroups.Add(group);
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = org.Id, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            db.OrgMemberGroupMemberships.Add(new OrgMemberGroupMembership
            {
                Id = gmId, OrgMemberGroupId = group.Id, OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var gm = await db.OrgMemberGroupMemberships
                .Include(m => m.OrgMemberGroup)
                .FirstAsync(m => m.Id == gmId);
            Assert.Equal(group.Id, gm.OrgMemberGroupId);
            Assert.Equal(membershipId, gm.OrganizationUserMembershipId);
            Assert.Equal("Editors", gm.OrgMemberGroup.Name);
        }
    }

    [Fact]
    public async Task OrgMemberGroupMembership_CascadeDeletesWithGroup()
    {
        var factory      = CreateFactory();
        var org          = MakeOrg();
        var userId       = Guid.NewGuid();
        var group        = MakeGroup(org.Id);
        var membershipId = Guid.NewGuid();
        var gmId         = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrgMemberGroups.Add(group);
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = org.Id, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            db.OrgMemberGroupMemberships.Add(new OrgMemberGroupMembership
            {
                Id = gmId, OrgMemberGroupId = group.Id, OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var g = await db.OrgMemberGroups.Include(g => g.Members).FirstAsync(g => g.Id == group.Id);
            db.OrgMemberGroups.Remove(g);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.OrgMemberGroupMemberships.FindAsync(gmId));
        }
    }

    // ── CmsPagePermission ─────────────────────────────────────────────────────

    [Fact]
    public async Task CmsPagePermission_CanGrantActionsToUser()
    {
        var factory = CreateFactory();
        var page    = MakePage(Guid.NewGuid());
        var userId  = Guid.NewGuid();
        var permId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            db.CmsPagePermissions.Add(new CmsPagePermission
            {
                Id = permId, OrganizationPageId = page.Id,
                AppUserId = userId,
                Actions = CmsPageAction.View | CmsPageAction.Edit,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var p = await db.CmsPagePermissions.FindAsync(permId);
            Assert.NotNull(p);
            Assert.Equal(userId, p.AppUserId);
            Assert.Null(p.OrgMemberGroupId);
            Assert.True(p.Actions.HasFlag(CmsPageAction.View));
            Assert.True(p.Actions.HasFlag(CmsPageAction.Edit));
            Assert.False(p.Actions.HasFlag(CmsPageAction.Delete));
        }
    }

    [Fact]
    public async Task CmsPagePermission_CanGrantActionsToGroup()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var page    = MakePage(org.Id);
        var group   = MakeGroup(org.Id);
        var permId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.OrganizationPages.Add(page);
            db.OrgMemberGroups.Add(group);
            db.CmsPagePermissions.Add(new CmsPagePermission
            {
                Id = permId, OrganizationPageId = page.Id,
                OrgMemberGroupId = group.Id,
                Actions = CmsPageAction.View,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var p = await db.CmsPagePermissions.FindAsync(permId);
            Assert.NotNull(p);
            Assert.Null(p.AppUserId);
            Assert.Equal(group.Id, p.OrgMemberGroupId);
            Assert.Equal(CmsPageAction.View, p.Actions);
        }
    }

    [Fact]
    public async Task CmsPagePermission_CascadeDeletesWithPage()
    {
        var factory = CreateFactory();
        var page    = MakePage(Guid.NewGuid());
        var permId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(page);
            db.CmsPagePermissions.Add(new CmsPagePermission
            {
                Id = permId, OrganizationPageId = page.Id,
                AppUserId = Guid.NewGuid(), Actions = CmsPageAction.View,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var p = await db.OrganizationPages
                .Include(p => p.PagePermissions)
                .FirstAsync(p => p.Id == page.Id);
            db.OrganizationPages.Remove(p);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.CmsPagePermissions.FindAsync(permId));
        }
    }

    [Fact]
    public void CmsPageAction_FlagCombinationsAreCorrect()
    {
        var all = CmsPageAction.View | CmsPageAction.Edit | CmsPageAction.Delete;
        Assert.True(all.HasFlag(CmsPageAction.View));
        Assert.True(all.HasFlag(CmsPageAction.Edit));
        Assert.True(all.HasFlag(CmsPageAction.Delete));

        Assert.False(CmsPageAction.View.HasFlag(CmsPageAction.Edit));
        Assert.False(CmsPageAction.None.HasFlag(CmsPageAction.View));
    }
}
