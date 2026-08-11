using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for CaseInviteController — the public accept-side of the sub-client email invite flow
/// (item #4's remaining piece), the counterpart to MyCaseController's invite-management endpoints.
/// </summary>
public class CaseInviteControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static Mock<UserManager<AppUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static CaseInviteController Build(IDbContextFactory<BenDataContext> factory,
        Mock<UserManager<AppUser>>? userManager = null, Guid? userId = null)
    {
        var ctrl = new CaseInviteController(factory, (userManager ?? CreateUserManagerMock()).Object,
            new Mock<IAuditLogService>().Object);
        var identity = userId.HasValue
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer")
            : new ClaimsIdentity(); // anonymous — no authenticationType
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return ctrl;
    }

    /// <summary>Seeds an org, a case with a primary client, and one pending invite.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid caseId, Guid inviterId, CaseClientInvite invite)>
        SeedPendingInviteAsync(string email = "invitee@t.com")
    {
        var factory  = CreateFactory();
        var inviterId = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var caseId   = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = inviterId, UserName = "inviter@t.com", NormalizedUserName = "INVITER@T.COM", Email = "inviter@t.com", NormalizedEmail = "INVITER@T.COM", DisplayName = "Inviter Person", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = inviterId });
        var clientReq = new ClientRequest { Id = Guid.NewGuid(), AppUserId = inviterId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Desc", Status = ClientRequestStatus.Assigned, DateCreated = DateTime.UtcNow, CreatedByAppUserId = inviterId };
        db.ClientRequests.Add(clientReq);
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, ClientRequestId = clientReq.Id,
            Title = "Test Case", CaseYear = DateTime.UtcNow.Year, OrgCaseNumber = 1, Status = CaseStatus.Accepted,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = inviterId,
        });
        var invite = new CaseClientInvite
        {
            Id = Guid.NewGuid(), CaseId = caseId, Email = email, Token = Guid.NewGuid().ToString("N"),
            DateExpires = DateTime.UtcNow.AddDays(14), DateCreated = DateTime.UtcNow, CreatedByAppUserId = inviterId,
        };
        db.CaseClientInvites.Add(invite);
        await db.SaveChangesAsync();

        return (factory, caseId, inviterId, invite);
    }

    // ── GetInfo ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInfo_UnknownToken_ReturnsNotFound()
    {
        var (factory, _, _, _) = await SeedPendingInviteAsync();
        var ctrl = Build(factory);
        var result = await ctrl.GetInfo("no-such-token", default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetInfo_Pending_ReturnsValidStatusAndAccountExistsFalse()
    {
        var (factory, caseId, _, invite) = await SeedPendingInviteAsync();
        var ctrl = Build(factory);

        var result = await ctrl.GetInfo(invite.Token, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var info = Assert.IsType<InviteInfoRecord>(ok.Value);
        Assert.Equal(InviteStatus.Valid, info.Status);
        Assert.Equal(caseId, info.CaseId);
        Assert.Equal("Test Case", info.CaseTitle);
        Assert.Equal("Inviter Person", info.InviterDisplayName);
        Assert.False(info.AccountExists);
    }

    [Fact]
    public async Task GetInfo_EmailAlreadyHasAccount_ReturnsAccountExistsTrue()
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync("existing@t.com");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "existing@t.com", NormalizedUserName = "EXISTING@T.COM", Email = "existing@t.com", NormalizedEmail = "EXISTING@T.COM", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetInfo(invite.Token, default)).Result);
        Assert.True(((InviteInfoRecord)ok.Value!).AccountExists);
    }

    [Theory]
    [InlineData(true, false, InviteStatus.Used)]
    [InlineData(false, true, InviteStatus.Revoked)]
    public async Task GetInfo_UsedOrRevoked_ReturnsCorrectStatus(bool accepted, bool revoked, InviteStatus expected)
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.CaseClientInvites.FirstAsync(i => i.Id == invite.Id);
            if (accepted) row.DateAccepted = DateTime.UtcNow;
            if (revoked) row.DateRevoked = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetInfo(invite.Token, default)).Result);
        Assert.Equal(expected, ((InviteInfoRecord)ok.Value!).Status);
    }

    [Fact]
    public async Task GetInfo_Expired_ReturnsExpiredStatus()
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.CaseClientInvites.FirstAsync(i => i.Id == invite.Id);
            row.DateExpires = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetInfo(invite.Token, default)).Result);
        Assert.Equal(InviteStatus.Expired, ((InviteInfoRecord)ok.Value!).Status);
    }

    // ── Accept (new account) ─────────────────────────────────────────────────

    [Fact]
    public async Task Accept_ValidInvite_CreatesUserAndCaseClientAccessAndStampsAccepted()
    {
        var (factory, caseId, _, invite) = await SeedPendingInviteAsync();
        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByEmailAsync(invite.Email)).ReturnsAsync((AppUser?)null);
        AppUser? createdUser = null;
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
          .Callback<AppUser, string>((u, _) => createdUser = u)
          .ReturnsAsync(IdentityResult.Success);

        var ctrl = Build(factory, um);
        var result = await ctrl.Accept(invite.Token, new AcceptInviteRequest("New Person", "P@ssw0rd123"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AcceptInviteResult>(ok.Value);
        Assert.Equal(caseId, dto.CaseId);
        Assert.NotNull(createdUser);
        Assert.Equal(invite.Email, createdUser!.Email);
        Assert.True(createdUser.EmailConfirmed);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CaseClientAccesses.AnyAsync(a => a.CaseId == caseId && a.AppUserId == createdUser.Id));
        var row = await db.CaseClientInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.NotNull(row.DateAccepted);
        Assert.Equal(createdUser.Id, row.AcceptedByAppUserId);
    }

    [Fact]
    public async Task Accept_EmailAlreadyHasAccount_ReturnsConflict()
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync();
        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByEmailAsync(invite.Email)).ReturnsAsync(new AppUser { Id = Guid.NewGuid(), Email = invite.Email });

        var ctrl = Build(factory, um);
        var result = await ctrl.Accept(invite.Token, new AcceptInviteRequest("New Person", "P@ssw0rd123"), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Accept_UsedOrRevoked_ReturnsBadRequest(bool accepted, bool revoked)
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.CaseClientInvites.FirstAsync(i => i.Id == invite.Id);
            if (accepted) row.DateAccepted = DateTime.UtcNow;
            if (revoked) row.DateRevoked = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory);
        var result = await ctrl.Accept(invite.Token, new AcceptInviteRequest("New Person", "P@ssw0rd123"), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Accept_IdentityCreateFails_SurfacesErrors()
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync();
        var um = CreateUserManagerMock();
        um.Setup(m => m.FindByEmailAsync(invite.Email)).ReturnsAsync((AppUser?)null);
        um.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak." }));

        var ctrl = Build(factory, um);
        var result = await ctrl.Accept(invite.Token, new AcceptInviteRequest("New Person", "weak"), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── AcceptExisting (signed-in user) ──────────────────────────────────────

    [Fact]
    public async Task AcceptExisting_SignedInUser_CreatesAccessAndStampsAccepted()
    {
        var (factory, caseId, _, invite) = await SeedPendingInviteAsync();
        var signedInUserId = Guid.NewGuid();
        var ctrl = Build(factory, userId: signedInUserId);

        var result = await ctrl.AcceptExisting(invite.Token, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(caseId, ((AcceptInviteResult)ok.Value!).CaseId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CaseClientAccesses.AnyAsync(a => a.CaseId == caseId && a.AppUserId == signedInUserId));
        var row = await db.CaseClientInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.Equal(signedInUserId, row.AcceptedByAppUserId);
    }

    [Fact]
    public async Task AcceptExisting_AlreadyHasAccess_IsIdempotentAndDoesNotDuplicateAccess()
    {
        var (factory, caseId, _, invite) = await SeedPendingInviteAsync();
        var signedInUserId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = signedInUserId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = signedInUserId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, userId: signedInUserId);
        var result = await ctrl.AcceptExisting(invite.Token, default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal(1, await verifyDb.CaseClientAccesses.CountAsync(a => a.CaseId == caseId && a.AppUserId == signedInUserId));
    }

    [Fact]
    public async Task AcceptExisting_Anonymous_ReturnsUnauthorized()
    {
        var (factory, _, _, invite) = await SeedPendingInviteAsync();
        var ctrl = Build(factory); // no userId — anonymous identity
        var result = await ctrl.AcceptExisting(invite.Token, default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }
}
