using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests the custom endpoints on <see cref="AdminAppUserController"/> that go
/// beyond the base-class CRUD: GetDetail (aggregate), CreateUser (UserManager),
/// and UpdateProfile.
/// </summary>
public class AdminAppUserControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

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
        m.Setup(x => x.Map<AppUserAdminRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not AppUser u)
                 return new AppUserAdminRecord { Id = Guid.Empty, DisplayName = "" };
             return new AppUserAdminRecord { Id = u.Id, DisplayName = u.DisplayName ?? "", Email = u.Email };
         });
        m.Setup(x => x.Map<IReadOnlyList<UserAddressAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<UserEmailAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<UserPhoneAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<UserLinkAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<UserNoteAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<UserMessageAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<OrganizationUserMembershipAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        m.Setup(x => x.Map<IReadOnlyList<UploadFileAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(_ => []);
        return m.Object;
    }

    private static (AdminAppUserController Ctrl, IDbContextFactory<BenDataContext> Factory)
        Build()
    {
        var factory    = CreateFactory();
        var userStore  = new Mock<IUserStore<AppUser>>();
        var userMgr    = new Mock<UserManager<AppUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var auditMock  = new Mock<IAuditLogService>();
        auditMock.Setup(x => x.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        auditMock.Setup(x => x.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var ctrl = new AdminAppUserController(factory, CreateMapper(), auditMock.Object, userMgr.Object,
            new Ben.Data.WebApi.Services.UserHandleService(factory));
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        return (ctrl, factory);
    }

    // ── The sign-in columns on /admin/users (Ben, 2026-09-19) ────────────────

    private static SignInEvent Arrival(Guid userId, DateTime utc, string method, bool ok = true)
        => new() { Id = Guid.NewGuid(), AppUserId = userId, Utc = utc, Succeeded = ok, Method = method };

    /// <summary>
    /// The whole point of the column: every way in counts, and the last one is the latest.
    /// </summary>
    [Fact]
    public async Task The_summary_counts_every_method_and_reports_the_latest()
    {
        var (ctrl, factory) = Build();
        var user = await SeedUserAsync(factory);
        var newest = new DateTime(2026, 9, 19, 18, 0, 0, DateTimeKind.Utc);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SignInEvents.AddRange(
                Arrival(user.Id, newest.AddDays(-3), Ben.Data.WebApi.Services.RecordingSignInManager.PasswordMethod),
                Arrival(user.Id, newest.AddDays(-2), Ben.Data.WebApi.Services.RecordingSignInManager.AppleMethod),
                Arrival(user.Id, newest.AddDays(-1), Ben.Data.WebApi.Services.RecordingSignInManager.HandoffMethod),
                Arrival(user.Id, newest,             Ben.Data.WebApi.Services.RecordingSignInManager.EntraMethod));
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(Ok(await ctrl.GetSignInSummary(default)));
        Assert.Equal(user.Id, row.AppUserId);
        Assert.Equal(4, row.Count);
        Assert.Equal(newest, row.LastUtc);
    }

    /// <summary>
    /// A failed attempt is kept in the table, and is not somebody signing in. Counting it would
    /// make an account under attack look like the site's most active user.
    /// </summary>
    [Fact]
    public async Task A_refused_attempt_is_not_a_sign_in()
    {
        var (ctrl, factory) = Build();
        var user = await SeedUserAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SignInEvents.AddRange(
                Arrival(user.Id, DateTime.UtcNow.AddHours(-2), Ben.Data.WebApi.Services.RecordingSignInManager.PasswordMethod),
                Arrival(user.Id, DateTime.UtcNow.AddHours(-1), Ben.Data.WebApi.Services.RecordingSignInManager.PasswordMethod, ok: false));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, Assert.Single(Ok(await ctrl.GetSignInSummary(default))).Count);
    }

    /// <summary>
    /// Accounts with nothing recorded are absent, not returned as zeroes — the screen says
    /// "Never" for anybody it finds no row for, which is the same fact without the freight.
    /// </summary>
    [Fact]
    public async Task Somebody_who_has_never_signed_in_has_no_row()
    {
        var (ctrl, factory) = Build();
        await SeedUserAsync(factory);

        Assert.Empty(Ok(await ctrl.GetSignInSummary(default)));
    }

    [Fact]
    public async Task Each_account_is_counted_on_its_own()
    {
        var (ctrl, factory) = Build();
        var one = await SeedUserAsync(factory, "One", "one@example.com");
        var two = await SeedUserAsync(factory, "Two", "two@example.com");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SignInEvents.AddRange(
                Arrival(one.Id, DateTime.UtcNow.AddHours(-3), Ben.Data.WebApi.Services.RecordingSignInManager.PasswordMethod),
                Arrival(one.Id, DateTime.UtcNow.AddHours(-2), Ben.Data.WebApi.Services.RecordingSignInManager.PasswordMethod),
                Arrival(two.Id, DateTime.UtcNow.AddHours(-1), Ben.Data.WebApi.Services.RecordingSignInManager.EntraMethod));
            await db.SaveChangesAsync();
        }

        var rows = Ok(await ctrl.GetSignInSummary(default)).ToDictionary(r => r.AppUserId);
        Assert.Equal(2, rows[one.Id].Count);
        Assert.Equal(1, rows[two.Id].Count);
    }

    private static IEnumerable<Ben.Service.Models.Admin.UserSignInSummary> Ok(
        ActionResult<IEnumerable<Ben.Service.Models.Admin.UserSignInSummary>> result)
        => Assert.IsAssignableFrom<IEnumerable<Ben.Service.Models.Admin.UserSignInSummary>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

    private static async Task<AppUser> SeedUserAsync(IDbContextFactory<BenDataContext> factory,
        string displayName = "Test User", string email = "test@example.com")
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), DisplayName = displayName, Email = email,
            UserName = email, DateCreated = DateTime.UtcNow,
        };
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ── GetDetail ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDetail_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var (ctrl, _) = Build();

        var result = await ctrl.GetDetail(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetDetail_ReturnsAggregate_WhenUserExists()
    {
        var (ctrl, factory) = Build();
        var user            = await SeedUserAsync(factory, "Alice", "alice@example.com");

        var result = await ctrl.GetDetail(user.Id, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<AppUserDetailAdminRecord>(ok.Value);
        Assert.Equal(user.Id, detail.User.Id);
        Assert.Equal("Alice", detail.User.DisplayName);
    }

    [Fact]
    public async Task GetDetail_ReturnsEmptyLists_WhenUserHasNoRelatedData()
    {
        var (ctrl, factory) = Build();
        var user            = await SeedUserAsync(factory);

        var result = await ctrl.GetDetail(user.Id, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<AppUserDetailAdminRecord>(ok.Value);
        Assert.Empty(detail.Addresses);
        Assert.Empty(detail.Emails);
        Assert.Empty(detail.UploadFiles);
        Assert.Empty(detail.Memberships);
    }

    // ── CreateUser ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenUserManagerFails()
    {
        var (ctrl, _) = Build();

        // Deliberately corrupt the UserManager mock to return a failure
        // — the controller is already constructed, so we test the failure path
        // by passing an empty password which Identity rejects.
        // Since UserManager is mocked, we need to rely on the mock returning
        // a failed result. Rebuild with explicit mock setup:
        var factory   = CreateFactory();
        var store     = new Mock<IUserStore<AppUser>>();
        var userMgr   = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userMgr.Setup(x => x.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "WeakPw", Description = "Too weak." }));
        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var c = new AdminAppUserController(factory, CreateMapper(), audit.Object, userMgr.Object,
            new Ben.Data.WebApi.Services.UserHandleService(factory));
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = await c.CreateUser(
            new AdminCreateUserRequest("fail@test.com", "weak", "Fail User", null, false, false), default);

        // A sentence, not a JSON list: the New User page shows the server's reason as prose and drops anything that
        // looks like JSON, which is how every refusal used to read "the server rejected the request".
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Too weak.", Assert.IsType<string>(bad.Value));
    }

    [Fact]
    public async Task CreateUser_Returns201_WhenIdentitySucceeds()
    {
        var factory  = CreateFactory();
        var store    = new Mock<IUserStore<AppUser>>();
        var userMgr  = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        AppUser? created = null;
        userMgr.Setup(x => x.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Success)
               .Callback<AppUser, string>((u, _) => { u.Id = Guid.NewGuid(); created = u; });
        userMgr.Setup(x => x.AddToRoleAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Success);
        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var c = new AdminAppUserController(factory, CreateMapper(), audit.Object, userMgr.Object,
            new Ben.Data.WebApi.Services.UserHandleService(factory));
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = await c.CreateUser(
            new AdminCreateUserRequest("new@test.com", "Str0ng!Pass", "New User", null, true, false), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        // C1 (site evaluation 2026-09-06): an administrator-created account used to carry a null
        // @name until the next API restart's backfill ran, and could not be mentioned until then.
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Handle));
    }

    // ── UpdateProfile ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfile_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var (ctrl, _) = Build();

        var result = await ctrl.UpdateProfile(Guid.NewGuid(),
            new AdminUpdateUserProfileRequest("New", null, null, null, false, false, false,
                null, DateTime.UtcNow, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateProfile_UpdatesDisplayName()
    {
        var (ctrl, factory) = Build();
        var user            = await SeedUserAsync(factory, "Old Name");

        var result = await ctrl.UpdateProfile(user.Id,
            new AdminUpdateUserProfileRequest("New Name", null, null, null, true, false, false,
                null, DateTime.UtcNow, null), default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<AppUserAdminRecord>(ok.Value);
        Assert.Equal("New Name", record.DisplayName);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.AppUsers.FindAsync(user.Id);
        Assert.Equal("New Name", saved!.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_SetsEmailConfirmed()
    {
        var (ctrl, factory) = Build();
        var user            = await SeedUserAsync(factory);

        await ctrl.UpdateProfile(user.Id,
            new AdminUpdateUserProfileRequest(null, null, null, null, true, false, false,
                null, user.DateCreated, null), default);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.AppUsers.FindAsync(user.Id);
        Assert.True(saved!.EmailConfirmed);
    }

    // ── Site roles (item 216) ─────────────────────────────────────────────────
    //
    // Until this endpoint existed the only way into Admin or Moderator was a row typed into
    // AspNetUserRoles by hand. The guards below are the ones a database edit would never have
    // enforced: nobody strips their own SuperAdmin role, and nobody strips the last one.

    private sealed record RolesRig(
        AdminAppUserController Ctrl,
        IDbContextFactory<BenDataContext> Factory,
        Mock<UserManager<AppUser>> UserMgr,
        Mock<IAuditLogService> Audit);

    /// <summary>
    /// A controller whose UserManager answers role questions from <paramref name="held"/>,
    /// whose database knows the three seeded roles, and whose caller is <paramref name="callerId"/>.
    /// </summary>
    private static async Task<(RolesRig Rig, AppUser User)> BuildForRolesAsync(
        IEnumerable<string> held, Guid? callerId = null, int superAdminCount = 2)
    {
        var factory = CreateFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var name in new[] { "SuperAdmin", "Admin", "Moderator" })
                db.Roles.Add(new IdentityRole<Guid>(name) { Id = Guid.NewGuid(), NormalizedName = name.ToUpperInvariant() });
            await db.SaveChangesAsync();
        }

        var user = new AppUser { Id = Guid.NewGuid(), Email = "target@example.com", UserName = "target@example.com", DisplayName = "Target" };

        var store   = new Mock<IUserStore<AppUser>>();
        var userMgr = new Mock<UserManager<AppUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userMgr.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userMgr.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(held.ToList());
        userMgr.Setup(x => x.GetUsersInRoleAsync("SuperAdmin"))
               .ReturnsAsync(Enumerable.Range(0, superAdminCount).Select(_ => new AppUser { Id = Guid.NewGuid() }).ToList());
        userMgr.Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(x => x.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);

        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var ctrl = new AdminAppUserController(factory, CreateMapper(), audit.Object, userMgr.Object,
            new Ben.Data.WebApi.Services.UserHandleService(factory));
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (callerId is { } caller)
        {
            http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, caller.ToString())], "test"));
        }
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };

        return (new RolesRig(ctrl, factory, userMgr, audit), user);
    }

    [Fact]
    public async Task SetRoles_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var (rig, _) = await BuildForRolesAsync([]);

        var result = await rig.Ctrl.SetRoles(Guid.NewGuid(), new AdminSetUserRolesRequest(["Admin"]), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SetRoles_ReturnsBadRequest_ForARoleTheSiteDoesNotDefine()
    {
        var (rig, user) = await BuildForRolesAsync([]);

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest(["Wizard"]), default);

        // A sentence naming the role, which the Site Roles tab shows as it stands. It used to show the site's two
        // SuperAdmin rules for every refusal, whatever had actually gone wrong (Ben, 2026-09-15).
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Wizard", Assert.IsType<string>(bad.Value));
        rig.UserMgr.Verify(x => x.AddToRolesAsync(It.IsAny<AppUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task SetRoles_AddsTheMissing_RemovesTheUnwanted_BumpsTheStamp_AndAudits()
    {
        var (rig, user) = await BuildForRolesAsync(["Admin"], callerId: Guid.NewGuid());

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest(["Moderator"]), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsType<AppUserRolesAdminRecord>(ok.Value);
        Assert.Equal(["Moderator"], roles.Roles);

        rig.UserMgr.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.SequenceEqual(new[] { "Moderator" }))), Times.Once);
        rig.UserMgr.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.SequenceEqual(new[] { "Admin" }))), Times.Once);
        // The bearer token carries the claims minted at sign-in; the stamp is what makes an
        // existing session notice the change at its next refresh.
        rig.UserMgr.Verify(x => x.UpdateSecurityStampAsync(user), Times.Once);
        rig.Audit.Verify(x => x.LogUpdateAsync("AppUserRoles", user.Id, It.IsAny<object>(), It.IsAny<object>(),
            It.IsAny<Guid>(), It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Identity throws rather than failing when a role row's lookup name does not match its name, or when a membership
    /// is already there. That reached the Site Roles page as a 500 with no body, which the page could only report as
    /// "the server did not say why" (Ben, 2026-09-15).
    /// </summary>
    [Fact]
    public async Task SetRoles_SaysWhatTheAccountSystemThrew_RatherThanFailingWithNothing()
    {
        var (rig, user) = await BuildForRolesAsync([], callerId: Guid.NewGuid());
        rig.UserMgr.Setup(x => x.AddToRolesAsync(It.IsAny<AppUser>(), It.IsAny<IEnumerable<string>>()))
                   .ThrowsAsync(new InvalidOperationException("Role SuperAdmin does not exist."));

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest(["SuperAdmin"]), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Role SuperAdmin does not exist.", Assert.IsType<string>(bad.Value));
    }

    [Fact]
    public async Task SetRoles_CanonicalisesCase_ToTheStoredRoleName()
    {
        var (rig, user) = await BuildForRolesAsync([]);

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest(["moderator", "MODERATOR"]), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsType<AppUserRolesAdminRecord>(ok.Value);
        Assert.Equal(["Moderator"], roles.Roles);
        rig.UserMgr.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.SequenceEqual(new[] { "Moderator" }))), Times.Once);
    }

    [Fact]
    public async Task SetRoles_IsANoOp_WhenNothingChanges()
    {
        var (rig, user) = await BuildForRolesAsync(["Admin", "Moderator"]);

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest(["Moderator", "Admin"]), default);

        Assert.IsType<OkObjectResult>(result.Result);
        rig.UserMgr.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<AppUser>()), Times.Never);
        rig.Audit.Verify(x => x.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetRoles_RefusesToRemoveYourOwnSuperAdminRole()
    {
        var (rig, user) = await BuildForRolesAsync(["SuperAdmin"], superAdminCount: 5);
        // The caller IS the target.
        rig.Ctrl.ControllerContext.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest([]), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        rig.UserMgr.Verify(x => x.RemoveFromRolesAsync(It.IsAny<AppUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task SetRoles_RefusesToRemoveTheLastSuperAdmin()
    {
        var (rig, user) = await BuildForRolesAsync(["SuperAdmin"], callerId: Guid.NewGuid(), superAdminCount: 1);

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest(["Admin"]), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        rig.UserMgr.Verify(x => x.RemoveFromRolesAsync(It.IsAny<AppUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        rig.UserMgr.Verify(x => x.AddToRolesAsync(It.IsAny<AppUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task SetRoles_LetsAnotherSuperAdminRemoveIt_WhenTheyAreNotTheLast()
    {
        var (rig, user) = await BuildForRolesAsync(["SuperAdmin"], callerId: Guid.NewGuid(), superAdminCount: 2);

        var result = await rig.Ctrl.SetRoles(user.Id, new AdminSetUserRolesRequest([]), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsType<AppUserRolesAdminRecord>(ok.Value);
        Assert.Empty(roles.Roles);
        rig.UserMgr.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.SequenceEqual(new[] { "SuperAdmin" }))), Times.Once);
    }

    [Fact]
    public async Task GetDetail_IncludesTheSiteRolesHeld()
    {
        var factory = CreateFactory();
        var user    = await SeedUserAsync(factory, "Rolly", "rolly@example.com");

        var store   = new Mock<IUserStore<AppUser>>();
        var userMgr = new Mock<UserManager<AppUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userMgr.Setup(x => x.GetRolesAsync(It.Is<AppUser>(u => u.Id == user.Id))).ReturnsAsync(["Moderator", "Admin"]);
        var audit = new Mock<IAuditLogService>();

        var ctrl = new AdminAppUserController(factory, CreateMapper(), audit.Object, userMgr.Object,
            new Ben.Data.WebApi.Services.UserHandleService(factory));
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = await ctrl.GetDetail(user.Id, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<AppUserDetailAdminRecord>(ok.Value);
        Assert.Equal(["Admin", "Moderator"], detail.Roles);
    }
}
