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

public class MediaLibraryControllerTests
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
        m.Setup(x => x.Map<IEnumerable<UploadFileRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<UploadFile> list
             ? list.Select(f => new UploadFileRecord
               {
                   Id = f.Id, FileName = f.FileName, StoredFileName = f.StoredFileName,
                   ContentType = f.ContentType, FileSize = f.FileSize, DateCreated = f.DateCreated,
                   AppUserId = f.AppUserId
               })
             : []);
        return m.Object;
    }

    private static MediaLibraryController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new MediaLibraryController(factory, CreateMapper());
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

    // ── Seed ─────────────────────────────────────────────────────────────────

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid userId, Guid orgId, Guid caseId)>
        SeedAsync()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Org", UrlName = "org",
            CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Case",
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN",
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
        return (factory, userId, orgId, caseId);
    }

    private static UploadFile MakeFile(Guid userId, string contentType = "video/mp4") => new()
    {
        Id                 = Guid.NewGuid(),
        AppUserId          = userId,
        FileName           = "clip.mp4",
        StoredFileName     = $"{Guid.NewGuid()}.mp4",
        ContentType        = contentType,
        FileSize           = 1024,
        UploadFileTypeId   = Guid.NewGuid(),
        IsPublic           = false,
        DateCreated        = DateTime.UtcNow,
        CreatedByAppUserId = userId,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFiles_ReturnsOwnMediaFiles()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(userId, "video/mp4"));
            db.UploadFiles.Add(MakeFile(userId, "audio/mp3"));
            db.UploadFiles.Add(MakeFile(userId, "application/pdf")); // excluded — not media
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetFiles(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, ((IEnumerable<UploadFileRecord>)ok.Value!).Count());
    }

    [Fact]
    public async Task GetFiles_ExcludesOtherUsersPersonalFiles()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(Guid.NewGuid(), "video/mp4")); // different user
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetFiles(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty((IEnumerable<UploadFileRecord>)ok.Value!);
    }

    [Fact]
    public async Task GetFiles_IncludesPublishedCaseVideosFromAccessibleCase()
    {
        var (factory, userId, _, caseId) = await SeedAsync();
        Guid publishedFileId;
        await using (var db = factory.CreateDbContext())
        {
            var otherUser = Guid.NewGuid();
            var file = MakeFile(otherUser, "video/mp4");
            publishedFileId = file.Id;
            db.UploadFiles.Add(file);
            db.VideoProjects.Add(new VideoProject
            {
                Id                   = Guid.NewGuid(),
                CaseId               = caseId,
                Name                 = "Ep1",
                ProjectJson          = "{}",
                PublishedUploadFileId = file.Id,
                CreatedByAppUserId   = otherUser,
                DateCreated          = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetFiles(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var files = ((IEnumerable<UploadFileRecord>)ok.Value!).ToList();
        Assert.Single(files);
        Assert.Equal(publishedFileId, files[0].Id);
    }

    [Fact]
    public async Task GetFiles_ExcludesPublishedVideosFromCasesUserCannotAccess()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            var otherOrgId  = Guid.NewGuid();
            var otherCaseId = Guid.NewGuid();
            var otherUser   = Guid.NewGuid();
            db.Organizations.Add(new Organization
            {
                Id = otherOrgId, Name = "Other", UrlName = "other",
                CreatedByAppUserId = otherUser, DateCreated = DateTime.UtcNow
            });
            db.Cases.Add(new Case
            {
                Id = otherCaseId, OrganizationId = otherOrgId, Title = "Other",
                StreetAddress1 = "1 St", City = "City", State = "TN",
                ZipCode = "00000", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = otherUser
            });
            var file = MakeFile(otherUser, "video/mp4");
            db.UploadFiles.Add(file);
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), CaseId = otherCaseId, Name = "X",
                ProjectJson = "{}", PublishedUploadFileId = file.Id,
                CreatedByAppUserId = otherUser, DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetFiles(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty((IEnumerable<UploadFileRecord>)ok.Value!);
    }

    [Fact]
    public async Task GetFiles_DeduplicatesOwnedPublishedFiles()
    {
        // If the user published their own video to a case, it should appear once
        var (factory, userId, _, caseId) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(userId, "video/mp4");
            db.UploadFiles.Add(file);
            db.VideoProjects.Add(new VideoProject
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = "Mine",
                ProjectJson = "{}", PublishedUploadFileId = file.Id,
                CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetFiles(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((IEnumerable<UploadFileRecord>)ok.Value!);
    }
}
