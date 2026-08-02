using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
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
/// Tests for ScheduleProposalController — org sends date proposals, client responds.
/// </summary>
public class ScheduleProposalControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static ScheduleProposalController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new ScheduleProposalController(factory);
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

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid caseId, Guid userId)> SeedBasicCase()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Manager, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Test Case",
            CaseYear = 2026, OrgCaseNumber = 1, StreetAddress1 = "123 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, caseId, userId);
    }

    [Fact]
    public async Task CreateProposal_ReturnsOk_WithSlots()
    {
        var (factory, orgId, caseId, userId) = await SeedBasicCase();
        var ctrl    = BuildController(factory, userId);
        var request = new CreateProposalRequest("Please pick a date", [
            new SlotInput(DateTime.UtcNow.AddDays(14), DateTime.UtcNow.AddDays(14).AddHours(3)),
            new SlotInput(DateTime.UtcNow.AddDays(21), null),
        ]);

        var result = await ctrl.Create(orgId, caseId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ScheduleProposalDto>(ok.Value);
        Assert.Equal(ScheduleProposalStatus.Pending, dto.Status);
        Assert.Equal(2, dto.Slots.Count);
        Assert.Equal("Please pick a date", dto.Notes);
    }

    [Fact]
    public async Task CreateProposal_Returns400_WhenNoSlots()
    {
        var (factory, orgId, caseId, userId) = await SeedBasicCase();
        var ctrl   = BuildController(factory, userId);
        var result = await ctrl.Create(orgId, caseId, new CreateProposalRequest(null, []), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task WithdrawProposal_SetsWithdrawnStatus()
    {
        var (factory, orgId, caseId, userId) = await SeedBasicCase();
        var ctrl = BuildController(factory, userId);

        // Create first
        var createResult = await ctrl.Create(orgId, caseId,
            new CreateProposalRequest(null, [new SlotInput(DateTime.UtcNow.AddDays(10), null)]),
            CancellationToken.None);
        var dto = ((OkObjectResult)createResult.Result!).Value as ScheduleProposalDto;

        // Withdraw
        var withdrawResult = await ctrl.Withdraw(orgId, caseId, dto!.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(withdrawResult);

        await using var db = await factory.CreateDbContextAsync();
        var proposal = await db.InvestigationScheduleProposals.FindAsync([dto.Id]);
        Assert.Equal(ScheduleProposalStatus.Withdrawn, proposal!.Status);
    }

    [Fact]
    public async Task GetAll_ReturnsList()
    {
        var (factory, orgId, caseId, userId) = await SeedBasicCase();
        var ctrl = BuildController(factory, userId);
        await ctrl.Create(orgId, caseId, new CreateProposalRequest(null, [new SlotInput(DateTime.UtcNow.AddDays(7), null)]), CancellationToken.None);

        var result = await ctrl.GetAll(orgId, caseId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var items  = Assert.IsAssignableFrom<IEnumerable<ScheduleProposalDto>>(ok.Value);
        Assert.Single(items);
    }
}
