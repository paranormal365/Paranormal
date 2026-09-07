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
/// Tests for ClientRequestController — draft/submit/withdraw lifecycle and org application tracking.
/// </summary>
public class ClientRequestControllerTests
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
        m.Setup(x => x.Map<ClientRequestRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is ClientRequest r
                ? new ClientRequestRecord { Id = r.Id, AppUserId = r.AppUserId, Status = r.Status, StreetAddress1 = r.StreetAddress1, City = r.City, State = r.State, ZipCode = r.ZipCode, Country = r.Country, Description = r.Description, DateCreated = r.DateCreated, CreatedByAppUserId = r.CreatedByAppUserId }
                : new ClientRequestRecord { StreetAddress1 = "", City = "", State = "", ZipCode = "", Country = "", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<ClientRequestRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<ClientRequest> list
                ? list.Select(r => new ClientRequestRecord { Id = r.Id, AppUserId = r.AppUserId, Status = r.Status, StreetAddress1 = r.StreetAddress1, City = r.City, State = r.State, ZipCode = r.ZipCode, Country = r.Country, DateCreated = r.DateCreated, CreatedByAppUserId = r.CreatedByAppUserId })
                : []);
        m.Setup(x => x.Map<IEnumerable<ClientRequestOrganizationRecord>>(It.IsAny<object>()))
            .Returns<object>(_ => []);
        return m.Object;
    }

    private static ClientRequestController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new ClientRequestController(factory, CreateMapper());
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

    private static async Task<(IDbContextFactory<BenDataContext>, Guid userId, Guid orgId)> SeedAsync()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId, IsAcceptingClients = true });
        await db.SaveChangesAsync();
        return (factory, userId, orgId);
    }

    private static UpsertClientRequestRequest MakeRequest(decimal? lat = null, decimal? lon = null, string? description = null) =>
        new("123 Main", null, "Nashville", "TN", "37201", "US", lat, lon, ClientGender.NotProvided, null, description);

    // ── GetMine ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMine_ReturnsOnlyOwnRequests()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Create(MakeRequest(), default);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMine(default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<ClientRequestRecord>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetMine_OtherUser_ReturnsEmpty()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Create(MakeRequest(), default);

        var other = Build(factory, Guid.NewGuid());
        var ok    = Assert.IsType<OkObjectResult>((await other.GetMine(default)).Result);
        Assert.Empty((IEnumerable<ClientRequestRecord>)ok.Value!);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsCreatedAsDraft()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Create(MakeRequest(), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ClientRequestRecord>(created.Value);
        Assert.Equal(ClientRequestStatus.Draft, dto.Status);
        Assert.Equal(userId, dto.AppUserId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Draft_UpdatesFields()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(), default)).Result!).Value!).Id;

        var result = await ctrl.Update(reqId, MakeRequest(description: "Updated description"), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ClientRequestRecord>(ok.Value);
        Assert.Equal("Updated description", dto.Description);
    }

    [Fact]
    public async Task Update_OtherUser_ReturnsForbid()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(), default)).Result!).Value!).Id;

        var other = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>((await other.Update(reqId, MakeRequest(), default)).Result);
    }

    [Fact]
    public async Task Update_NotDraft_ReturnsBadRequest()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);

        Assert.IsType<BadRequestObjectResult>((await ctrl.Update(reqId, MakeRequest(), default)).Result);
    }

    // ── Submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_ValidRequest_ChangesStatusToSubmitted()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;

        var result = await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ClientRequestRecord>(ok.Value);
        Assert.Equal(ClientRequestStatus.Submitted, dto.Status);
    }

    [Fact]
    public async Task Submit_NoGeocode_ReturnsBadRequest()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(description: "Desc"), default)).Result!).Value!).Id;

        Assert.IsType<BadRequestObjectResult>((await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default)).Result);
    }

    [Fact]
    public async Task Submit_TooManyOrgs_ReturnsBadRequest()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(), default)).Result!).Value!).Id;

        Assert.IsType<BadRequestObjectResult>((await ctrl.Submit(reqId, new SubmitClientRequestRequest([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]), default)).Result);
    }

    [Fact]
    public async Task Submit_CreatesOrgApplications()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.ClientRequestOrganizations.AnyAsync(a => a.ClientRequestId == reqId && a.OrganizationId == orgId));
    }

    // ── Withdraw ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_ChangesStatusToWithdrawn()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(), default)).Result!).Value!).Id;

        var result = await ctrl.Withdraw(reqId, default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ClientRequestRecord>(ok.Value);
        Assert.Equal(ClientRequestStatus.Withdrawn, dto.Status);
    }

    [Fact]
    public async Task Withdraw_OtherUser_ReturnsForbid()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(), default)).Result!).Value!).Id;

        var other = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>((await other.Withdraw(reqId, default)).Result);
    }

    [Fact]
    public async Task Withdraw_CancelsStillOpenOrgApplications()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);

        await ctrl.Withdraw(reqId, default);

        await using var db = await factory.CreateDbContextAsync();
        var app = await db.ClientRequestOrganizations.FirstAsync(a => a.ClientRequestId == reqId && a.OrganizationId == orgId);
        Assert.Equal(ClientOrgRequestStatus.Cancelled, app.Status);
    }

    [Fact]
    public async Task Withdraw_DoesNotChangeAlreadyRejectedApplications()
    {
        // A Declined request's application is already Rejected — withdrawing it shouldn't touch that row.
        var (factory, userId, rejectedOrgId, _, reqId) = await SeedDeclinedAsync();
        var ctrl = Build(factory, userId);

        await ctrl.Withdraw(reqId, default);

        await using var db = await factory.CreateDbContextAsync();
        var app = await db.ClientRequestOrganizations.FirstAsync(a => a.ClientRequestId == reqId && a.OrganizationId == rejectedOrgId);
        Assert.Equal(ClientOrgRequestStatus.Rejected, app.Status);
    }

    // ── AddOrganization ──────────────────────────────────────────────────────

    /// <summary>Seeds a request Declined after one rejected org application, plus a second accepting org to resubmit to.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid userId, Guid rejectedOrgId, Guid newOrgId, Guid reqId)> SeedDeclinedAsync()
    {
        var (factory, userId, rejectedOrgId) = await SeedAsync();
        var newOrgId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = newOrgId, Name = "New Org", UrlName = "new", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId, IsAcceptingClients = true });
            await db.SaveChangesAsync();
        }

        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([rejectedOrgId]), default);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var app = await db.ClientRequestOrganizations.FirstAsync(a => a.ClientRequestId == reqId);
            app.Status = ClientOrgRequestStatus.Rejected;
            var req = await db.ClientRequests.FirstAsync(r => r.Id == reqId);
            req.Status = ClientRequestStatus.Declined;
            await db.SaveChangesAsync();
        }

        return (factory, userId, rejectedOrgId, newOrgId, reqId);
    }

    [Fact]
    public async Task AddOrganization_NotDeclined_ReturnsBadRequest()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var newOrgId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = newOrgId, Name = "New Org", UrlName = "new", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId, IsAcceptingClients = true });
            await db.SaveChangesAsync();
        }
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);

        var result = await ctrl.AddOrganization(reqId, new AddOrganizationRequest(newOrgId), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddOrganization_Declined_Succeeds_AndReopensAsSubmitted()
    {
        var (factory, userId, _, newOrgId, reqId) = await SeedDeclinedAsync();
        var ctrl = Build(factory, userId);

        var result = await ctrl.AddOrganization(reqId, new AddOrganizationRequest(newOrgId), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ClientRequestRecord>(ok.Value);
        Assert.Equal(ClientRequestStatus.Submitted, dto.Status);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.ClientRequestOrganizations.AnyAsync(a => a.ClientRequestId == reqId && a.OrganizationId == newOrgId));
    }

    [Fact]
    public async Task AddOrganization_AtCap_ReturnsBadRequest()
    {
        var (factory, userId, _, newOrgId, reqId) = await SeedDeclinedAsync();
        var thirdOrgId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = thirdOrgId, Name = "Third Org", UrlName = "third", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId, IsAcceptingClients = true });
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory, userId);
        await ctrl.AddOrganization(reqId, new AddOrganizationRequest(newOrgId), default); // fills the 2nd slot

        var result = await ctrl.AddOrganization(reqId, new AddOrganizationRequest(thirdOrgId), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddOrganization_DuplicateOrg_ReturnsBadRequest()
    {
        var (factory, userId, rejectedOrgId, _, reqId) = await SeedDeclinedAsync();
        var ctrl = Build(factory, userId);

        var result = await ctrl.AddOrganization(reqId, new AddOrganizationRequest(rejectedOrgId), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddOrganization_Withdrawn_Succeeds_AndReopensAsSubmitted()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var newOrgId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = newOrgId, Name = "New Org", UrlName = "new", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId, IsAcceptingClients = true });
            await db.SaveChangesAsync();
        }
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Haunting"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);
        await ctrl.Withdraw(reqId, default);

        var result = await ctrl.AddOrganization(reqId, new AddOrganizationRequest(newOrgId), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ClientRequestRecord>(ok.Value);
        Assert.Equal(ClientRequestStatus.Submitted, dto.Status);

        await using var db2 = await factory.CreateDbContextAsync();
        Assert.True(await db2.ClientRequestOrganizations.AnyAsync(a => a.ClientRequestId == reqId && a.OrganizationId == newOrgId));
    }

    [Fact]
    public async Task AddOrganization_OtherUser_ReturnsForbid()
    {
        var (factory, _, _, newOrgId, reqId) = await SeedDeclinedAsync();
        var other = Build(factory, Guid.NewGuid());

        var result = await other.AddOrganization(reqId, new AddOrganizationRequest(newOrgId), default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── Deleting a draft (site evaluation 2026-09-06, W-R5) ──────────────────

    [Fact]
    public async Task DeleteDraft_RemovesTheDraft()
    {
        var (factory, userId, _) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(), default)).Result!).Value!).Id;

        var result = await ctrl.DeleteDraft(reqId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ClientRequests.ToListAsync());
    }

    [Fact]
    public async Task DeleteDraft_RefusesASubmittedRequest()
    {
        // A request a group has received is withdrawn, not erased — deleting it would take the
        // application rows with it and leave the group's list with a hole it cannot explain.
        var (factory, userId, orgId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await ctrl.Create(MakeRequest(36.17m, -86.78m, "Knocking"), default)).Result!).Value!).Id;
        await ctrl.Submit(reqId, new SubmitClientRequestRequest([orgId]), default);

        var result = await ctrl.DeleteDraft(reqId, default);

        Assert.IsType<BadRequestObjectResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.ClientRequests.ToListAsync());
    }

    [Fact]
    public async Task DeleteDraft_RefusesSomebodyElsesDraft()
    {
        var (factory, userId, _) = await SeedAsync();
        var reqId = ((ClientRequestRecord)((CreatedAtActionResult)(await Build(factory, userId).Create(MakeRequest(), default)).Result!).Value!).Id;

        var result = await Build(factory, Guid.NewGuid()).DeleteDraft(reqId, default);

        Assert.IsType<ForbidResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.ClientRequests.ToListAsync());
    }

    // ── Parked requests (site evaluation 2026-09-06, phase 1) ────────────────
    //
    // A request typed into the signed-out wizard under an address that already has an account is
    // parked, not written to that account. These cover the only door back: the emailed link.

    private const string ParkedSecret = "a-secret-from-the-email";

    private static async Task<Guid> SeedPendingAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, string normalizedEmail = "U@T.COM",
        string secret = ParkedSecret, TimeSpan? age = null)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.PendingClientRequests.Add(new PendingClientRequest
        {
            Id                  = id,
            NormalizedEmail     = normalizedEmail,
            SecretHash          = Ben.Data.WebApi.Controllers.Public.PublicClientRequestController.HashSecret(secret),
            DisplayName         = "Casey Miller",
            StreetAddress1      = "2500 West End Ave",
            City                = "Nashville",
            State               = "TN",
            ZipCode             = "37203",
            Country             = "US",
            Latitude            = 36.1627m,
            Longitude           = -86.7816m,
            Description         = "<p>Three knocks, 2am.</p>",
            OrganizationIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { orgId }),
            DateCreated         = DateTime.UtcNow - (age ?? TimeSpan.Zero),
            DateExpires         = DateTime.UtcNow - (age ?? TimeSpan.Zero) + TimeSpan.FromDays(14),
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task AdoptPending_MakesItTheirOwnSubmittedRequest()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var pendingId = await SeedPendingAsync(factory, orgId);

        var result = await Build(factory, userId).AdoptPending(pendingId, ParkedSecret, default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var request = await db.ClientRequests.SingleAsync();
        Assert.Equal(userId, request.AppUserId);
        Assert.Equal(ClientRequestStatus.Submitted, request.Status);
        Assert.Equal("2500 West End Ave", request.StreetAddress1);
        Assert.Equal(orgId, (await db.ClientRequestOrganizations.SingleAsync()).OrganizationId);

        // The parked row is gone, so the link cannot make a second copy.
        Assert.Empty(await db.PendingClientRequests.ToListAsync());
    }

    [Fact]
    public async Task AdoptPending_RefusesTheWrongKey()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var pendingId = await SeedPendingAsync(factory, orgId);

        var result = await Build(factory, userId).AdoptPending(pendingId, "not-the-secret", default);

        Assert.IsType<NotFoundResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ClientRequests.ToListAsync());
    }

    [Fact]
    public async Task AdoptPending_RefusesADifferentAccount()
    {
        // The whole point: the link is not a bearer token. Even holding it, an account that is
        // not the one the request named gets nothing — the request is about somebody's home.
        var (factory, _, orgId) = await SeedAsync();
        var pendingId = await SeedPendingAsync(factory, orgId, normalizedEmail: "SOMEBODY.ELSE@T.COM");

        var (_, otherId) = (0, Guid.NewGuid());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = otherId, UserName = "other@t.com", NormalizedUserName = "OTHER@T.COM",
                Email = "other@t.com", NormalizedEmail = "OTHER@T.COM", DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, otherId).AdoptPending(pendingId, ParkedSecret, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.ClientRequests.ToListAsync());
        Assert.Single(await check.PendingClientRequests.ToListAsync());
    }

    [Fact]
    public async Task AdoptPending_RefusesAnExpiredRow()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var pendingId = await SeedPendingAsync(factory, orgId, age: TimeSpan.FromDays(15));

        var result = await Build(factory, userId).AdoptPending(pendingId, ParkedSecret, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPending_ShowsTheAddressButNotTheStory()
    {
        // What was written about the activity is not this reader's to have until they say the
        // request is theirs. The address is, because it is what tells them whether it is.
        var (factory, userId, orgId) = await SeedAsync();
        var pendingId = await SeedPendingAsync(factory, orgId);

        var result = await Build(factory, userId).GetPending(pendingId, ParkedSecret, default);

        var record = Assert.IsType<PendingClientRequestRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("2500 West End Ave", record.StreetAddress1);
        Assert.Equal("Casey Miller", record.DisplayName);
        Assert.Equal("Test Org", Assert.Single(record.OrganizationNames));
        Assert.DoesNotContain("knocks", System.Text.Json.JsonSerializer.Serialize(record));
    }

    [Fact]
    public async Task DiscardPending_LeavesTheAccountAlone()
    {
        var (factory, userId, orgId) = await SeedAsync();
        var pendingId = await SeedPendingAsync(factory, orgId);

        var result = await Build(factory, userId).DiscardPending(pendingId, ParkedSecret, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.PendingClientRequests.ToListAsync());
        Assert.Empty(await db.ClientRequests.ToListAsync());
    }
}
