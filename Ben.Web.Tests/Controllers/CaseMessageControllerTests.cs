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
/// Tests for CaseMessageController — org↔client message board.
/// </summary>
public class CaseMessageControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseMessageController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseMessageController(factory);
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

    private static CaseMessageController BuildAnonymous(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new CaseMessageController(factory);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid caseId, Guid userId)> SeedAsync()
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
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, caseId, userId);
    }

    private static async Task SeedClientMessage(IDbContextFactory<BenDataContext> factory, Guid caseId, Guid clientId, bool readByOrg = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.CaseMessages.Add(new CaseMessage
        {
            Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = clientId,
            Body = "Hello from client", SenderSide = CaseMessageSide.Client,
            IsReadByClient = true, IsReadByOrg = readByOrg,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();
    }

    // ── GetMessages ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_Unauthenticated_ReturnsUnauthorized()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var ctrl = BuildAnonymous(factory);

        var result = await ctrl.GetMessages(orgId, caseId, default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetMessages_NonMember_ReturnsNotFound()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.GetMessages(orgId, caseId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetMessages_Member_ReturnsEmptyList()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.GetMessages(orgId, caseId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<CaseMessageRecord>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetMessages_MarksUnreadClientMessagesAsRead()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await SeedClientMessage(factory, caseId, Guid.NewGuid(), readByOrg: false);
        var ctrl = BuildController(factory, userId);

        await ctrl.GetMessages(orgId, caseId, default);

        await using var db = await factory.CreateDbContextAsync();
        var msg = await db.CaseMessages.FirstAsync(m => m.CaseId == caseId);
        Assert.True(msg.IsReadByOrg);
    }

    // ── PostMessage ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PostMessage_ValidBody_ReturnsOkWithRecord()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.PostMessage(orgId, caseId, new PostCaseMessageRequest("Hello client!"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseMessageRecord>(ok.Value);
        Assert.Equal("Hello client!", dto.Body);
        Assert.Equal(CaseMessageSide.Organization, dto.SenderSide);
        Assert.Equal(userId, dto.AuthorAppUserId);
        Assert.True(dto.IsReadByOrg);
        Assert.False(dto.IsReadByClient);
    }

    [Fact]
    public async Task PostMessage_EmptyBody_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.PostMessage(orgId, caseId, new PostCaseMessageRequest("   "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PostMessage_NonMember_ReturnsNotFound()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.PostMessage(orgId, caseId, new PostCaseMessageRequest("Hi"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task PostMessage_PersistsToDatabase()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        await ctrl.PostMessage(orgId, caseId, new PostCaseMessageRequest("Saved message"), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CaseMessages.AnyAsync(m => m.CaseId == caseId && m.Body == "Saved message"));
    }

    // ── GetUnreadCount ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCount_NoMessages_ReturnsZero()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.GetUnreadCount(orgId, caseId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, ok.Value);
    }

    [Fact]
    public async Task GetUnreadCount_WithUnreadClientMessages_ReturnsCorrectCount()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var clientId = Guid.NewGuid();
        await SeedClientMessage(factory, caseId, clientId, readByOrg: false);
        await SeedClientMessage(factory, caseId, clientId, readByOrg: false);
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.GetUnreadCount(orgId, caseId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, ok.Value);
    }

    [Fact]
    public async Task GetUnreadCount_AlreadyReadMessages_ReturnsZero()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await SeedClientMessage(factory, caseId, Guid.NewGuid(), readByOrg: true);
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.GetUnreadCount(orgId, caseId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, ok.Value);
    }
}
