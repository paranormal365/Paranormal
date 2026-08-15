using AutoMapper;
using Ben.Data.Common.Enums;
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

/// <summary>
/// Tests for MediaLibraryController — the universal media library's cross-scope aggregation
/// (owned, shared by person/investigation-team/org, public, and case-linked).
/// </summary>
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
                   AppUserId = f.AppUserId, IsPublic = f.IsPublic,
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

    private static async Task<List<UploadFileRecord>> GetFilesAsync(MediaLibraryController ctrl, string? contentTypePrefixes = null)
    {
        var result = await ctrl.GetFiles(contentTypePrefixes, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return ((IEnumerable<UploadFileRecord>)ok.Value!).ToList();
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
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId, Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
        });
        await db.SaveChangesAsync();
        return (factory, userId, orgId, caseId);
    }

    private static UploadFile MakeFile(Guid userId, string contentType = "video/mp4", bool isPublic = false) => new()
    {
        Id                 = Guid.NewGuid(),
        AppUserId          = userId,
        FileName           = "clip.mp4",
        StoredFileName     = $"{Guid.NewGuid()}.mp4",
        ContentType        = contentType,
        FileSize           = 1024,
        UploadFileTypeId   = Guid.NewGuid(),
        IsPublic           = isPublic,
        DateCreated        = DateTime.UtcNow,
        CreatedByAppUserId = userId,
    };

    // ── Owned + contentTypePrefixes filter ──────────────────────────────────────

    [Fact]
    public async Task GetFiles_NoFilter_ReturnsAllOwnedContentTypes()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(userId, "video/mp4"));
            db.UploadFiles.Add(MakeFile(userId, "audio/mp3"));
            db.UploadFiles.Add(MakeFile(userId, "application/pdf"));
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Equal(3, files.Count); // no filter → every content type, including non-media
    }

    [Fact]
    public async Task GetFiles_ExcludesArchivedVersions()
    {
        // item #6 phase 3 — a replaced file's archived prior version must never surface in the
        // library, even though it's still owned by the caller like any other row.
        var (factory, userId, _, _) = await SeedAsync();
        var liveId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var live = MakeFile(userId, "video/mp4");
            live.Id = liveId;
            db.UploadFiles.Add(live);

            var archived = MakeFile(userId, "video/mp4");
            archived.ArchivedFromUploadFileId = liveId;
            db.UploadFiles.Add(archived);

            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(liveId, files[0].Id);
    }

    [Fact]
    public async Task GetFiles_ContentTypePrefixes_FiltersToRequestedTypes()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(userId, "video/mp4"));
            db.UploadFiles.Add(MakeFile(userId, "audio/mp3"));
            db.UploadFiles.Add(MakeFile(userId, "application/pdf")); // excluded by the filter
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId), "video/,audio/,image/");
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public async Task GetFiles_ExcludesOtherUsersPersonalFiles()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(Guid.NewGuid(), "video/mp4")); // different user, not shared
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Empty(files);
    }

    // ── Person / investigation-team / org / public shares ───────────────────────

    [Fact]
    public async Task GetFiles_IncludesFileSharedWithMePersonally()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        Guid fileId;
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/jpeg");
            fileId = file.Id;
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Person,
                TargetAppUserId = userId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(fileId, files[0].Id);
    }

    [Fact]
    public async Task GetFiles_InactiveShare_IsExcluded()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/jpeg");
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Person,
                TargetAppUserId = userId, SharedByAppUserId = ownerId, IsActive = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Empty(files);
    }

    [Fact]
    public async Task GetFiles_IncludesFileSharedWithMyInvestigationTeam()
    {
        var (factory, userId, orgId, caseId) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        Guid fileId;
        await using (var db = factory.CreateDbContext())
        {
            db.Investigations.Add(new Investigation
            {
                Id = invId, CaseId = caseId, Title = "Inv", ScheduledDateTime = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.InvestigationAttendees.Add(new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = userId });
            var file = MakeFile(ownerId, "video/mp4");
            fileId = file.Id;
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.InvestigationTeam,
                TargetInvestigationId = invId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(fileId, files[0].Id);
    }

    [Fact]
    public async Task GetFiles_ExcludesInvestigationTeamShare_WhenNotAnAttendee()
    {
        var (factory, userId, _, caseId) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Investigations.Add(new Investigation
            {
                Id = invId, CaseId = caseId, Title = "Inv", ScheduledDateTime = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            // userId is NOT added as an attendee
            var file = MakeFile(ownerId, "video/mp4");
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.InvestigationTeam,
                TargetInvestigationId = invId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Empty(files);
    }

    [Fact]
    public async Task GetFiles_IncludesFileSharedWithMyOrg_ViaTieredTable_OrgMembersTier()
    {
        var (factory, userId, orgId, _) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/png");
            db.UploadFiles.Add(file);
            db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, OrganizationId = orgId,
                SharedByAppUserId = ownerId, Visibility = FileShareVisibility.OrgMembers, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
    }

    [Fact]
    public async Task GetFiles_ExcludesOrgAdminsOnlyShare_WhenViewerIsNotAdmin()
    {
        var (factory, userId, orgId, _) = await SeedAsync(); // seeded membership Role = Member
        var ownerId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/png");
            db.UploadFiles.Add(file);
            db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, OrganizationId = orgId,
                SharedByAppUserId = ownerId, Visibility = FileShareVisibility.OrgAdminsOnly, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Empty(files);
    }

    [Fact]
    public async Task GetFiles_IncludesFileSharedWithMyOrg_ViaNewGeneralizedTable()
    {
        var (factory, userId, orgId, _) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/png");
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Organization,
                TargetOrganizationId = orgId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
    }

    [Fact]
    public async Task GetFiles_IncludesPublicFile_ViaIsPublicFlag()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(Guid.NewGuid(), "image/png", isPublic: true));
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
    }

    [Fact]
    public async Task GetFiles_IncludesPublicFile_ViaPublicShare()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/png");
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Public,
                SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
    }

    // ── Case-linked scope (published video / CaseFile / CaseTimelineEntryFile) ──

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

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(publishedFileId, files[0].Id);
    }

    [Fact]
    public async Task GetFiles_IncludesCaseFileLinkedFile()
    {
        var (factory, userId, _, caseId) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        Guid fileId;
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "application/pdf");
            fileId = file.Id;
            db.UploadFiles.Add(file);
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = file.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(fileId, files[0].Id);
    }

    [Fact]
    public async Task GetFiles_IncludesCaseTimelineEntryEvidenceFile()
    {
        var (factory, userId, _, caseId) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        Guid fileId;
        await using (var db = factory.CreateDbContext())
        {
            var entry = new CaseTimelineEntry
            {
                Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = ownerId,
                EntryType = CaseTimelineEntryType.Evidence, Visibility = CaseTimelineVisibility.OrgOnly,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            };
            db.CaseTimelineEntries.Add(entry);
            var file = MakeFile(ownerId, "audio/wav");
            fileId = file.Id;
            db.UploadFiles.Add(file);
            db.CaseTimelineEntryFiles.Add(new CaseTimelineEntryFile
            {
                Id = Guid.NewGuid(), CaseTimelineEntryId = entry.Id, UploadFileId = file.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(fileId, files[0].Id);
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

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Empty(files);
    }

    // ── Dedup ─────────────────────────────────────────────────────────────────

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

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
    }

    [Fact]
    public async Task GetFiles_DeduplicatesFileMatchingMultipleScopesAtOnce()
    {
        // Public AND shared with me personally AND linked to an accessible case — still one row.
        var (factory, userId, _, caseId) = await SeedAsync();
        var ownerId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            var file = MakeFile(ownerId, "image/png", isPublic: true);
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Person,
                TargetAppUserId = userId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = file.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
    }
}
