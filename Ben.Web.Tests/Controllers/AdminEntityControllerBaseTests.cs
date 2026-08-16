using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
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
/// Tests the CRUD logic defined in <see cref="AdminEntityControllerBase{TEntity,TRecord}"/>.
/// Uses <see cref="AdminUserAddressTypeController"/> as a representative concrete type
/// (pure delegation to base, no extra endpoints).
/// These tests cover GetAll, GetById, Create, Update, Delete for all 22+
/// controllers that extend AdminEntityControllerBase.
/// </summary>
public class AdminEntityControllerBaseTests
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
        m.Setup(x => x.Map<UserAddressTypeAdminRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UserAddressType e)
                 return new UserAddressTypeAdminRecord { Name = "" };
             return new UserAddressTypeAdminRecord
             {
                 Id   = e.Id,
                 Name = e.Name ?? "",
             };
         });
        m.Setup(x => x.Map<IEnumerable<UserAddressTypeAdminRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UserAddressType> list) return [];
             return list.Select(e => new UserAddressTypeAdminRecord { Id = e.Id, Name = e.Name ?? "" });
         });
        return m.Object;
    }

    private static AdminUserAddressTypeController Build(
        IDbContextFactory<BenDataContext> factory, Guid? userId = null)
    {
        var auditMock = new Mock<IAuditLogService>();
        auditMock.Setup(x => x.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        auditMock.Setup(x => x.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        auditMock.Setup(x => x.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var ctrl = new AdminUserAddressTypeController(factory, CreateMapper(), auditMock.Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = userId.HasValue
                    ? new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
                      ], "Bearer"))
                    : new ClaimsPrincipal(new ClaimsIdentity())
            }
        };
        return ctrl;
    }

    private static UserAddressType MakeType(string name = "Home") => new()
    {
        Id = Guid.NewGuid(), Name = name, IsActive = true, IsPublic = true, SortOrder = 1,
        DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
    };

    /// <summary>
    /// Simulates the raw JSON body model binding already consumed, so
    /// <c>WasJsonPropertySetAsync</c> has something real to re-read.
    /// </summary>
    private static void SetJsonBody(ControllerBase ctrl, string json)
    {
        ctrl.ControllerContext.HttpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNothingSeeded()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.GetAll(default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<UserAddressTypeAdminRecord>>(ok.Value);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetAll_ReturnsAllSeededRecords()
    {
        var factory = CreateFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserAddressTypes.AddRange(MakeType("Work"), MakeType("Home"));
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<UserAddressTypeAdminRecord>>(ok.Value)
                          .ToList();
        Assert.Equal(2, items.Count);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.GetById(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsRecord_WhenExists()
    {
        var factory = CreateFactory();
        var entity  = MakeType("Billing");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserAddressTypes.Add(entity);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetById(entity.Id, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UserAddressTypeAdminRecord>(ok.Value);
        Assert.Equal(entity.Id, record.Id);
        Assert.Equal("Billing", record.Name);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_PersistsEntity_AndReturns201()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);
        var entity  = MakeType("Vacation");

        var result  = await ctrl.Create(entity, default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UserAddressTypeAdminRecord>(created.Value);
        Assert.Equal(entity.Id, record.Id);
        Assert.Equal("Vacation", record.Name);

        await using var db = await factory.CreateDbContextAsync();
        Assert.NotNull(await db.UserAddressTypes.FindAsync(entity.Id));
    }

    [Fact]
    public async Task Create_AssignsNewGuid_WhenIdIsEmpty()
    {
        var factory = CreateFactory();
        var entity  = MakeType("Other");
        entity.Id   = Guid.Empty;  // force empty
        var ctrl    = Build(factory, Guid.NewGuid());

        await ctrl.Create(entity, default);

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public async Task Create_PersistsExplicitFalse_WhenJsonBodyIncludesIsActiveFalse()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());
        var entity  = MakeType("Explicit");
        entity.IsActive = false; // what model binding would have already produced from the JSON below
        SetJsonBody(ctrl, """{"isActive": false, "name": "Explicit"}""");

        await ctrl.Create(entity, default);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.UserAddressTypes.FindAsync(entity.Id);
        Assert.NotNull(saved);
        Assert.False(saved!.IsActive);
    }

    [Fact]
    public async Task Create_PersistsExplicitFalse_WhenJsonKeyIsPascalCase()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());
        var entity  = MakeType("PascalCase");
        entity.IsActive = false;
        SetJsonBody(ctrl, """{"IsActive": false, "Name": "PascalCase"}""");

        await ctrl.Create(entity, default);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.UserAddressTypes.FindAsync(entity.Id);
        Assert.NotNull(saved);
        Assert.False(saved!.IsActive);
    }

    [Fact]
    public async Task Create_DefaultsToTrue_WhenIsActiveOmittedFromJsonBody()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());
        var entity  = MakeType("Omitted");
        entity.IsActive = false; // the type's default when the JSON never mentioned the field
        SetJsonBody(ctrl, """{"name": "Omitted"}""");

        await ctrl.Create(entity, default);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.UserAddressTypes.FindAsync(entity.Id);
        Assert.NotNull(saved);
        Assert.True(saved!.IsActive);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsNotFound_WhenEntityMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Update(Guid.NewGuid(), MakeType(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ChangesEntity_AndReturns200()
    {
        var factory = CreateFactory();
        var entity  = MakeType("Old");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserAddressTypes.Add(entity);
            await db.SaveChangesAsync();
        }
        var ctrl    = Build(factory, Guid.NewGuid());
        var updated = MakeType("New");
        updated.Id  = entity.Id;

        var result  = await ctrl.Update(entity.Id, updated, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UserAddressTypeAdminRecord>(ok.Value);
        Assert.Equal("New", record.Name);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenEntityMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Delete(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_RemovesEntity_AndReturnsNoContent()
    {
        var factory = CreateFactory();
        var entity  = MakeType("ToDelete");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserAddressTypes.Add(entity);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.Delete(entity.Id, default);

        Assert.IsType<NoContentResult>(result);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Null(await verify.UserAddressTypes.FindAsync(entity.Id));
    }
}
