using System.Security.Claims;
using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The classic upload now answers to the same configurable limit as the chunked path
/// (<see cref="SiteSettingKeys.UploadMaxFileBytes"/>) — one number governs both doors.
/// </summary>
public class UploadFileSizeLimitTests
{
    private static readonly Guid Caller = new("11111111-1111-1111-1111-111111111111");

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static UploadFileController Build(IDbContextFactory<BenDataContext> factory)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((uid, name) => $"users/{uid}/{name}");

        var ctrl = new UploadFileController(factory, new Mock<IMapper>().Object, storage.Object,
            new Mock<IAuditLogService>().Object, new FileMetadataExtractorService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadFileController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Caller.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static IFormFile FileOfSize(long bytes)
    {
        var file = new Mock<IFormFile>();
        file.Setup(f => f.Length).Returns(bytes);
        file.Setup(f => f.FileName).Returns("big.mp4");
        file.Setup(f => f.ContentType).Returns("video/mp4");
        return file.Object;
    }

    [Fact]
    public async Task Upload_OverTheConfiguredLimit_IsRefusedWithTheNumber()
    {
        var factory = CreateFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.UploadMaxFileBytes, Value = "1000",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Caller,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).Upload(
            Guid.NewGuid(), Guid.Empty, null, false, FileOfSize(1001), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("1,000", refusal.Value!.ToString());
    }

    [Fact]
    public async Task Upload_UnderTheLimit_ProceedsToTheNextCheck()
    {
        // No settings row: the default (2 GiB) applies, and a small file passes the size gate.
        // The unknown file type is then the refusal — proving the size check let it through.
        var result = await Build(CreateFactory()).Upload(
            Guid.NewGuid(), Guid.Empty, null, false, FileOfSize(10), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("file type", refusal.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
