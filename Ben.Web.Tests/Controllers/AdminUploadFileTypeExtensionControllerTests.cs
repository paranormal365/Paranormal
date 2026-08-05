using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
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

/// <summary>
/// Tests for AdminUploadFileTypeExtensionController — extension pattern CRUD for file types.
/// </summary>
public class AdminUploadFileTypeExtensionControllerTests
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
        m.Setup(x => x.Map<UploadFileTypeExtensionRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is UploadFileTypeExtension e
                ? new UploadFileTypeExtensionRecord { Id = e.Id, UploadFileTypeId = e.UploadFileTypeId, Pattern = e.Pattern, DateCreated = e.DateCreated, CreatedByAppUserId = e.CreatedByAppUserId }
                : new UploadFileTypeExtensionRecord { Pattern = "", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        return m.Object;
    }

    private static AdminUploadFileTypeExtensionController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(a => a.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var ctrl = new AdminUploadFileTypeExtensionController(factory, CreateMapper(), audit.Object);
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

    private static async Task<(IDbContextFactory<BenDataContext>, Guid fileTypeId, Guid userId)> SeedAsync()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var fileTypeId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.UploadFileTypes.Add(new UploadFileType { Id = fileTypeId, Name = "Test Type", AllowAllExtensions = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        await db.SaveChangesAsync();
        return (factory, fileTypeId, userId);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_MissingId_ReturnsNotFound()
    {
        var (factory, _, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>((await Build(factory, userId).GetById(Guid.NewGuid(), default)).Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidPattern_ReturnsCreated()
    {
        var (factory, fileTypeId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Create(new CreateUploadFileTypeExtensionRequest(fileTypeId, ".pdf", userId), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<UploadFileTypeExtensionRecord>(created.Value);
        Assert.Equal(".pdf", dto.Pattern);
        Assert.Equal(fileTypeId, dto.UploadFileTypeId);
    }

    [Fact]
    public async Task Create_PatternNormalisedToLowercase()
    {
        var (factory, fileTypeId, userId) = await SeedAsync();
        var result = await Build(factory, userId).Create(new CreateUploadFileTypeExtensionRequest(fileTypeId, ".PDF", userId), default);
        var dto = (UploadFileTypeExtensionRecord)((CreatedAtActionResult)result.Result!).Value!;
        Assert.Equal(".pdf", dto.Pattern);
    }

    [Fact]
    public async Task Create_InvalidFileTypeId_ReturnsBadRequest()
    {
        var (factory, _, userId) = await SeedAsync();
        Assert.IsType<BadRequestObjectResult>((await Build(factory, userId).Create(new CreateUploadFileTypeExtensionRequest(Guid.NewGuid(), ".pdf", userId), default)).Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingExtension_ReturnsUpdated()
    {
        var (factory, fileTypeId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var extId = ((UploadFileTypeExtensionRecord)((CreatedAtActionResult)(await ctrl.Create(new CreateUploadFileTypeExtensionRequest(fileTypeId, ".pdf", userId), default)).Result!).Value!).Id;

        var result = await ctrl.Update(extId, new UpdateUploadFileTypeExtensionRequest(".PDF2"), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UploadFileTypeExtensionRecord>(ok.Value);
        Assert.Equal(".pdf2", dto.Pattern);
    }

    [Fact]
    public async Task Update_MissingId_ReturnsNotFound()
    {
        var (factory, _, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>((await Build(factory, userId).Update(Guid.NewGuid(), new UpdateUploadFileTypeExtensionRequest(".pdf"), default)).Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingExtension_ReturnsNoContent()
    {
        var (factory, fileTypeId, userId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var extId = ((UploadFileTypeExtensionRecord)((CreatedAtActionResult)(await ctrl.Create(new CreateUploadFileTypeExtensionRequest(fileTypeId, ".pdf", userId), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await ctrl.Delete(extId, default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.UploadFileTypeExtensions.AnyAsync(e => e.Id == extId));
    }

    [Fact]
    public async Task Delete_MissingId_ReturnsNotFound()
    {
        var (factory, _, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>(await Build(factory, userId).Delete(Guid.NewGuid(), default));
    }

    // ── GetById roundtrip ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsCreatedExtension()
    {
        var (factory, fileTypeId, userId) = await SeedAsync();
        var ctrl  = Build(factory, userId);
        var extId = ((UploadFileTypeExtensionRecord)((CreatedAtActionResult)(await ctrl.Create(new CreateUploadFileTypeExtensionRequest(fileTypeId, ".tx*", userId), default)).Result!).Value!).Id;

        var ok  = Assert.IsType<OkObjectResult>((await ctrl.GetById(extId, default)).Result);
        var dto = Assert.IsType<UploadFileTypeExtensionRecord>(ok.Value);
        Assert.Equal(".tx*", dto.Pattern);
    }
}
