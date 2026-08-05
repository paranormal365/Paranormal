using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class VideoProjectControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<VideoProjectRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is VideoProject p
             ? new VideoProjectRecord { Id = p.Id, CaseId = p.CaseId, Name = p.Name, ProjectJson = p.ProjectJson, DateCreated = p.DateCreated, CreatedByAppUserId = p.CreatedByAppUserId }
             : new VideoProjectRecord { Name = "", ProjectJson = "" });
        m.Setup(x => x.Map<IEnumerable<VideoProjectRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<VideoProject> list
             ? list.Select(p => new VideoProjectRecord { Id = p.Id, CaseId = p.CaseId, Name = p.Name, ProjectJson = p.ProjectJson, DateCreated = p.DateCreated, CreatedByAppUserId = p.CreatedByAppUserId })
             : []);
        return m.Object;
    }

    private static VideoProjectController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid userId,
        bool isSuperAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));
        var ctrl = new VideoProjectController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid caseId, Guid userId)>
        SeedAsync()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();

        await using var db = factory.CreateDbContext();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = "test-org",
            CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Test Case",
            StreetAddress1 = "1 Main St", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
        });
        await db.SaveChangesAsync();
        return (factory, orgId, caseId, userId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_Member_ReturnsProjects()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = "Edit 1",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetAll(caseId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var items  = Assert.IsAssignableFrom<IEnumerable<VideoProjectRecord>>(ok.Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, _, caseId, _) = await SeedAsync();
        var stranger = Guid.NewGuid();
        var ctrl     = Build(factory, stranger);
        var result   = await ctrl.GetAll(caseId, CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_SuperAdmin_ReturnsProjects()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var ctrl   = Build(factory, Guid.NewGuid(), isSuperAdmin: true);
        var result = await ctrl.GetAll(caseId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_UnknownCase_ReturnsForbid()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetAll(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_Member_ReturnsProject()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "My Edit",
                ProjectJson = "{\"clips\":[]}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetById(caseId, projectId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<VideoProjectRecord>(ok.Value);
        Assert.Equal(projectId, record.Id);
        Assert.Equal("My Edit", record.Name);
    }

    [Fact]
    public async Task GetById_WrongCase_ReturnsNotFound()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetById(caseId, Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Member_ReturnsCreatedAndPersists()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var request = new VideoProjectRequest { Name = "Investigation #1", ProjectJson = "{\"clips\":[]}" };

        var result  = await ctrl.Create(caseId, request, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<VideoProjectRecord>(created.Value);
        Assert.Equal("Investigation #1", record.Name);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.VideoProjects.CountAsync());
    }

    [Fact]
    public async Task Create_NonMember_ReturnsForbid()
    {
        var (factory, _, caseId, _) = await SeedAsync();
        var ctrl   = Build(factory, Guid.NewGuid());
        var result = await ctrl.Create(caseId,
            new VideoProjectRequest { Name = "x", ProjectJson = "{}" },
            CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Create_SetsCreatedByAppUserId()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        await ctrl.Create(caseId, new VideoProjectRequest { Name = "p", ProjectJson = "{}" }, CancellationToken.None);

        await using var db = factory.CreateDbContext();
        var entity = await db.VideoProjects.SingleAsync();
        Assert.Equal(userId, entity.CreatedByAppUserId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Owner_PersistsChanges()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Old",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.Update(caseId, projectId,
            new VideoProjectRequest { Name = "New", ProjectJson = "{\"v\":1}" },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db2 = factory.CreateDbContext();
        var entity = await db2.VideoProjects.SingleAsync();
        Assert.Equal("New", entity.Name);
        Assert.NotNull(entity.DateUpdated);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Update(caseId, Guid.NewGuid(),
            new VideoProjectRequest { Name = "x", ProjectJson = "{}" },
            CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Creator_RemovesProject()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Draft",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.Delete(caseId, projectId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        await using var db2 = factory.CreateDbContext();
        Assert.Equal(0, await db2.VideoProjects.CountAsync());
    }

    [Fact]
    public async Task Delete_NonCreator_NonSuperAdmin_ReturnsForbid()
    {
        var (factory, orgId, caseId, ownerId) = await SeedAsync();
        var otherId = Guid.NewGuid();

        // Add other user as org member too
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = otherId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            var projectId = Guid.NewGuid();
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Draft",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        await using var dbCheck = factory.CreateDbContext();
        var pid    = (await dbCheck.VideoProjects.SingleAsync()).Id;
        var ctrl   = Build(factory, otherId);
        var result = await ctrl.Delete(caseId, pid, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Delete_SuperAdmin_CanDeleteAnyProject()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Draft",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, Guid.NewGuid(), isSuperAdmin: true);
        var result = await ctrl.Delete(caseId, projectId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Delete(caseId, Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }
}
