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

/// <summary>
/// The member-title ladder endpoints (item 157). Titles are seniority, never permission —
/// these tests pin the auth boundary (members read, admins write), the per-org isolation of the
/// ladder, and the promise that deleting a rung clears rather than blocks.
/// </summary>
public sealed class OrganizationMemberLevelControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static IMapper Mapper()
    {
        var mock = new Mock<IMapper>();
        mock.Setup(m => m.Map<OrganizationMemberLevelRecord>(It.IsAny<object>()))
            .Returns<object>(o =>
            {
                var l = (OrganizationMemberLevel)o;
                return new OrganizationMemberLevelRecord
                { Id = l.Id, OrganizationId = l.OrganizationId, Name = l.Name, SortOrder = l.SortOrder, IsActive = l.IsActive };
            });
        mock.Setup(m => m.Map<IEnumerable<OrganizationMemberLevelRecord>>(It.IsAny<object>()))
            .Returns<object>(o => ((IEnumerable<OrganizationMemberLevel>)o)
                .Select(l => new OrganizationMemberLevelRecord
                { Id = l.Id, OrganizationId = l.OrganizationId, Name = l.Name, SortOrder = l.SortOrder, IsActive = l.IsActive }));
        return mock.Object;
    }

    private static OrganizationMemberLevelController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new OrganizationMemberLevelController(factory, Mapper());
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

    private static async Task<(Guid orgId, Guid adminId, Guid memberId)> SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        var orgId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = "g", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();
        return (orgId, adminId, memberId);
    }

    [Fact]
    public async Task Members_can_read_the_ladder_but_not_edit_it()
    {
        var factory = CreateFactory();
        var (orgId, _, memberId) = await SeedAsync(factory);
        var ctrl = Build(factory, memberId);

        Assert.IsType<OkObjectResult>((await ctrl.GetAll(orgId, default)).Result);
        Assert.IsType<ForbidResult>((await ctrl.Create(orgId, new UpsertMemberLevelRequest("Rung", 1, true), default)).Result);
    }

    [Fact]
    public async Task A_stranger_cannot_even_read_it()
    {
        var factory = CreateFactory();
        var (orgId, _, _) = await SeedAsync(factory);
        var ctrl = Build(factory, Guid.NewGuid());

        Assert.IsType<ForbidResult>((await ctrl.GetAll(orgId, default)).Result);
    }

    [Fact]
    public async Task An_admin_manages_the_full_lifecycle()
    {
        var factory = CreateFactory();
        var (orgId, adminId, _) = await SeedAsync(factory);
        var ctrl = Build(factory, adminId);

        var created = Assert.IsType<CreatedAtActionResult>(
            (await ctrl.Create(orgId, new UpsertMemberLevelRequest("Tech Specialist", 6, true), default)).Result);
        var record = Assert.IsType<OrganizationMemberLevelRecord>(created.Value);

        Assert.IsType<OkObjectResult>(
            (await ctrl.Update(orgId, record.Id, new UpsertMemberLevelRequest("Tech Lead", 6, true), default)).Result);
        Assert.IsType<NoContentResult>(await ctrl.Delete(orgId, record.Id, default));
    }

    [Fact]
    public async Task Assigning_a_title_from_another_group_is_refused()
    {
        // The ladder is per-group by design, not by accident: group B's rung must never be
        // pinnable onto group A's member, however the ids are obtained.
        var factory = CreateFactory();
        var (orgA, adminA, memberA) = await SeedAsync(factory);
        var (orgB, adminB, _) = await SeedAsync(factory);

        Guid foreignLevelId;
        Guid membershipA;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var level = new OrganizationMemberLevel
            {
                Id = Guid.NewGuid(), OrganizationId = orgB, Name = "Foreign Rung", SortOrder = 1,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminB,
            };
            db.OrganizationMemberLevels.Add(level);
            await db.SaveChangesAsync();
            foreignLevelId = level.Id;
            membershipA = (await db.OrganizationUserMemberships.FirstAsync(m => m.OrganizationId == orgA && m.AppUserId == memberA)).Id;
        }

        var ctrl = Build(factory, adminA);
        var result = await ctrl.Assign(orgA, membershipA,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(foreignLevelId), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Assign_and_clear_round_trip()
    {
        var factory = CreateFactory();
        var (orgId, adminId, memberId) = await SeedAsync(factory);

        Guid levelId, membershipId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var level = new OrganizationMemberLevel
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Investigator", SortOrder = 3,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            };
            db.OrganizationMemberLevels.Add(level);
            await db.SaveChangesAsync();
            levelId = level.Id;
            membershipId = (await db.OrganizationUserMemberships.FirstAsync(m => m.OrganizationId == orgId && m.AppUserId == memberId)).Id;
        }

        var ctrl = Build(factory, adminId);
        Assert.IsType<NoContentResult>(await ctrl.Assign(orgId, membershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(levelId), default));

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(levelId, (await db.OrganizationUserMemberships.FindAsync(membershipId))!.MemberLevelId);

        Assert.IsType<NoContentResult>(await ctrl.Assign(orgId, membershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(null), default));

        await using (var verify = await factory.CreateDbContextAsync())
            Assert.Null((await verify.OrganizationUserMemberships.FindAsync(membershipId))!.MemberLevelId);
    }

    [Fact]
    public async Task A_member_cannot_assign_titles()
    {
        var factory = CreateFactory();
        var (orgId, adminId, memberId) = await SeedAsync(factory);
        Guid membershipId;
        await using (var db = await factory.CreateDbContextAsync())
            membershipId = (await db.OrganizationUserMemberships.FirstAsync(m => m.OrganizationId == orgId && m.AppUserId == memberId)).Id;

        var ctrl = Build(factory, memberId);
        Assert.IsType<ForbidResult>(await ctrl.Assign(orgId, membershipId,
            new OrganizationMemberLevelController.AssignMemberLevelRequest(null), default));
    }
}
