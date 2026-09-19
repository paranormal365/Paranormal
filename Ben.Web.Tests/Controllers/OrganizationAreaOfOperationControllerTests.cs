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
/// Tests for OrganizationAreaOfOperationController — upsert, delete, and acceptance flags.
/// </summary>
public class OrganizationAreaOfOperationControllerTests
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
        m.Setup(x => x.Map<OrganizationAreaOfOperationRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrganizationAreaOfOperation a
                ? new OrganizationAreaOfOperationRecord { Id = a.Id, OrganizationId = a.OrganizationId, RadiusMiles = a.RadiusMiles, CenterLatitude = a.CenterLatitude, CenterLongitude = a.CenterLongitude, DisplayLabel = a.DisplayLabel, DateCreated = a.DateCreated }
                : new OrganizationAreaOfOperationRecord { DateCreated = DateTime.UtcNow });
        return m.Object;
    }

    private static OrganizationAreaOfOperationController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new OrganizationAreaOfOperationController(factory, CreateMapper(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
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

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid userId)> SeedAsync(bool makeAdmin = true)
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = makeAdmin ? OrganizationMemberRole.Owner : OrganizationMemberRole.Member,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, userId);
    }

    private static UpsertAreaOfOperationRequest MakeRequest() =>
        new(25.0m, 36.17m, -86.78m, "Nashville TN", true, false);

    // ── Get ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_NonAdmin_ReturnsForbid()
    {
        var (factory, orgId, _) = await SeedAsync(makeAdmin: false);
        var memberId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = memberId, UserName = "m@t.com", NormalizedUserName = "M@T.COM", Email = "m@t.com", NormalizedEmail = "M@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId });
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>((await Build(factory, memberId).Get(orgId, default)).Result);
    }

    [Fact]
    public async Task Get_NotFound_WhenNoneExists()
    {
        var (factory, orgId, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>((await Build(factory, userId).Get(orgId, default)).Result);
    }

    // ── Upsert ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_CreatesNewAreaOfOperation()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Upsert(orgId, MakeRequest(), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<OrganizationAreaOfOperationRecord>(ok.Value);
        Assert.Equal(25.0m, dto.RadiusMiles);
        Assert.Equal(36.17m, dto.CenterLatitude);
        Assert.Equal("Nashville TN", dto.DisplayLabel);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingAreaOfOperation()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Upsert(orgId, MakeRequest(), default);
        var result = await ctrl.Upsert(orgId, new UpsertAreaOfOperationRequest(50.0m, 36.17m, -86.78m, "Updated", false, true), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<OrganizationAreaOfOperationRecord>(ok.Value);
        Assert.Equal(50.0m, dto.RadiusMiles);

        await using var db = await factory.CreateDbContextAsync();
        var count = await db.OrganizationAreaOfOperations.CountAsync(a => a.OrganizationId == orgId);
        Assert.Equal(1, count); // upsert — only one row
    }

    [Fact]
    public async Task Upsert_UpdatesOrgAcceptanceFlags()
    {
        var (factory, orgId, userId) = await SeedAsync();
        await Build(factory, userId).Upsert(orgId, new UpsertAreaOfOperationRequest(25.0m, 36.17m, -86.78m, null, true, true), default);
        await using var db = await factory.CreateDbContextAsync();
        var org = await db.Organizations.FindAsync(orgId);
        Assert.True(org!.IsAcceptingClients);
        Assert.True(org.AcceptsClientsOutsideRange);
    }

    [Fact]
    public async Task Upsert_NonAdmin_ReturnsForbid()
    {
        var (factory, orgId, _) = await SeedAsync(makeAdmin: false);
        var memberId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = memberId, UserName = "m@t.com", NormalizedUserName = "M@T.COM", Email = "m@t.com", NormalizedEmail = "M@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId });
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>((await Build(factory, memberId).Upsert(orgId, MakeRequest(), default)).Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingArea_ReturnsNoContent()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Upsert(orgId, MakeRequest(), default);
        Assert.IsType<NoContentResult>(await ctrl.Delete(orgId, default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.OrganizationAreaOfOperations.AnyAsync(a => a.OrganizationId == orgId));
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var (factory, orgId, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>(await Build(factory, userId).Delete(orgId, default));
    }

    // ── UpdateAcceptance ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAcceptance_UpdatesOrgFlags()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var result = await Build(factory, userId).UpdateAcceptance(orgId, new UpdateClientAcceptanceRequest(true, true), default);
        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        var org = await db.Organizations.FindAsync(orgId);
        Assert.True(org!.IsAcceptingClients);
        Assert.True(org.AcceptsClientsOutsideRange);
    }
}
