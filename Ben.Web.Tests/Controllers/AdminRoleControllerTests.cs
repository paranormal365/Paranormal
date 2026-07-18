using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Unit tests for AdminRoleController — site-level role management (GET / POST / DELETE).
/// </summary>
public class AdminRoleControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<RoleManager<IdentityRole<Guid>>> CreateRoleManagerMock()
    {
        var store = new Mock<IRoleStore<IdentityRole<Guid>>>();
        return new Mock<RoleManager<IdentityRole<Guid>>>(
            store.Object,
            /* IEnumerable<IRoleValidator<IdentityRole<Guid>>> */ null!,
            /* ILookupNormalizer                               */ null!,
            /* IdentityErrorDescriber                          */ null!,
            /* ILogger<RoleManager<IdentityRole<Guid>>>        */ null!);
    }

    private static Mock<UserManager<AppUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<IMapper> CreateMapperMock()
    {
        var mock = new Mock<IMapper>();
        mock.Setup(m => m.Map<AppRoleAdminRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is IdentityRole<Guid> role
                ? new AppRoleAdminRecord { Id = role.Id, Name = role.Name, NormalizedName = role.NormalizedName }
                : new AppRoleAdminRecord());
        return mock;
    }

    private static AdminRoleController BuildController(
        Mock<RoleManager<IdentityRole<Guid>>>? roleManagerMock = null,
        Mock<UserManager<AppUser>>? userManagerMock = null,
        Mock<IMapper>? mapperMock = null)
    {
        roleManagerMock ??= CreateRoleManagerMock();
        userManagerMock ??= CreateUserManagerMock();
        mapperMock      ??= CreateMapperMock();
        return new AdminRoleController(roleManagerMock.Object, userManagerMock.Object, mapperMock.Object);
    }

    private static IdentityRole<Guid> MakeRole(string name) =>
        new IdentityRole<Guid>(name) { Id = Guid.NewGuid(), NormalizedName = name.ToUpperInvariant() };

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsRolesOrderedByNameWithUserCounts()
    {
        var adminRole = MakeRole("Admin");
        var superRole = MakeRole("SuperAdmin");

        var roleManagerMock  = CreateRoleManagerMock();
        var userManagerMock  = CreateUserManagerMock();

        roleManagerMock.Setup(m => m.Roles)
            .Returns(new[] { superRole, adminRole }.AsQueryable());

        userManagerMock
            .Setup(m => m.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync([new AppUser(), new AppUser()]);
        userManagerMock
            .Setup(m => m.GetUsersInRoleAsync("SuperAdmin"))
            .ReturnsAsync([new AppUser()]);

        var controller = BuildController(roleManagerMock, userManagerMock);

        var result = await controller.GetAll(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<AdminRoleWithCountResponse>>(ok.Value).ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal("Admin", list[0].Role.Name);    // ordered by name
        Assert.Equal(2, list[0].UserCount);
        Assert.Equal("SuperAdmin", list[1].Role.Name);
        Assert.Equal(1, list[1].UserCount);
    }

    [Fact]
    public async Task GetAll_WhenNoRoles_ReturnsEmptyList()
    {
        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock.Setup(m => m.Roles)
            .Returns(Array.Empty<IdentityRole<Guid>>().AsQueryable());

        var controller = BuildController(roleManagerMock);

        var result = await controller.GetAll(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<AdminRoleWithCountResponse>>(ok.Value);
        Assert.Empty(list);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithValidName_ReturnsCreatedAndRole()
    {
        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.CreateAsync(It.IsAny<IdentityRole<Guid>>()))
            .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(roleManagerMock);

        var result = await controller.Create(new AdminCreateRoleRequest("Moderator"));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<AppRoleAdminRecord>(created.Value);
        Assert.Equal("Moderator", record.Name);
    }

    [Fact]
    public async Task Create_WithBlankName_ReturnsBadRequest()
    {
        var controller = BuildController();

        var result = await controller.Create(new AdminCreateRoleRequest("   "));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("required", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WhenRoleManagerFails_ReturnsBadRequest()
    {
        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.CreateAsync(It.IsAny<IdentityRole<Guid>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role already exists." }));

        var controller = BuildController(roleManagerMock);

        var result = await controller.Create(new AdminCreateRoleRequest("Duplicate"));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var errors = Assert.IsAssignableFrom<IEnumerable<string>>(bad.Value);
        Assert.Contains("Role already exists.", errors);
    }

    [Fact]
    public async Task Create_TrimsWhitespaceFromName()
    {
        IdentityRole<Guid>? captured = null;

        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.CreateAsync(It.IsAny<IdentityRole<Guid>>()))
            .Callback<IdentityRole<Guid>>(r => captured = r)
            .ReturnsAsync(IdentityResult.Success);

        var controller = BuildController(roleManagerMock);

        await controller.Create(new AdminCreateRoleRequest("  Editor  "));

        Assert.NotNull(captured);
        Assert.Equal("Editor", captured.Name);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WhenRoleNotFound_ReturnsNotFound()
    {
        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((IdentityRole<Guid>?)null);

        var controller = BuildController(roleManagerMock);

        var result = await controller.Delete(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_WhenUsersAssigned_ReturnsConflict()
    {
        var role = MakeRole("SuperAdmin");

        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.FindByIdAsync(role.Id.ToString()))
            .ReturnsAsync(role);

        var userManagerMock = CreateUserManagerMock();
        userManagerMock
            .Setup(m => m.GetUsersInRoleAsync("SuperAdmin"))
            .ReturnsAsync([new AppUser(), new AppUser(), new AppUser()]);

        var controller = BuildController(roleManagerMock, userManagerMock);

        var result = await controller.Delete(role.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("3", conflict.Value?.ToString());
    }

    [Fact]
    public async Task Delete_WhenRoleIsEmpty_DeletesAndReturnsNoContent()
    {
        var role = MakeRole("OldRole");

        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.FindByIdAsync(role.Id.ToString()))
            .ReturnsAsync(role);
        roleManagerMock
            .Setup(m => m.DeleteAsync(role))
            .ReturnsAsync(IdentityResult.Success);

        var userManagerMock = CreateUserManagerMock();
        userManagerMock
            .Setup(m => m.GetUsersInRoleAsync("OldRole"))
            .ReturnsAsync([]);

        var controller = BuildController(roleManagerMock, userManagerMock);

        var result = await controller.Delete(role.Id);

        Assert.IsType<NoContentResult>(result);
        roleManagerMock.Verify(m => m.DeleteAsync(role), Times.Once);
    }

    [Fact]
    public async Task Delete_WhenRoleManagerDeleteFails_ReturnsBadRequest()
    {
        var role = MakeRole("BrokenRole");

        var roleManagerMock = CreateRoleManagerMock();
        roleManagerMock
            .Setup(m => m.FindByIdAsync(role.Id.ToString()))
            .ReturnsAsync(role);
        roleManagerMock
            .Setup(m => m.DeleteAsync(role))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Delete failed." }));

        var userManagerMock = CreateUserManagerMock();
        userManagerMock
            .Setup(m => m.GetUsersInRoleAsync("BrokenRole"))
            .ReturnsAsync([]);

        var controller = BuildController(roleManagerMock, userManagerMock);

        var result = await controller.Delete(role.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errors = Assert.IsAssignableFrom<IEnumerable<string>>(bad.Value);
        Assert.Contains("Delete failed.", errors);
    }
}
