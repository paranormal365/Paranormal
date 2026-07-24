using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class OrganizationLogoControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static Mock<IMapper> CreateMapperMock()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<IEnumerable<OrganizationLogoRecord>>(It.IsAny<object>()))
         .Returns(Array.Empty<OrganizationLogoRecord>());
        m.Setup(x => x.Map<OrganizationLogoRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is OrganizationLogo l
             ? new OrganizationLogoRecord { Id = l.Id, OrganizationId = l.OrganizationId, UploadFileId = l.UploadFileId, IsActive = l.IsActive }
             : new OrganizationLogoRecord { UploadFileId = Guid.NewGuid() });
        return m;
    }

    private static OrganizationLogoController Build(
        IDbContextFactory<BenDataContext> factory,
        ClaimsPrincipal? principal = null,
        Mock<IOrganizationSecurityService>? security = null)
    {
        security ??= GrantAll();
        var ctrl = new OrganizationLogoController(factory, CreateMapperMock().Object, security.Object, new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = principal ?? Anonymous() } };
        return ctrl;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SuperAdmin(Guid id) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)
        ], "Bearer"));

    private static Mock<IOrganizationSecurityService> GrantAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(true);
        return s;
    }

    private static async Task<Guid> SeedUploadFileAsync(IDbContextFactory<BenDataContext> f)
    {
        await using var db = await f.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
            FileName = "logo.png", StoredFileName = "logo.png", ContentType = "image/png",
            FileSize = 100, FileData = new byte[4],
            IsPublic = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<OrganizationLogo> SeedLogoAsync(
        IDbContextFactory<BenDataContext> f, Guid orgId, bool isActive = false)
    {
        await using var db = await f.CreateDbContextAsync();
        var logo = new OrganizationLogo
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            UploadFileId = Guid.NewGuid(), IsActive = isActive,
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
        };
        db.OrganizationLogos.Add(logo);
        await db.SaveChangesAsync();
        return logo;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenNoUserId_ReturnsUnauthorized()
    {
        var ctrl   = Build(CreateFactory(), Anonymous());
        var result = await ctrl.GetAll(Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_AsSuperAdmin_ReturnsLogos()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        await SeedLogoAsync(factory, orgId, isActive: true);
        var ctrl   = Build(factory, SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.GetAll(orgId, default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenNoUserId_ReturnsUnauthorized()
    {
        var ctrl   = Build(CreateFactory(), Anonymous());
        var result = await ctrl.Create(Guid.NewGuid(),
            new CreateOrgLogoRequest(Guid.NewGuid(), null, false, 1), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithMissingUploadFile_ReturnsBadRequest()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Create(Guid.NewGuid(),
            new CreateOrgLogoRequest(Guid.NewGuid(), null, false, 1), default);
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("not found", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WhenIsActiveTrue_DeactivatesExistingActiveLogo()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var existing = await SeedLogoAsync(factory, orgId, isActive: true);
        var fileId   = await SeedUploadFileAsync(factory);
        var ctrl     = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId,
            new CreateOrgLogoRequest(fileId, "New logo", IsActive: true, SortOrder: 2), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        // Old logo deactivated
        var old = await db.OrganizationLogos.FindAsync(existing.Id);
        Assert.False(old!.IsActive);
        // New logo is active
        Assert.True(await db.OrganizationLogos.AnyAsync(l => l.UploadFileId == fileId && l.IsActive));
    }

    [Fact]
    public async Task Create_AsSuperAdmin_CreatesLogo()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var fileId  = await SeedUploadFileAsync(factory);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId,
            new CreateOrgLogoRequest(fileId, "Alt text", false, 1), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrganizationLogos.AnyAsync(l => l.OrganizationId == orgId));
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenNotFound_ReturnsNotFound()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Update(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateOrgLogoRequest("Alt", true, 1), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_AsSuperAdmin_UpdatesAltText()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var logo    = await SeedLogoAsync(factory, orgId);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(orgId, logo.Id,
            new UpdateOrgLogoRequest("Updated alt", false, 5), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.OrganizationLogos.FindAsync(logo.Id);
        Assert.Equal("Updated alt", updated!.AltText);
        Assert.Equal(5, updated.SortOrder);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WhenNotFound_ReturnsNotFound()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Delete(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_AsSuperAdmin_RemovesLogo()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var logo    = await SeedLogoAsync(factory, orgId);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Delete(orgId, logo.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.OrganizationLogos.FindAsync(logo.Id));
    }
}
