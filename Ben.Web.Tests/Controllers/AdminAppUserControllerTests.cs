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
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);
        auditMock.Setup(x => x.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        var ctrl = new AdminAppUserController(factory, CreateMapper(), auditMock.Object, userMgr.Object);
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        return (ctrl, factory);
    }

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
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        var c = new AdminAppUserController(factory, CreateMapper(), audit.Object, userMgr.Object);
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = await c.CreateUser(
            new AdminCreateUserRequest("fail@test.com", "weak", "Fail User", null, false, false), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateUser_Returns201_WhenIdentitySucceeds()
    {
        var factory  = CreateFactory();
        var store    = new Mock<IUserStore<AppUser>>();
        var userMgr  = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userMgr.Setup(x => x.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Success)
               .Callback<AppUser, string>((u, _) => u.Id = Guid.NewGuid());
        userMgr.Setup(x => x.AddToRoleAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Success);
        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        var c = new AdminAppUserController(factory, CreateMapper(), audit.Object, userMgr.Object);
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = await c.CreateUser(
            new AdminCreateUserRequest("new@test.com", "Str0ng!Pass", "New User", null, true, false), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
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
}
