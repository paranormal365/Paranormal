using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class OrgMemberGroupControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static Mock<IMapper> CreateMapperMock()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<IEnumerable<OrgMemberGroupRecord>>(It.IsAny<object>()))
         .Returns(Array.Empty<OrgMemberGroupRecord>());
        m.Setup(x => x.Map<OrgMemberGroupRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrgMemberGroup g
             ? new OrgMemberGroupRecord { Id = g.Id, OrganizationId = g.OrganizationId, Name = g.Name }
             : new OrgMemberGroupRecord { Name = "?" });
        m.Setup(x => x.Map<IEnumerable<OrgMemberGroupMembershipRecord>>(It.IsAny<object>()))
         .Returns(Array.Empty<OrgMemberGroupMembershipRecord>());
        m.Setup(x => x.Map<OrgMemberGroupMembershipRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrgMemberGroupMembership mem
             ? new OrgMemberGroupMembershipRecord { Id = mem.Id, OrgMemberGroupId = mem.OrgMemberGroupId, OrganizationUserMembershipId = mem.OrganizationUserMembershipId }
             : new OrgMemberGroupMembershipRecord());
        return m;
    }

    private static OrgMemberGroupController Build(
        IDbContextFactory<BenDataContext> factory,
        ClaimsPrincipal? principal = null,
        Mock<IOrganizationSecurityService>? security = null)
    {
        security ??= GrantAll();
        var ctrl = new OrgMemberGroupController(factory, CreateMapperMock().Object, security.Object);
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = principal ?? Anonymous() } };
        return ctrl;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SuperAdmin(Guid id) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)
        ], "Bearer"));

    private static Mock<IOrganizationSecurityService> GrantAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(true);
        return s;
    }

    private static async Task<(OrgMemberGroup Group, OrganizationUserMembership Membership)>
        SeedGroupAndMembershipAsync(IDbContextFactory<BenDataContext> f, Guid orgId, Guid userId)
    {
        await using var db = await f.CreateDbContextAsync();
        var group = new OrgMemberGroup
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Editors",
            IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
        };
        var membership = new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
        };
        db.OrgMemberGroups.Add(group);
        db.OrganizationUserMemberships.Add(membership);
        await db.SaveChangesAsync();
        return (group, membership);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenNoUserId_ReturnsUnauthorized()
    {
        var ctrl   = Build(CreateFactory(), Anonymous());
        var result = await ctrl.GetAll(Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_AsSuperAdmin_ReturnsGroups()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        await SeedGroupAndMembershipAsync(factory, orgId, userId);
        var ctrl   = Build(factory, SuperAdmin(userId));
        var result = await ctrl.GetAll(orgId, default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithBlankName_ReturnsBadRequest()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Create(Guid.NewGuid(),
            new CreateOrgMemberGroupRequest("   ", null, true, 1), default);
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("required", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_AsSuperAdmin_CreatesGroup()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, SuperAdmin(userId));

        var result = await ctrl.Create(orgId,
            new CreateOrgMemberGroupRequest("Writers", "Content writers", true, 1), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrgMemberGroups.AnyAsync(g => g.Name == "Writers" && g.OrganizationId == orgId));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WhenNotFound_ReturnsNotFound()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Delete(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_AsSuperAdmin_RemovesGroup()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var (group, _) = await SeedGroupAndMembershipAsync(factory, orgId, userId);
        var ctrl = Build(factory, SuperAdmin(userId));

        var result = await ctrl.Delete(orgId, group.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.OrgMemberGroups.FindAsync(group.Id));
    }

    // ── AddMember ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_WhenMembershipNotInOrg_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var (group, _) = await SeedGroupAndMembershipAsync(factory, orgId, userId);

        // Try to add a membership from a DIFFERENT org
        var otherMembershipId = Guid.NewGuid();
        var ctrl = Build(factory, SuperAdmin(userId));

        var result = await ctrl.AddMember(orgId, group.Id,
            new AddGroupMemberRequest(otherMembershipId), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddMember_WhenDuplicate_ReturnsConflict()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var (group, membership) = await SeedGroupAndMembershipAsync(factory, orgId, userId);

        // Add once
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrgMemberGroupMemberships.Add(new OrgMemberGroupMembership
            {
                Id = Guid.NewGuid(), OrgMemberGroupId = group.Id,
                OrganizationUserMembershipId = membership.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, SuperAdmin(userId));
        var result = await ctrl.AddMember(orgId, group.Id,
            new AddGroupMemberRequest(membership.Id), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddMember_AsSuperAdmin_AddsToGroup()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var (group, membership) = await SeedGroupAndMembershipAsync(factory, orgId, userId);
        var ctrl = Build(factory, SuperAdmin(userId));

        var result = await ctrl.AddMember(orgId, group.Id,
            new AddGroupMemberRequest(membership.Id), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrgMemberGroupMemberships
            .AnyAsync(m => m.OrgMemberGroupId == group.Id && m.OrganizationUserMembershipId == membership.Id));
    }

    // ── RemoveMember ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMember_WhenNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var (group, _) = await SeedGroupAndMembershipAsync(factory, orgId, userId);
        var ctrl = Build(factory, SuperAdmin(userId));

        var result = await ctrl.RemoveMember(orgId, group.Id, Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }
}
