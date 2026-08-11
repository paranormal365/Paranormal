using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
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

/// <summary>
/// Tests for OrganizationAddressCrudController — org address CRUD and the member-access list.
/// <para>
/// Phase-B regression focus: <c>GetMemberAccess</c> and <c>RemoveMemberAccess</c> checked the
/// caller's CMS permission for the route orgId, but never that <c>addressId</c> actually
/// belonged to that org (unlike their own sibling <c>AddMemberAccess</c>, which does) — a real
/// admin of their OWN org could read or delete another org's address member-access list just by
/// knowing/guessing an addressId.
/// </para>
/// </summary>
public class OrganizationAddressCrudControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrganizationAddressRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrganizationAddress a
             ? new OrganizationAddressRecord
             {
                 Id = a.Id, OrganizationId = a.OrganizationId, OrganizationAddressTypeId = a.OrganizationAddressTypeId,
                 StreetAddress1 = a.StreetAddress1, City = a.City, State = a.State, ZipCode = a.ZipCode, Country = a.Country,
                 SortOrder = a.SortOrder, DateCreated = a.DateCreated, CreatedByAppUserId = a.CreatedByAppUserId,
             }
             : new OrganizationAddressRecord { StreetAddress1 = "", City = "", State = "", ZipCode = "", Country = "" });
        m.Setup(x => x.Map<IEnumerable<OrganizationAddressRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<OrganizationAddress> list
             ? list.Select(a => new OrganizationAddressRecord
             {
                 Id = a.Id, OrganizationId = a.OrganizationId, OrganizationAddressTypeId = a.OrganizationAddressTypeId,
                 StreetAddress1 = a.StreetAddress1, City = a.City, State = a.State, ZipCode = a.ZipCode, Country = a.Country,
                 SortOrder = a.SortOrder, DateCreated = a.DateCreated, CreatedByAppUserId = a.CreatedByAppUserId,
             })
             : []);
        m.Setup(x => x.Map<OrganizationAddressMemberAccessRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrganizationAddressMemberAccess x
             ? new OrganizationAddressMemberAccessRecord { Id = x.Id, OrganizationAddressId = x.OrganizationAddressId, OrganizationUserMembershipId = x.OrganizationUserMembershipId, DateCreated = x.DateCreated, CreatedByAppUserId = x.CreatedByAppUserId }
             : new OrganizationAddressMemberAccessRecord());
        m.Setup(x => x.Map<IEnumerable<OrganizationAddressMemberAccessRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<OrganizationAddressMemberAccess> list
             ? list.Select(x => new OrganizationAddressMemberAccessRecord { Id = x.Id, OrganizationAddressId = x.OrganizationAddressId, OrganizationUserMembershipId = x.OrganizationUserMembershipId, DateCreated = x.DateCreated, CreatedByAppUserId = x.CreatedByAppUserId })
             : []);
        return m.Object;
    }

    private static Mock<IOrganizationSecurityService> GrantAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(true);
        return s;
    }

    private static OrganizationAddressCrudController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId, Mock<IOrganizationSecurityService>? security = null)
    {
        security ??= GrantAll();
        var ctrl = new OrganizationAddressCrudController(factory, CreateMapper(), security.Object, new Mock<IAuditLogService>().Object);
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

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, userId);
    }

    private static OrgAddressUpsertRequest MakeRequest() =>
        new(Guid.NewGuid(), "123 Main St", null, "Nashville", "TN", "37201", "US", 10);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsAddress()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);

        var result = await ctrl.Create(orgId, MakeRequest(), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<OrganizationAddressRecord>(created.Value);
        Assert.Equal("123 Main St", dto.StreetAddress1);
        Assert.Equal(orgId, dto.OrganizationId);
    }

    [Fact]
    public async Task GetAll_ReturnsCreatedAddresses()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Create(orgId, MakeRequest(), default);

        var result = await ctrl.GetAll(orgId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationAddressRecord>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetMemberAccess_Empty_ReturnsEmptyList()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = ((OrganizationAddressRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeRequest(), default)).Result!).Value!);

        var result = await ctrl.GetMemberAccess(orgId, created.Id, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<OrganizationAddressMemberAccessRecord>>(ok.Value));
    }

    [Fact]
    public async Task AddMemberAccess_GrantsAccess()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var address = ((OrganizationAddressRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeRequest(), default)).Result!).Value!);

        await using var db = await factory.CreateDbContextAsync();
        var membership = await db.OrganizationUserMemberships.FirstAsync(m => m.OrganizationId == orgId && m.AppUserId == userId);

        var result = await ctrl.AddMemberAccess(orgId, address.Id, new AddAddressMemberAccessRequest(membership.Id), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<OrganizationAddressMemberAccessRecord>(created.Value);
        Assert.Equal(address.Id, dto.OrganizationAddressId);
    }

    // ── Cross-org chain (Phase B) ────────────────────────────────────────────

    [Fact]
    public async Task GetMemberAccessAndRemoveMemberAccess_AddressBelongsToDifferentOrg_ReturnsNotFound()
    {
        var (factory, victimOrgId, victimUserId) = await SeedAsync();
        var victim  = Build(factory, victimUserId);
        var address = ((OrganizationAddressRecord)((CreatedAtActionResult)(await victim.Create(victimOrgId, MakeRequest(), default)).Result!).Value!);

        var accessId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var victimMembership = await db.OrganizationUserMemberships.FirstAsync(m => m.OrganizationId == victimOrgId && m.AppUserId == victimUserId);
            db.OrganizationAddressMemberAccesses.Add(new OrganizationAddressMemberAccess
            {
                Id = accessId, OrganizationAddressId = address.Id, OrganizationUserMembershipId = victimMembership.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = victimUserId,
            });
            await db.SaveChangesAsync();
        }

        var attackerOrgId = Guid.NewGuid();
        var attackerId    = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = attackerOrgId, Name = "Attacker Org", UrlName = "attacker", DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = attackerOrgId, AppUserId = attackerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            await db.SaveChangesAsync();
        }
        var attacker = Build(factory, attackerId); // has real CMS permission (GrantAll mock) — the exact attacker shape

        var getResult = await attacker.GetMemberAccess(attackerOrgId, address.Id, default);
        Assert.IsType<NotFoundResult>(getResult.Result);

        var removeResult = await attacker.RemoveMemberAccess(attackerOrgId, address.Id, accessId, default);
        Assert.IsType<NotFoundResult>(removeResult);

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.True(await verifyDb.OrganizationAddressMemberAccesses.AnyAsync(x => x.Id == accessId));
    }
}
