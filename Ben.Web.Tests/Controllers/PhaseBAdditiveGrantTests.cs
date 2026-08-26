using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 156 Phase B: formerly admin-only writes open ADDITIVELY to role grants. Each test is the
/// same sentence three ways — a plain member is refused, the same member with the named grant
/// gets through, and the admin path is untouched.
/// </summary>
public sealed class PhaseBAdditiveGrantTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid AdminId, Guid MemberId, Guid MembershipId);

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = "g", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.AppUsers.AddRange(
            new AppUser { Id = adminId, UserName = adminId.ToString() },
            new AppUser { Id = memberId, UserName = memberId.ToString() });
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = membershipId, OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();
        return new World(factory, orgId, adminId, memberId, membershipId);
    }

    /// <summary>Gives the member a custom role holding one grant.</summary>
    private static async Task GrantAsync(World w, DataTable table, DataAction actions)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var role = new OrganizationRole
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrgId, Name = "Granted Role",
            IsActive = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
        };
        db.OrganizationRoles.Add(role);
        db.OrganizationRolePermissions.Add(new OrganizationRolePermission
        {
            Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
            TableName = table, Actions = actions,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
        });
        db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
        {
            Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
            OrganizationUserMembershipId = w.MembershipId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.AdminId,
        });
        await db.SaveChangesAsync();
    }

    private static T WithUser<T>(T ctrl, Guid userId) where T : ControllerBase
    {
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

    private static CaseController Cases(World w, Guid userId)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<Ben.Service.Models.Entities.CaseRecord>(It.IsAny<object>()))
              .Returns(new Ben.Service.Models.Entities.CaseRecord { Title = "x" });
        return WithUser(new CaseController(w.Factory, mapper.Object,
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(w.Factory),
            new OrganizationSecurityService(w.Factory),
            new Ben.Data.WebApi.Services.RequestReviewNotifier(w.Factory, new Ben.Data.WebApi.Services.PlatformMessageService(w.Factory))), userId);
    }

    private static OrgCalendarEventTypeController EventTypes(World w, Guid userId)
        => WithUser(new OrgCalendarEventTypeController(w.Factory, new Mock<IMapper>().Object,
            new OrganizationSecurityService(w.Factory)), userId);

    private static CreateCaseRequest NewCase() => new(
        "T", null, "1 Main", null, "Nashville", "TN", "37201", "US", null, null);

    [Fact]
    public async Task A_Case_Create_grant_opens_case_creation_to_a_plain_member()
    {
        var w = await SeedAsync();

        Assert.IsType<ForbidResult>((await Cases(w, w.MemberId).Create(w.OrgId, NewCase(), default)).Result);

        await GrantAsync(w, DataTable.Case, DataAction.Create);
        var result = await Cases(w, w.MemberId).Create(w.OrgId, NewCase(), default);
        Assert.IsNotType<ForbidResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Cases.CountAsync());
    }

    [Fact]
    public async Task A_ClientRequest_Update_grant_lets_a_member_decline_a_request()
    {
        var w = await SeedAsync();
        Guid requestId;
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var cr = new ClientRequest
            {
                Id = Guid.NewGuid(), AppUserId = Guid.NewGuid(), Status = ClientRequestStatus.Submitted,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            };
            db.ClientRequests.Add(cr);
            db.ClientRequestOrganizations.Add(new ClientRequestOrganization
            {
                Id = Guid.NewGuid(), ClientRequestId = cr.Id, OrganizationId = w.OrgId,
                Status = ClientOrgRequestStatus.Pending,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = cr.AppUserId,
            });
            await db.SaveChangesAsync();
            requestId = cr.Id;
        }

        Assert.IsType<ForbidResult>(await Cases(w, w.MemberId).DeclineClientRequest(w.OrgId, requestId, default));

        await GrantAsync(w, DataTable.ClientRequest, DataAction.Update);
        Assert.IsNotType<ForbidResult>(await Cases(w, w.MemberId).DeclineClientRequest(w.OrgId, requestId, default));
    }

    [Fact]
    public async Task An_OrgCalendar_Create_grant_lets_a_member_add_an_event_type()
    {
        var w = await SeedAsync();
        var request = new UpsertCalendarEventTypeRequest("Vigil", null, null, 1, true);

        Assert.IsType<ForbidResult>((await EventTypes(w, w.MemberId).Create(w.OrgId, request, default)).Result);

        await GrantAsync(w, DataTable.OrgCalendar, DataAction.Create);
        Assert.IsNotType<ForbidResult>((await EventTypes(w, w.MemberId).Create(w.OrgId, request, default)).Result);
    }

    [Fact]
    public async Task The_admin_path_is_untouched_by_all_of_this()
    {
        var w = await SeedAsync();
        Assert.IsNotType<ForbidResult>((await Cases(w, w.AdminId).Create(w.OrgId, NewCase(), default)).Result);
        Assert.IsNotType<ForbidResult>((await EventTypes(w, w.AdminId).Create(w.OrgId,
            new UpsertCalendarEventTypeRequest("Vigil", null, null, 1, true), default)).Result);
    }

    [Fact]
    public async Task A_grant_on_the_wrong_table_opens_nothing()
    {
        // The additive gate must be per-table, not "any grant anywhere".
        var w = await SeedAsync();
        await GrantAsync(w, DataTable.Equipment, DataAction.Create);

        Assert.IsType<ForbidResult>((await Cases(w, w.MemberId).Create(w.OrgId, NewCase(), default)).Result);
    }
}
