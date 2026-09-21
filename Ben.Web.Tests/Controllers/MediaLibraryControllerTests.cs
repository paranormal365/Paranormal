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
                   // Carried because the listing reads it to name a group that was handed a
                   // file (item 180 Phase B) — a stand-in mapper that drops it hides V-3.
                   OwnerOrganizationId = f.OwnerOrganizationId,
               })
             : []);
        return m.Object;
    }

    /// <summary>
    /// Storage that says every file is there.
    /// </summary>
    /// <remarks>
    /// The listing now drops rows whose bytes are missing (Ben, 2026-09-21: "p201.jpg or
    /// test-photo.jpg can't be loaded so they should not show up"). These fixtures never write a
    /// byte, so without a stand-in that answers yes, every existing test here would assert against
    /// an empty list and pass for the wrong reason. A fixture that means to test something else
    /// says so rather than quietly agreeing.
    /// </remarks>
    private static Ben.Data.Common.Interfaces.IFileStorageService EverythingExists()
    {
        var m = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        m.Setup(s => s.Exists(It.IsAny<string>())).Returns(true);
        return m.Object;
    }

    private static MediaLibraryController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new MediaLibraryController(factory, CreateMapper(), EverythingExists());
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

    private static async Task<List<UploadFileRecord>> GetFilesAsync(
        MediaLibraryController ctrl,
        string? contentTypePrefixes = null,
        string? scope = null,
        Guid? caseId = null,
        Guid? investigationId = null)
    {
        var result = await ctrl.GetFiles(contentTypePrefixes, scope, caseId, investigationId, CancellationToken.None);
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

    /// <summary>
    /// A file that is really there and is really its own file.
    /// </summary>
    /// <remarks>
    /// <para>Two things here are load-bearing rather than decorative, and both were added when the
    /// listing learned to drop rows that are not real files (2026-09-21).</para>
    ///
    /// <para><b>A storage path</b>, because a row with neither a path nor bytes is precisely what
    /// the new rule discards — the p201.jpg case. Without one every fixture in this file would
    /// assert against an empty list and pass for the wrong reason.</para>
    ///
    /// <para><b>A distinct name</b>, because the listing now shows a file once. Every fixture used
    /// to be called clip.mp4 at 1024 bytes, so two of them are the same file by any test a person
    /// could apply, and a fixture that means to check two files must use two.</para>
    /// </remarks>
    private static UploadFile MakeFile(Guid userId, string contentType = "video/mp4", bool isPublic = false)
    {
        var id = Guid.NewGuid();
        var stored = $"{id:N}.mp4";
        return new()
        {
            Id                 = id,
            AppUserId          = userId,
            FileName           = $"clip-{id:N}.mp4",
            StoredFileName     = stored,
            StoragePath        = $"users/{userId}/{stored}",
            ContentType        = contentType,
            FileSize           = 1024,
            UploadFileTypeId   = Guid.NewGuid(),
            IsPublic           = isPublic,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
    }

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

    // ── What is not library media (Ben, 2026-09-21) ─────────────────────────────

    /// <summary>
    /// A profile photograph is somebody's face, not library media.
    /// </summary>
    /// <remarks>
    /// Avatars are public so that other people can see them, which is exactly how they arrived in
    /// everybody's library. The site's own default avatars — so-user-circle.png and its siblings —
    /// are profile photographs owned by the site, so the same rule removes those too.
    /// </remarks>
    [Fact]
    public async Task GetFiles_LeavesOutProfilePhotographs()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            var avatar = MakeFile(userId, "image/png");
            avatar.UploadFileTypeId = Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.ProfilePhotoFileTypeId;
            db.UploadFiles.Add(avatar);
            db.UploadFiles.Add(MakeFile(userId, "image/png"));   // an ordinary picture of mine
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.NotEqual(Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.ProfilePhotoFileTypeId,
                        files[0].UploadFileTypeId);
    }

    /// <summary>
    /// A row whose bytes are not there is worse than absent.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-21: <i>"p201.jpg or test-photo.jpg can't be loaded so they should not show up
    /// in the media library"</i>. A card that never draws is a thing a person tries twice before
    /// concluding the site is broken.
    /// </remarks>
    [Fact]
    public async Task GetFiles_LeavesOutFilesWhoseBytesAreGone()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var missing = MakeFile(userId, "image/jpeg");
        var present = MakeFile(userId, "image/jpeg");

        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.AddRange(missing, present);
            await db.SaveChangesAsync();
        }

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>())).Returns(true);
        storage.Setup(s => s.Exists(missing.StoragePath!)).Returns(false);

        var controller = new MediaLibraryController(factory, CreateMapper(), storage.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

        var files = await GetFilesAsync(controller);
        Assert.Single(files);
        Assert.Equal(present.Id, files[0].Id);
    }

    /// <summary>
    /// A storage fault must not empty the library.
    /// </summary>
    /// <remarks>
    /// A provider that cannot answer is not evidence that a file is gone, and a listing that
    /// vanishes the moment storage hiccups is a worse failure than the one this rule fixes.
    /// </remarks>
    [Fact]
    public async Task GetFiles_KeepsListingWhenStorageCannotAnswer()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(userId, "image/jpeg"));
            await db.SaveChangesAsync();
        }

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>())).Throws(new IOException("storage is away"));

        var controller = new MediaLibraryController(factory, CreateMapper(), storage.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

        Assert.Single(await GetFilesAsync(controller));
    }

    /// <summary>
    /// The same file, uploaded three times, is one row in the library.
    /// </summary>
    /// <remarks>
    /// Ben found IMG_1997.JPG listed several times. Each listing is a real and separate row — the
    /// same photograph uploaded to a case, to an event and to a group is three uploads — and the
    /// oldest survives, because it is the one the others were copied from.
    /// </remarks>
    [Fact]
    public async Task GetFiles_ShowsTheSameFileOnce()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var oldest = DateTime.UtcNow.AddDays(-9);

        await using (var db = factory.CreateDbContext())
        {
            foreach (var day in new[] { 0, 3, 6 })
            {
                var copy = MakeFile(userId, "image/jpeg");
                copy.FileName = "IMG_1997.JPG";
                copy.FileSize = 4096;
                copy.DateCreated = oldest.AddDays(day);
                db.UploadFiles.Add(copy);
            }
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Single(files);
        Assert.Equal(oldest, files[0].DateCreated);
    }

    /// <summary>Two different photographs that happen to share a name are still two.</summary>
    [Fact]
    public async Task GetFiles_DoesNotCollapseDifferentFilesWithTheSameName()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            var a = MakeFile(userId, "image/jpeg");
            a.FileName = "IMG_1997.JPG"; a.FileSize = 4096;
            var b = MakeFile(userId, "image/jpeg");
            b.FileName = "IMG_1997.JPG"; b.FileSize = 91_233;
            db.UploadFiles.AddRange(a, b);
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, (await GetFilesAsync(Build(factory, userId))).Count);
    }

    /// <summary>
    /// Being public does not put somebody else's file in your library.
    /// </summary>
    /// <remarks>
    /// <para>This test used to assert the opposite, and the opposite is what turned this listing
    /// into a dumping ground for the whole site (Ben, 2026-09-21: "when someone uploads media, it
    /// should not automatically be added to the media library as public").</para>
    ///
    /// <para><c>IsPublic</c> is set by a dozen paths that have nothing to do with a media library
    /// — a venue photograph, an event gallery, a tour gallery, a feed post's picture, an accepted
    /// piece of event evidence, an avatar — and every one of them landed in every user's library.
    /// A public SHARE is the deliberate act and still lists, which the next test holds.</para>
    /// </remarks>
    [Fact]
    public async Task GetFiles_DoesNotListSomebodyElsesFileJustBecauseItIsPublic()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(Guid.NewGuid(), "image/png", isPublic: true));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await GetFilesAsync(Build(factory, userId)));
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

    // ── Scoping (item 91) ────────────────────────────────────────────────────

    /// <summary>
    /// The property everything else rests on: a scope narrows, and cannot widen.
    /// </summary>
    /// <remarks>
    /// This is the one that would matter if the implementation were rearranged. The controller
    /// computes the full audience union first and intersects a scope over the result, so a caller
    /// naming a case they have no access to gets nothing. Weaving the scope into the union — the
    /// obvious "optimisation" — would turn the query string into a way of reading other people's
    /// case media.
    /// </remarks>
    [Fact]
    public async Task GetFiles_CaseScope_CannotReachACaseTheCallerHasNoAccessTo()
    {
        var (factory, userId, _, _) = await SeedAsync();

        var strangerOrgId = Guid.NewGuid();
        var strangerCaseId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.Organizations.Add(new Organization
            {
                Id = strangerOrgId, Name = "Someone else", UrlName = "else",
                CreatedByAppUserId = strangerId, DateCreated = DateTime.UtcNow,
            });
            db.Cases.Add(new Case
            {
                Id = strangerCaseId, OrganizationId = strangerOrgId, Title = "Not yours",
                StreetAddress1 = "2 Main", City = "Nashville", State = "TN",
                ZipCode = "37201", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = strangerId,
            });

            var theirFile = MakeFile(strangerId, "video/mp4");
            db.UploadFiles.Add(theirFile);
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = strangerCaseId, UploadFileId = theirFile.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = strangerId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId), scope: "case", caseId: strangerCaseId);

        Assert.Empty(files);
    }

    [Fact]
    public async Task GetFiles_PersonalScope_ReturnsOnlyOwnFiles()
    {
        var (factory, userId, _, caseId) = await SeedAsync();
        var mineId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            var mine = MakeFile(userId, "video/mp4");
            mine.Id = mineId;
            db.UploadFiles.Add(mine);

            // Reachable through the case, but not mine.
            var theirs = MakeFile(otherOwnerId, "video/mp4");
            db.UploadFiles.Add(theirs);
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = theirs.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = otherOwnerId,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, (await GetFilesAsync(Build(factory, userId))).Count);

        var personal = await GetFilesAsync(Build(factory, userId), scope: "personal");
        Assert.Single(personal);
        Assert.Equal(mineId, personal[0].Id);
    }

    [Fact]
    public async Task GetFiles_CaseScope_ReturnsThatCasesMediaAndNothingElse()
    {
        var (factory, userId, _, caseId) = await SeedAsync();
        var onTheCaseId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            var onTheCase = MakeFile(userId, "video/mp4");
            onTheCase.Id = onTheCaseId;
            db.UploadFiles.Add(onTheCase);
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = onTheCaseId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

            // Mine, and reachable, but attached to no case.
            db.UploadFiles.Add(MakeFile(userId, "video/mp4"));
            await db.SaveChangesAsync();
        }

        var scoped = await GetFilesAsync(Build(factory, userId), scope: "case", caseId: caseId);

        Assert.Single(scoped);
        Assert.Equal(onTheCaseId, scoped[0].Id);
    }

    /// <summary>
    /// A case scope with no case chosen returns nothing, rather than everything.
    /// </summary>
    /// <remarks>
    /// The tempting reading is "no case named, so do not filter". That turns a half-made selection
    /// into the widest possible answer, which is the opposite of what the person was in the middle
    /// of asking for.
    /// </remarks>
    [Fact]
    public async Task GetFiles_CaseScopeWithNoCase_ReturnsNothing()
    {
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(userId, "video/mp4"));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await GetFilesAsync(Build(factory, userId), scope: "case"));
    }

    [Fact]
    public async Task GetFiles_UnknownScope_BehavesAsNoScope()
    {
        // A typo should not blank the media tab.
        var (factory, userId, _, _) = await SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.UploadFiles.Add(MakeFile(userId, "video/mp4"));
            await db.SaveChangesAsync();
        }

        Assert.Single(await GetFilesAsync(Build(factory, userId), scope: "nonsense"));
    }

    [Fact]
    public async Task GetScopes_OffersOnlyCasesAtTheCallersOwnOrganizations()
    {
        var (factory, userId, _, caseId) = await SeedAsync();

        await using (var db = factory.CreateDbContext())
        {
            var strangerOrgId = Guid.NewGuid();
            db.Organizations.Add(new Organization
            {
                Id = strangerOrgId, Name = "Someone else", UrlName = "else",
                CreatedByAppUserId = Guid.NewGuid(), DateCreated = DateTime.UtcNow,
            });
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = strangerOrgId, Title = "Not yours",
                StreetAddress1 = "2 Main", City = "Nashville", State = "TN",
                ZipCode = "37201", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetScopes(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var scopes = ((IEnumerable<MediaScopeCase>)ok.Value!).ToList();

        var offered = Assert.Single(scopes);
        Assert.Equal(caseId, offered.Id);
    }

    // ── Owner and case, so two identical file names can be told apart (V-3) ──

    /// <summary>
    /// Every listed file says who owns it.
    /// </summary>
    /// <remarks>
    /// V-3 of the 2026-09-06 evaluation: the video editor's Server tab showed seven identical
    /// <c>test-audio.mp3</c> rows — other people's uploads, reachable through a shared group —
    /// with no owner and no case. A file name is not an identity, and this listing is the one
    /// place that spans several people's files at once.
    /// </remarks>
    [Fact]
    public async Task GetFiles_SaysWhoOwnsEachFile()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var otherId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.AppUsers.AddRange(
                new AppUser { Id = userId,  DisplayName = "Sarah Mitchell", Email = "sarah@t.com", DateCreated = DateTime.UtcNow },
                new AppUser { Id = otherId, DisplayName = "James Thornton", Email = "james@t.com", DateCreated = DateTime.UtcNow });

            var mine   = MakeFile(userId);
            // Shared with me deliberately. It used to be reachable by its public FLAG alone, which
            // is the thing that stopped listing — see the test above.
            var theirs = MakeFile(otherId);
            db.UploadFiles.AddRange(mine, theirs);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = theirs.Id, IsActive = true,
                TargetType = ShareTargetType.Person, TargetAppUserId = userId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = otherId,
            });
            await db.SaveChangesAsync();
        }

        var files = await GetFilesAsync(Build(factory, userId));
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.OwnerDisplayName == "Sarah Mitchell");
        Assert.Contains(files, f => f.OwnerDisplayName == "James Thornton");
    }

    /// <summary>A file handed to a group names the group, and says it is one.</summary>
    /// <remarks>
    /// Item 180 Phase B leaves such a file with no owning person at all, so "who owns this" has
    /// to have a second answer or the row goes back to being anonymous.
    /// </remarks>
    [Fact]
    public async Task GetFiles_NamesTheGroupWhenAFileWasHandedOver()
    {
        var (factory, userId, orgId, _) = await SeedAsync();

        await using (var db = factory.CreateDbContext())
        {
            var handedOver = MakeFile(userId);
            handedOver.AppUserId           = null;
            handedOver.OwnerOrganizationId = orgId;
            db.UploadFiles.Add(handedOver);
            // Shared with the group that now owns it, because a public flag alone no longer lists.
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = handedOver.Id, IsActive = true,
                TargetType = ShareTargetType.Organization, TargetOrganizationId = orgId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var file = Assert.Single(await GetFilesAsync(Build(factory, userId)));
        Assert.Equal("Org (group)", file.OwnerDisplayName);
    }

    /// <summary>A file attached to a case carries the case's reference.</summary>
    [Fact]
    public async Task GetFiles_SaysWhichCaseAFileBelongsTo()
    {
        var (factory, userId, _, caseId) = await SeedAsync();

        await using (var db = factory.CreateDbContext())
        {
            db.AppUsers.Add(new AppUser { Id = userId, DisplayName = "Sarah Mitchell", Email = "sarah@t.com", DateCreated = DateTime.UtcNow });

            var attached = MakeFile(userId);
            var loose    = MakeFile(userId);
            db.UploadFiles.AddRange(attached, loose);

            var seeded = await db.Cases.FirstAsync(c => c.Id == caseId);
            seeded.CaseYear      = 2026;
            seeded.OrgCaseNumber = 3;

            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = attached.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();

            var files = await GetFilesAsync(Build(factory, userId));
            Assert.Equal(2, files.Count);
            Assert.Equal("#2026-003", files.Single(f => f.Id == attached.Id).CaseReference);

            // And a file that belongs to no case says nothing rather than guessing.
            Assert.Null(files.Single(f => f.Id == loose.Id).CaseReference);
        }
    }
}
