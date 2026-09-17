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
        var ctrl = new CaseMessageController(factory, new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), Ben.Data.WebApi.Services.LinkPreviews.LinkPreviewWarmer.None);
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
        var ctrl = new CaseMessageController(factory, new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), Ben.Data.WebApi.Services.LinkPreviews.LinkPreviewWarmer.None);
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
        // The org's half of the client thread follows the case grant now: reading it is Case.Read
        // and answering the client in the group's name is Case.Update (IH-03 step 2). The suite's
        // subject is the message board, so its member is seeded able to work the case; refusal is
        // covered by ReadDoesNotGrantDestructionTests and the tests below.
        await TestSeeds.BridgeAsync(factory, orgId, TestSeeds.CaseWork);
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

    // ── Formatted messages (2026-09-14): Body stays plain for the iPhone app, BodyHtml is added ─────────

    [Fact]
    public async Task PostMessage_Html_StoresBothForms_AndBodyIsThePlainWords()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await BuildController(factory, userId).PostMessage(orgId, caseId,
            new PostCaseMessageRequest(BodyHtml: "<p><strong>Visit</strong> moved to Friday.</p><ul><li>Bring keys</li></ul>"), default);

        var dto = Assert.IsType<CaseMessageRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("<p><strong>Visit</strong> moved to Friday.</p><ul><li>Bring keys</li></ul>", dto.BodyHtml);
        Assert.Equal("Visit moved to Friday.\n\nBring keys", dto.Body);
        Assert.DoesNotContain("<", dto.Body);

        await using var db = await factory.CreateDbContextAsync();
        var stored = await db.CaseMessages.SingleAsync(m => m.Id == dto.Id);
        Assert.Equal(dto.Body, stored.Body);
        Assert.Equal(dto.BodyHtml, stored.BodyHtml);
    }

    [Fact]
    public async Task PostMessage_Html_WithAScript_StoresNeitherFormWithIt()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await BuildController(factory, userId).PostMessage(orgId, caseId,
            new PostCaseMessageRequest(BodyHtml: "<p>Hi</p><script>alert(1)</script><img src=x onerror=\"steal()\">"), default);

        var dto = Assert.IsType<CaseMessageRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.DoesNotContain("script", dto.BodyHtml!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", dto.BodyHtml!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", dto.Body);
    }

    [Fact]
    public async Task PostMessage_PlainBodyOnly_IsStoredAsBefore_WithNoHtml()
    {
        // What the shipped iPhone app sends.
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await BuildController(factory, userId).PostMessage(orgId, caseId,
            new PostCaseMessageRequest("Line one\nline two"), default);

        var dto = Assert.IsType<CaseMessageRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Line one\nline two", dto.Body);
        Assert.Null(dto.BodyHtml);
    }

    [Theory]
    [InlineData("<p></p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task PostMessage_AnEmptiedEditor_IsRefused(string html)
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await BuildController(factory, userId).PostMessage(orgId, caseId,
            new PostCaseMessageRequest(BodyHtml: html), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>Records what a post asked to have previews made for.</summary>
    private sealed class RecordingWarmer : Ben.Data.WebApi.Services.LinkPreviews.ILinkPreviewWarmer
    {
        public readonly List<(string? Text, string? Html, Guid User)> Calls = [];
        public void WarmFrom(string? text, string? html, Guid userId) => Calls.Add((text, html, userId));
    }

    [Fact]
    public async Task PostMessage_AsksForTheCardsOfItsLinks_AfterItIsSaved()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var warmer = new RecordingWarmer();
        var ctrl = new CaseMessageController(factory, new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), warmer)
        {
            ControllerContext = BuildController(factory, userId).ControllerContext,
        };

        await ctrl.PostMessage(orgId, caseId, new PostCaseMessageRequest(BodyHtml: "<p>See <a href=\"https://example.com/deed\">the deed</a></p>"), default);

        var call = Assert.Single(warmer.Calls);
        Assert.Equal(userId, call.User);
        Assert.Single(Ben.Data.WebApi.Services.LinkPreviews.LinkPreviewWarmer.LinksIn(call.Text, call.Html), "https://example.com/deed");
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

    // ── A lapse stops writing, not reading (2026-09-17 audit) ────────────────────────────────
    //
    // The guard's own sentence promises "everything already here stays readable. Renewing brings
    // everything back exactly as it was." Both GETs on this controller asked WhyReadOnlyAsync and
    // returned BadRequest, while all nine other call sites in the tree are writes. So a lapsed
    // group could not read its own client thread — and the phone routes these GETs, maps the
    // prose to messagesProblem and renders it, which put the word "Renewing" on an iPhone screen.
    // PaidPlan documents that as an App Review 3.1.1 risk.

    private static async Task<Guid> SeedClientAccountAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id, UserName = $"client{id:N}@t", Email = "client@t",
            DisplayName = "A Client", DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task LapseAsync(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Status = SubscriptionStatus.Lapsed,
            Interval = BillingInterval.Monthly,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMessages_WhenLapsed_StillReadsTheThread()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await SeedClientMessage(factory, caseId, await SeedClientAccountAsync(factory));
        await LapseAsync(factory, orgId, userId);

        var result = await BuildController(factory, userId).GetMessages(orgId, caseId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<CaseMessageRecord>>(ok.Value));
    }

    [Fact]
    public async Task GetUnreadCount_WhenLapsed_StillCounts()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await SeedClientMessage(factory, caseId, Guid.NewGuid());
        await LapseAsync(factory, orgId, userId);

        var result = await BuildController(factory, userId).GetUnreadCount(orgId, caseId, default);

        Assert.Equal(1, Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    /// <summary>
    /// The one write on a read path pauses instead. Marking a client's message read is a claim
    /// that somebody dealt with it, so a lapsed group reading the thread must not consume it.
    /// </summary>
    [Fact]
    public async Task GetMessages_WhenLapsed_DoesNotMarkTheClientsMessageRead()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await SeedClientMessage(factory, caseId, Guid.NewGuid());
        await LapseAsync(factory, orgId, userId);

        await BuildController(factory, userId).GetMessages(orgId, caseId, default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CaseMessages.AnyAsync(m => m.CaseId == caseId && !m.IsReadByOrg));
    }

    [Fact]
    public async Task GetMessages_WhenPaying_MarksTheClientsMessageRead()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await SeedClientMessage(factory, caseId, Guid.NewGuid());

        await BuildController(factory, userId).GetMessages(orgId, caseId, default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseMessages.AnyAsync(m => m.CaseId == caseId && !m.IsReadByOrg));
    }

    /// <summary>And the write is still refused, which is the rule item 84 actually states.</summary>
    [Fact]
    public async Task PostMessage_WhenLapsed_IsStillRefused()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        await LapseAsync(factory, orgId, userId);

        var result = await BuildController(factory, userId)
            .PostMessage(orgId, caseId, new PostCaseMessageRequest(Body: "Trying to answer"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
