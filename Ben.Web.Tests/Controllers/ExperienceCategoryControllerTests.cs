using AutoMapper;
using Ben.Data.Common.Constants;
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
/// Tests for ExperienceCategoryController (public read) and
/// AdminExperienceCategoryController / AdminExperienceTypeController (SuperAdmin CRUD).
/// </summary>
public class ExperienceCategoryControllerTests
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
        m.Setup(x => x.Map<ExperienceCategoryRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is ExperienceCategory c
                ? new ExperienceCategoryRecord { Id = c.Id, Name = c.Name, SortOrder = c.SortOrder, IsActive = c.IsActive, IsApproved = c.IsApproved, DateCreated = c.DateCreated, CreatedByAppUserId = c.CreatedByAppUserId }
                : new ExperienceCategoryRecord { Name = "", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<ExperienceCategoryRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<ExperienceCategory> list
                ? list.Select(c => new ExperienceCategoryRecord { Id = c.Id, Name = c.Name, SortOrder = c.SortOrder, IsActive = c.IsActive, IsApproved = c.IsApproved, DateCreated = c.DateCreated, CreatedByAppUserId = c.CreatedByAppUserId })
                : []);
        m.Setup(x => x.Map<ExperienceTypeRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is ExperienceType t
                ? new ExperienceTypeRecord { Id = t.Id, ExperienceCategoryId = t.ExperienceCategoryId, Name = t.Name, IsActive = t.IsActive, IsApproved = t.IsApproved, DateCreated = t.DateCreated, CreatedByAppUserId = t.CreatedByAppUserId }
                : new ExperienceTypeRecord { Name = "", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<ExperienceTypeRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<ExperienceType> list
                ? list.Select(t => new ExperienceTypeRecord { Id = t.Id, ExperienceCategoryId = t.ExperienceCategoryId, Name = t.Name, IsActive = t.IsActive, IsApproved = t.IsApproved, DateCreated = t.DateCreated, CreatedByAppUserId = t.CreatedByAppUserId })
                : []);
        m.Setup(x => x.Map<IReadOnlyList<ExperienceTypeRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<ExperienceType> list
                ? list.Select(t => new ExperienceTypeRecord { Id = t.Id, ExperienceCategoryId = t.ExperienceCategoryId, Name = t.Name, IsActive = t.IsActive, IsApproved = t.IsApproved, DateCreated = t.DateCreated, CreatedByAppUserId = t.CreatedByAppUserId }).ToList()
                : (IReadOnlyList<ExperienceTypeRecord>)[]);
        return m.Object;
    }

    private static ExperienceCategoryController BuildPublic(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new ExperienceCategoryController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return ctrl;
    }

    private static AdminExperienceCategoryController BuildAdmin(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new AdminExperienceCategoryController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                     new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer"))
            }
        };
        return ctrl;
    }

    private static AdminExperienceTypeController BuildTypeAdmin(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new AdminExperienceTypeController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                     new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid userId)> SeedAsync(bool includeApproved = true)
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        if (includeApproved)
        {
            db.ExperienceCategories.Add(new ExperienceCategory { Id = Guid.NewGuid(), Name = "Visual", SortOrder = 1, IsActive = true, IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.ExperienceCategories.Add(new ExperienceCategory { Id = Guid.NewGuid(), Name = "Audio",  SortOrder = 2, IsActive = true, IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.ExperienceCategories.Add(new ExperienceCategory { Id = Guid.NewGuid(), Name = "Pending", SortOrder = 3, IsActive = true, IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        }
        await db.SaveChangesAsync();
        return (factory, userId);
    }

    // ── Public GetAll ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsOnlyApprovedAndActive()
    {
        var (factory, _) = await SeedAsync();
        var ctrl = BuildPublic(factory);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetAll(default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<ExperienceCategoryRecord>>(ok.Value);
        Assert.Equal(2, list.Count());
        Assert.All(list, r => Assert.True(r.IsApproved && r.IsActive));
    }

    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var (factory, _) = await SeedAsync(includeApproved: false);
        var ok = Assert.IsType<OkObjectResult>((await BuildPublic(factory).GetAll(default)).Result);
        Assert.Empty((IEnumerable<ExperienceCategoryRecord>)ok.Value!);
    }

    // ── Public GetTypes ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetTypes_ReturnsApprovedTypesForCategory()
    {
        var (factory, userId) = await SeedAsync(includeApproved: false);
        var catId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.ExperienceCategories.Add(new ExperienceCategory { Id = catId, Name = "Visual", SortOrder = 1, IsActive = true, IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.ExperienceTypes.Add(new ExperienceType { Id = Guid.NewGuid(), ExperienceCategoryId = catId, Name = "Apparition", SortOrder = 1, IsActive = true, IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.ExperienceTypes.Add(new ExperienceType { Id = Guid.NewGuid(), ExperienceCategoryId = catId, Name = "Pending Type", SortOrder = 2, IsActive = true, IsApproved = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        await db.SaveChangesAsync();

        var ctrl = BuildPublic(factory);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetTypes(catId, default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<ExperienceTypeRecord>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Apparition", list.First().Name);
    }

    // ── Admin category CRUD ───────────────────────────────────────────────────

    [Fact]
    public async Task AdminCreate_CreatesApprovedCategory()
    {
        var (factory, userId) = await SeedAsync(includeApproved: false);
        var ctrl   = BuildAdmin(factory, userId);
        var result = await ctrl.Create(new UpsertExperienceCategoryRequest("New Cat", null, null, null, 1, true), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ExperienceCategoryRecord>(created.Value);
        Assert.Equal("New Cat", dto.Name);
        Assert.True(dto.IsApproved);
    }

    [Fact]
    public async Task AdminUpdate_UpdatesCategory()
    {
        var (factory, userId) = await SeedAsync(includeApproved: false);
        var admin  = BuildAdmin(factory, userId);
        var catId  = ((ExperienceCategoryRecord)((CreatedAtActionResult)(await admin.Create(new UpsertExperienceCategoryRequest("Old", null, null, null, 1, true), default)).Result!).Value!).Id;

        var result = await admin.Update(catId, new UpsertExperienceCategoryRequest("Updated", null, null, null, 1, true), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ExperienceCategoryRecord>(ok.Value);
        Assert.Equal("Updated", dto.Name);
    }

    [Fact]
    public async Task AdminDelete_RemovesCategory()
    {
        var (factory, userId) = await SeedAsync(includeApproved: false);
        var admin = BuildAdmin(factory, userId);
        var catId = ((ExperienceCategoryRecord)((CreatedAtActionResult)(await admin.Create(new UpsertExperienceCategoryRequest("To Delete", null, null, null, 1, true), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await admin.Delete(catId, default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.ExperienceCategories.AnyAsync(c => c.Id == catId));
    }

    // ── Admin type CRUD ───────────────────────────────────────────────────────

    [Fact]
    public async Task AdminTypeCreate_CreatesApprovedType()
    {
        var (factory, userId) = await SeedAsync(includeApproved: false);
        var admin  = BuildAdmin(factory, userId);
        var catId  = ((ExperienceCategoryRecord)((CreatedAtActionResult)(await admin.Create(new UpsertExperienceCategoryRequest("Visual", null, null, null, 1, true), default)).Result!).Value!).Id;

        var typeAdmin = BuildTypeAdmin(factory, userId);
        var result    = await typeAdmin.Create(catId, new UpsertExperienceTypeRequest("Apparition", null, null, 1, true), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ExperienceTypeRecord>(created.Value);
        Assert.Equal("Apparition", dto.Name);
        Assert.True(dto.IsApproved);
    }

    [Fact]
    public async Task AdminTypeDelete_RemovesType()
    {
        var (factory, userId) = await SeedAsync(includeApproved: false);
        var admin     = BuildAdmin(factory, userId);
        var catId     = ((ExperienceCategoryRecord)((CreatedAtActionResult)(await admin.Create(new UpsertExperienceCategoryRequest("Visual", null, null, null, 1, true), default)).Result!).Value!).Id;
        var typeAdmin = BuildTypeAdmin(factory, userId);
        var typeId    = ((ExperienceTypeRecord)((CreatedAtActionResult)(await typeAdmin.Create(catId, new UpsertExperienceTypeRequest("Type", null, null, 1, true), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await typeAdmin.Delete(catId, typeId, default));
    }
}
