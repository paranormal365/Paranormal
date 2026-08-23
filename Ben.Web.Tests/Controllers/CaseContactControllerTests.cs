using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Case contacts (item 158): the fallback that keeps "who do I talk to" from ever being empty,
/// the members-only rule, and the case-edit write gate.
/// </summary>
public sealed class CaseContactControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseContactController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseContactController(factory);
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

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId,
        Guid AdminId, Guid ManagerId, Guid MemberId);

    private static async Task<World> SeedAsync(bool withManager = true)
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = "g", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.AppUsers.AddRange(
            new AppUser { Id = adminId, UserName = adminId.ToString(), DisplayName = "Admin" },
            new AppUser { Id = managerId, UserName = managerId.ToString(), DisplayName = "Manager Mel" },
            new AppUser { Id = memberId, UserName = memberId.ToString(), DisplayName = "Member Max" });
        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = managerId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Case", CaseYear = 2026, OrgCaseNumber = 9,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            CaseManagerAppUserId = withManager ? managerId : null,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        await db.SaveChangesAsync();
        return new World(factory, orgId, caseId, adminId, managerId, memberId);
    }

    [Fact]
    public async Task With_no_explicit_contact_the_case_manager_stands_in()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.MemberId);

        var ok = Assert.IsType<OkObjectResult>((await ctrl.GetAll(w.OrgId, w.CaseId, default)).Result);
        var contact = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CaseContactRecord>>(ok.Value));
        Assert.Equal("Manager Mel", contact.DisplayName);
        Assert.True(contact.IsFallback);
    }

    [Fact]
    public async Task An_explicit_contact_replaces_the_fallback_and_clearing_returns_to_it()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        var ok = Assert.IsType<OkObjectResult>((await ctrl.SetAll(w.OrgId, w.CaseId,
            new SetCaseContactsRequest([w.MemberId]), default)).Result);
        var contact = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CaseContactRecord>>(ok.Value));
        Assert.Equal("Member Max", contact.DisplayName);
        Assert.False(contact.IsFallback);

        ok = Assert.IsType<OkObjectResult>((await ctrl.SetAll(w.OrgId, w.CaseId,
            new SetCaseContactsRequest([]), default)).Result);
        contact = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CaseContactRecord>>(ok.Value));
        Assert.True(contact.IsFallback);
    }

    [Fact]
    public async Task A_non_member_cannot_be_made_a_contact()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        var result = await ctrl.SetAll(w.OrgId, w.CaseId,
            new SetCaseContactsRequest([Guid.NewGuid()]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task The_case_manager_may_set_contacts_but_a_plain_member_may_not()
    {
        var w = await SeedAsync();

        Assert.IsType<OkObjectResult>((await Build(w.Factory, w.ManagerId).SetAll(w.OrgId, w.CaseId,
            new SetCaseContactsRequest([w.MemberId]), default)).Result);
        Assert.IsType<ForbidResult>((await Build(w.Factory, w.MemberId).SetAll(w.OrgId, w.CaseId,
            new SetCaseContactsRequest([w.MemberId]), default)).Result);
    }

    [Fact]
    public async Task A_stranger_cannot_even_read_the_contacts()
    {
        var w = await SeedAsync();
        Assert.IsType<ForbidResult>((await Build(w.Factory, Guid.NewGuid()).GetAll(w.OrgId, w.CaseId, default)).Result);
    }

    [Fact]
    public async Task With_no_manager_and_no_contacts_the_list_is_honestly_empty()
    {
        var w = await SeedAsync(withManager: false);
        var ok = Assert.IsType<OkObjectResult>((await Build(w.Factory, w.MemberId).GetAll(w.OrgId, w.CaseId, default)).Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<CaseContactRecord>>(ok.Value));
    }
}
