using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
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
/// Item 171 (Ben, 2026-08-23: "the gates count as tabs"): the stats panel's numbers follow the
/// same read gates as the tabs that list them. A seat the Cases tab is hidden from receives
/// NULL for the case numbers — never zero, which would read as an idle group — and the member
/// count stays baseline for every member.
/// </summary>
public sealed class OrganizationStatsControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>One org with one member, three cases (one closed) and two investigations.</summary>
    private static async Task<(Guid orgId, Guid memberId)> SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        var orgId    = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = memberId, UserName = memberId.ToString(), Email = $"{memberId}@test.com" });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Org", UrlName = $"org-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId,
        });
        foreach (var status in new[] { CaseStatus.Active, CaseStatus.Active, CaseStatus.Closed })
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Title = "Case", Status = status,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId,
            });
        for (var i = 0; i < 2; i++)
            db.Investigations.Add(new Investigation
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Title = "Inv",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId,
            });
        await db.SaveChangesAsync();
        return (orgId, memberId);
    }

    /// <summary>The controller as one caller, with per-table security answers.</summary>
    private static OrganizationStatsController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId,
        bool canReadCases, bool canReadInvestigations, bool superAdmin = false)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), DataTable.Case, DataAction.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canReadCases);
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), DataTable.Investigation, DataAction.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canReadInvestigations);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (superAdmin) claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));

        var ctrl = new OrganizationStatsController(factory, security.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
                },
            },
        };
        return ctrl;
    }

    private static OrgStatsSummary Body(ActionResult<OrgStatsSummary> result)
        => Assert.IsType<OrgStatsSummary>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task A_member_without_the_case_gate_gets_members_and_null_case_numbers()
    {
        var factory = CreateFactory();
        var (orgId, memberId) = await SeedAsync(factory);

        var stats = Body(await Build(factory, memberId, canReadCases: false, canReadInvestigations: false)
            .Get(orgId, CancellationToken.None));

        Assert.Equal(1, stats.Members);
        Assert.Null(stats.Cases);
        Assert.Null(stats.OpenCases);
        Assert.Null(stats.Investigations);
        Assert.Null(stats.CasesByStatus);
        Assert.Null(stats.CasesPerMonth);
    }

    [Fact]
    public async Task A_member_with_both_gates_gets_the_full_numbers()
    {
        var factory = CreateFactory();
        var (orgId, memberId) = await SeedAsync(factory);

        var stats = Body(await Build(factory, memberId, canReadCases: true, canReadInvestigations: true)
            .Get(orgId, CancellationToken.None));

        Assert.Equal(3, stats.Cases);
        Assert.Equal(2, stats.OpenCases);   // Closed is a resting state
        Assert.Equal(2, stats.Investigations);
        Assert.NotNull(stats.CasesByStatus);
        Assert.NotNull(stats.CasesPerMonth);
    }

    [Fact]
    public async Task The_two_gates_are_independent()
    {
        var factory = CreateFactory();
        var (orgId, memberId) = await SeedAsync(factory);

        var stats = Body(await Build(factory, memberId, canReadCases: false, canReadInvestigations: true)
            .Get(orgId, CancellationToken.None));

        Assert.Null(stats.Cases);
        Assert.Equal(2, stats.Investigations);
    }

    [Fact]
    public async Task A_non_member_is_refused_outright()
    {
        var factory = CreateFactory();
        var (orgId, _) = await SeedAsync(factory);

        var result = await Build(factory, Guid.NewGuid(), canReadCases: true, canReadInvestigations: true)
            .Get(orgId, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task A_superadmin_gets_the_full_numbers_without_membership()
    {
        var factory = CreateFactory();
        var (orgId, _) = await SeedAsync(factory);

        var stats = Body(await Build(factory, Guid.NewGuid(),
                canReadCases: false, canReadInvestigations: false, superAdmin: true)
            .Get(orgId, CancellationToken.None));

        Assert.Equal(3, stats.Cases);
        Assert.Equal(2, stats.Investigations);
    }
}
