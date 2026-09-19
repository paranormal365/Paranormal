using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using System.Text.Json;
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
        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("test/path");
        fileStorage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("test/path");
        fileStorage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var ctrl = new VideoProjectController(factory, CreateMapper(), fileStorage.Object, Ben.Web.Tests.TestMedia.Ingest());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };
        return ctrl;
    }

    private static IFormFile FakeUpload(string fileName)
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "video/mp4",
        };
    }

    private static JsonElement Json(object payload)
        => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload));

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
    public async Task GetAll_ReturnsOwnProjects()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = "Edit 1",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), Name = "Personal Edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            // Different user — should NOT be returned
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), Name = "Other",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetAll(null, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var items  = Assert.IsAssignableFrom<IEnumerable<VideoProjectRecord>>(ok.Value);
        Assert.Equal(2, items.Count());
    }

    [Fact]
    public async Task GetAll_FilterByCaseId_ReturnsOnlyThatCase()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = "Case Edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), Name = "Personal Edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetAll(caseId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var items  = Assert.IsAssignableFrom<IEnumerable<VideoProjectRecord>>(ok.Value);
        Assert.Single(items);
        Assert.Equal("Case Edit", items.First().Name);
    }

    /// <summary>
    /// A case's projects belong to the case, not to whoever happened to press Save.
    /// </summary>
    /// <remarks>
    /// Each member used to see a different list on the case's Video tab, and nobody could pick up
    /// anybody else's edit, while help described the case tab as shared work (2026-09-05 audit,
    /// persistence-14 and site-7).
    /// </remarks>
    [Fact]
    public async Task GetAll_ForACase_ShowsEveryMembers_Projects()
    {
        var (factory, orgId, caseId, ownerId) = await SeedAsync();
        var colleagueId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = colleagueId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = colleagueId
            });
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = "Owner's cut",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, colleagueId);
        var result = await ctrl.GetAll(caseId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var items  = Assert.IsAssignableFrom<IEnumerable<VideoProjectRecord>>(ok.Value);

        Assert.Equal("Owner's cut", Assert.Single(items).Name);
    }

    /// <summary>
    /// Sharing stops at the case. Somebody outside the organisation gets nothing.
    /// </summary>
    [Fact]
    public async Task GetAll_ForACase_RefusesSomebodyWhoCannotReachIt()
    {
        var (factory, _, caseId, _) = await SeedAsync();

        var ctrl   = Build(factory, Guid.NewGuid());
        var result = await ctrl.GetAll(caseId, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>
    /// Without a case the list is still personal: a member's own projects are their own.
    /// </summary>
    [Fact]
    public async Task GetAll_WithoutACase_StaysPersonal()
    {
        var (factory, orgId, _, ownerId) = await SeedAsync();
        var colleagueId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = colleagueId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = colleagueId
            });
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), Name = "Owner's private edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, colleagueId);
        var result = await ctrl.GetAll(null, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var items  = Assert.IsAssignableFrom<IEnumerable<VideoProjectRecord>>(ok.Value);

        Assert.Empty(items);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_Owner_ReturnsProject()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "My Edit",
                ProjectJson = "{\"clips\":[]}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.GetById(projectId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<VideoProjectRecord>(ok.Value);
        Assert.Equal(projectId, record.Id);
    }

    [Fact]
    public async Task GetById_OtherUser_ReturnsNotFound()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, Guid.NewGuid());
        var result = await ctrl.GetById(projectId, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>
    /// The other half of a shared list: the project it names can actually be opened.
    /// </summary>
    [Fact]
    public async Task GetById_AColleagueOnTheCase_CanOpenIt()
    {
        var (factory, orgId, caseId, ownerId) = await SeedAsync();
        var colleagueId = Guid.NewGuid();
        var projectId   = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = colleagueId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = colleagueId
            });
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Owner's cut",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, colleagueId);
        var result = await ctrl.GetById(projectId, CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);

        Assert.Equal(projectId, Assert.IsType<VideoProjectRecord>(ok.Value).Id);
    }

    /// <summary>
    /// Reading is shared; writing is not. A colleague can open somebody's case project and cannot
    /// overwrite it.
    /// </summary>
    [Fact]
    public async Task Update_AColleagueOnTheCase_CannotOverwriteIt()
    {
        var (factory, orgId, caseId, ownerId) = await SeedAsync();
        var colleagueId = Guid.NewGuid();
        var projectId   = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = colleagueId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = colleagueId
            });
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Owner's cut",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, colleagueId);
        var result = await ctrl.Update(projectId, Json(new { projectName = "Mine now" }), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A render published to a case appears on the case's Files tab.
    /// </summary>
    /// <remarks>
    /// The finished video existed only as a column on the project row. It was written under the
    /// case's folder and then nothing on the case pointed at it, so it was invisible to everybody
    /// except the person who made it (2026-09-05 audit, site-6).
    /// </remarks>
    [Fact]
    public async Task Publish_ToACaseProject_PutsTheRenderOnTheCase()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Porch cut",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.Publish(projectId, FakeUpload("cut.mp4"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);

        await using (var db = factory.CreateDbContext())
        {
            var project = await db.VideoProjects.FirstAsync(p => p.Id == projectId);
            var link    = Assert.Single(db.CaseFiles.Where(f => f.CaseId == caseId));

            Assert.Equal(project.PublishedUploadFileId, link.UploadFileId);
            Assert.Contains("Porch cut", link.Description);
        }
    }

    /// <summary>
    /// A personal project has no case to appear on, and must not invent one.
    /// </summary>
    [Fact]
    public async Task Publish_ToAPersonalProject_LinksNoCase()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, Name = "Personal cut",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, userId);
        await ctrl.Publish(projectId, FakeUpload("cut.mp4"), CancellationToken.None);

        await using (var db = factory.CreateDbContext())
            Assert.Empty(db.CaseFiles);
    }

    /// <summary>
    /// Publishing twice replaces the case's copy rather than leaving both on the Files tab.
    /// </summary>
    [Fact]
    public async Task Publish_Twice_LeavesOneFileOnTheCase()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, CaseId = caseId, Name = "Porch cut",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, userId);
        await ctrl.Publish(projectId, FakeUpload("first.mp4"), CancellationToken.None);
        await ctrl.Publish(projectId, FakeUpload("second.mp4"), CancellationToken.None);

        await using (var db = factory.CreateDbContext())
        {
            var project = await db.VideoProjects.FirstAsync(p => p.Id == projectId);
            var link    = Assert.Single(db.CaseFiles.Where(f => f.CaseId == caseId));

            Assert.Equal(project.PublishedUploadFileId, link.UploadFileId);
        }
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Personal_ExtractsNameAndPersists()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var body = Json(new { projectName = "My Clip Reel", tracks = new[] { new { } } });

        var result  = await ctrl.Create(null, body, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<VideoProjectRecord>(created.Value);
        Assert.Equal("My Clip Reel", record.Name);
        Assert.Null(record.CaseId);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.VideoProjects.CountAsync());
    }

    [Fact]
    public async Task Create_WithCaseId_Member_LinksCaseAndPersists()
    {
        var (factory, _, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var body = Json(new { projectName = "Investigation Edit" });

        var result  = await ctrl.Create(caseId, body, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<VideoProjectRecord>(created.Value);
        Assert.Equal(caseId, record.CaseId);
    }

    [Fact]
    public async Task Create_WithCaseId_NonMember_ReturnsForbid()
    {
        var (factory, _, caseId, _) = await SeedAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        var body = Json(new { projectName = "Edit" });
        var result = await ctrl.Create(caseId, body, CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Create_MissingProjectName_UsesDefault()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var body = Json(new { tracks = new object[] { } });

        var result  = await ctrl.Create(null, body, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<VideoProjectRecord>(created.Value);
        Assert.Equal("Untitled Project", record.Name);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Owner_PersistsChanges()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, Name = "Old",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.Update(projectId, Json(new { projectName = "New", v = 1 }), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db2 = factory.CreateDbContext();
        var entity = await db2.VideoProjects.SingleAsync();
        Assert.Equal("New", entity.Name);
        Assert.NotNull(entity.DateUpdated);
    }

    [Fact]
    public async Task Update_OtherUser_ReturnsNotFound()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, Name = "Edit",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, Guid.NewGuid());
        var result = await ctrl.Update(projectId, Json(new { projectName = "x" }), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Creator_RemovesProject()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, Name = "Draft",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, userId);
        var result = await ctrl.Delete(projectId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        await using var db2 = factory.CreateDbContext();
        Assert.Equal(0, await db2.VideoProjects.CountAsync());
    }

    [Fact]
    public async Task Delete_NonCreator_ReturnsForbid()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, Name = "Draft",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, Guid.NewGuid());
        var result = await ctrl.Delete(projectId, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Delete_SuperAdmin_CanDeleteAnyProject()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.VideoProjects.Add(new VideoProject
            {
                Id = projectId, Name = "Draft",
                ProjectJson = "{}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, Guid.NewGuid(), isSuperAdmin: true);
        var result = await ctrl.Delete(projectId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var (factory, _, _, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }
}

