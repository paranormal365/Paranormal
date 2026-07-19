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
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="AdminUploadFileTypeController"/>:
/// GetAll (ordered), GetAllWithExtensions (includes extension patterns),
/// Create, Update, Delete.
/// </summary>
public class AdminUploadFileTypeControllerTests
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
        m.Setup(x => x.Map<UploadFileTypeRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFileType t) return new UploadFileTypeRecord { Name = "", Description = "" };
             return new UploadFileTypeRecord
             {
                 Id = t.Id, Name = t.Name ?? "", Description = t.Description,
                 IsActive = t.IsActive, AllowAllExtensions = t.AllowAllExtensions, SortOrder = t.SortOrder,
             };
         });
        m.Setup(x => x.Map<IEnumerable<UploadFileTypeRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFileType> list) return [];
             return list.Select(t => new UploadFileTypeRecord { Id = t.Id, Name = t.Name ?? "", Description = t.Description });
         });
        m.Setup(x => x.Map<UploadFileTypeExtensionRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFileTypeExtension e) return new UploadFileTypeExtensionRecord { Pattern = "" };
             return new UploadFileTypeExtensionRecord { Id = e.Id, Pattern = e.Pattern ?? "" };
         });
        return m.Object;
    }

    private static AdminUploadFileTypeController Build(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new AdminUploadFileTypeController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext() };
        return ctrl;
    }

    private static async Task<(Guid TypeId, Guid ExtId)> SeedTypeWithExtensionAsync(
        IDbContextFactory<BenDataContext> factory,
        string name = "Audio", string ext = ".mp3", int sortOrder = 1)
    {
        var typeId  = Guid.NewGuid();
        var extId   = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = typeId, Name = name, IsActive = true, IsPublic = true,
            AllowAllExtensions = false, SortOrder = sortOrder,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
        {
            Id = extId, UploadFileTypeId = typeId, Pattern = ext,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (typeId, extId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoneSeeded()
    {
        var factory = CreateFactory();
        var result  = await Build(factory).GetAll(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFileTypeRecord>>(ok.Value));
    }

    [Fact]
    public async Task GetAll_ReturnsBothTypes()
    {
        var factory = CreateFactory();
        await SeedTypeWithExtensionAsync(factory, "Image", ".png", sortOrder: 1);
        await SeedTypeWithExtensionAsync(factory, "Audio", ".mp3", sortOrder: 2);

        var result = await Build(factory).GetAll(default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var types = Assert.IsAssignableFrom<IEnumerable<UploadFileTypeRecord>>(ok.Value).ToList();
        Assert.Equal(2, types.Count);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMissing()
    {
        var factory = CreateFactory();

        var result = await Build(factory).GetById(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsRecord_WhenExists()
    {
        var factory     = CreateFactory();
        var (typeId, _) = await SeedTypeWithExtensionAsync(factory, "Video");

        var result = await Build(factory).GetById(typeId, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UploadFileTypeRecord>(ok.Value);
        Assert.Equal(typeId, record.Id);
        Assert.Equal("Video", record.Name);
    }

    // ── GetAllWithExtensions ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllWithExtensions_ReturnsExtensionPatterns()
    {
        var factory     = CreateFactory();
        var (typeId, _) = await SeedTypeWithExtensionAsync(factory, "Audio", ".mp3");

        var result = await Build(factory).GetAllWithExtensions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFileTypeWithExtensionsResponse>>(ok.Value)
                         .ToList();
        Assert.Single(list);
        Assert.Equal(typeId, list[0].FileType.Id);
        Assert.Single(list[0].Extensions);
        Assert.Equal(".mp3", list[0].Extensions[0].Pattern);
    }

    [Fact]
    public async Task GetAllWithExtensions_ReturnsEmpty_WhenNoneSeeded()
    {
        var factory = CreateFactory();

        var result = await Build(factory).GetAllWithExtensions(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFileTypeWithExtensionsResponse>>(ok.Value));
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Persists_AndReturns201()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);
        var req     = new AdminCreateUploadFileTypeRequest(
            "Doc", "Documents", null, null, true, true, 5, false, Guid.NewGuid());

        var result  = await ctrl.Create(req, default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileTypeRecord>(created.Value);
        Assert.Equal("Doc",  record.Name);
        Assert.False(record.AllowAllExtensions);
        Assert.Equal(5,      record.SortOrder);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsNotFound_WhenTypeMissing()
    {
        var factory = CreateFactory();

        var result = await Build(factory).Update(Guid.NewGuid(),
            new AdminUpdateUploadFileTypeRequest("X", null, null, null, true, true, 1, false, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ChangesName_AndAllowAllExtensions()
    {
        var factory     = CreateFactory();
        var (typeId, _) = await SeedTypeWithExtensionAsync(factory, "Old");
        var ctrl        = Build(factory);

        var result = await ctrl.Update(typeId,
            new AdminUpdateUploadFileTypeRequest("New", "New desc", null, null, true, true, 1, true, null), default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UploadFileTypeRecord>(ok.Value);
        Assert.Equal("New", record.Name);
        Assert.True(record.AllowAllExtensions);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMissing()
    {
        var result = await Build(CreateFactory()).Delete(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_RemovesType_AndReturnsNoContent()
    {
        var factory     = CreateFactory();
        var (typeId, _) = await SeedTypeWithExtensionAsync(factory, "ToDelete");

        var result = await Build(factory).Delete(typeId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.UploadFileTypes.FindAsync(typeId));
    }
}
