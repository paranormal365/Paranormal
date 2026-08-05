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
    public async Task AddOrganization_OtherUser_ReturnsForbid()
    {
        var (factory, _, _, newOrgId, reqId) = await SeedDeclinedAsync();
        var other = Build(factory, Guid.NewGuid());

        var result = await other.AddOrganization(reqId, new AddOrganizationRequest(newOrgId), default);
        Assert.IsType<ForbidResult>(result.Result);
    }
}
