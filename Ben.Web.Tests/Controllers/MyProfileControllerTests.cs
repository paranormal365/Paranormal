using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers;
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
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for MyProfileController — the first self-service account surface. The theme throughout
/// is that everything is scoped to the caller: no endpoint takes a user id, and files belonging
/// to someone else must be unusable as an avatar.
/// </summary>
public class MyProfileControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>
    /// Stand-in mapper. Carries every field the assertions read — a mapper that silently drops
    /// one turns a test of the controller into a test of this helper.
    /// </summary>
    private static IMapper CreateMapper()
    {
        static AppUserPhotoRecord ToRecord(AppUserPhoto p) => new()
        {
            Id = p.Id, AppUserId = p.AppUserId, UploadFileId = p.UploadFileId,
            AltText = p.AltText, IsPublic = p.IsPublic, IsActive = p.IsActive,
            DateCreated = p.DateCreated, DateUpdated = p.DateUpdated,
            CreatedByAppUserId = p.CreatedByAppUserId, UpdatedByAppUserId = p.UpdatedByAppUserId,
            FileName = p.UploadFile?.FileName,
        };

        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<AppUserPhotoRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is AppUserPhoto p ? ToRecord(p) : new AppUserPhotoRecord());
        m.Setup(x => x.Map<List<AppUserPhotoRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<AppUserPhoto> list ? list.Select(ToRecord).ToList() : []);
        m.Setup(x => x.Map<IEnumerable<AppUserPhotoRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<AppUserPhoto> list ? list.Select(ToRecord).ToList() : []);
        return m.Object;
    }

    private static MyProfileController Build(IDbContextFactory<BenDataContext> factory, Guid? userId)
    {
        var ctrl = new MyProfileController(factory, CreateMapper(), Mock.Of<IAuditLogService>());
        var principal = userId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid userId)> SeedAsync(
        string? displayName = "Original Name")
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser
        {
            Id = userId, UserName = "me@test.com", NormalizedUserName = "ME@TEST.COM",
            Email = "me@test.com", NormalizedEmail = "ME@TEST.COM",
            DisplayName = displayName, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (factory, userId);
    }

    private static async Task<Guid> AddFileAsync(
        IDbContextFactory<BenDataContext> factory, Guid ownerId, bool isPublic = false)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "face.png", StoredFileName = "s.png", ContentType = "image/png",
            FileSize = 1, IsPublic = isPublic,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<MyProfileRecord> GetProfileAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var result = await Build(factory, userId).GetProfile(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<MyProfileRecord>(ok.Value);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfile_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var result = await Build(CreateFactory(), userId: null).GetProfile(default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task SetPhoto_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var result = await Build(CreateFactory(), userId: null)
            .SetPhoto(new SetMyPhotoRequest(Guid.NewGuid(), true, null), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfile_ReturnsTheCallersOwnDetails()
    {
        var (factory, userId) = await SeedAsync();

        var profile = await GetProfileAsync(factory, userId);

        Assert.Equal(userId, profile.AppUserId);
        Assert.Equal("Original Name", profile.DisplayName);
        Assert.Equal("me@test.com", profile.Email);
        Assert.Null(profile.PublicPhoto);
        Assert.Null(profile.PrivatePhoto);
    }

    [Fact]
    public async Task Gender_RoundTrips_AndNotProvidedClears(
        )
    {
        // Item 163: self-declared, optional, feeds only the default-avatar choice. Null in the
        // request leaves it alone; NotProvided is a real answer that clears it.
        var (factory, userId) = await SeedAsync();

        await Build(factory, userId).UpdateProfile(
            new UpdateMyProfileRequest(null, Gender: Ben.Data.Common.Enums.ClientGender.Female), default);
        Assert.Equal(Ben.Data.Common.Enums.ClientGender.Female,
            (await GetProfileAsync(factory, userId)).Gender);

        await Build(factory, userId).UpdateProfile(new UpdateMyProfileRequest(null), default);
        Assert.Equal(Ben.Data.Common.Enums.ClientGender.Female,
            (await GetProfileAsync(factory, userId)).Gender);

        await Build(factory, userId).UpdateProfile(
            new UpdateMyProfileRequest(null, Gender: Ben.Data.Common.Enums.ClientGender.NotProvided), default);
        Assert.Equal(Ben.Data.Common.Enums.ClientGender.NotProvided,
            (await GetProfileAsync(factory, userId)).Gender);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.AppUsers.FindAsync(userId))!.Gender);
    }

    [Fact]
    public async Task UpdateProfile_SetsDisplayName()
    {
        var (factory, userId) = await SeedAsync();

        await Build(factory, userId).UpdateProfile(new UpdateMyProfileRequest("  Ben C  "), default);

        Assert.Equal("Ben C", (await GetProfileAsync(factory, userId)).DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_NullDisplayName_LeavesTheExistingNameAlone()
    {
        var (factory, userId) = await SeedAsync();

        await Build(factory, userId).UpdateProfile(new UpdateMyProfileRequest(null), default);

        // "Not supplied" must not be read as "clear it" — that distinction is the whole reason
        // the request takes a nullable string rather than a plain one.
        Assert.Equal("Original Name", (await GetProfileAsync(factory, userId)).DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_WhitespaceDisplayName_ClearsIt()
    {
        var (factory, userId) = await SeedAsync();

        await Build(factory, userId).UpdateProfile(new UpdateMyProfileRequest("   "), default);

        Assert.Null((await GetProfileAsync(factory, userId)).DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_RejectsAnOverlongDisplayName()
    {
        var (factory, userId) = await SeedAsync();

        var result = await Build(factory, userId)
            .UpdateProfile(new UpdateMyProfileRequest(new string('x', 101)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Original Name", (await GetProfileAsync(factory, userId)).DisplayName);
    }

    // ── Photos ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetPhoto_FillsTheRequestedSlotAndShowsUpOnTheProfile()
    {
        var (factory, userId) = await SeedAsync();
        var fileId = await AddFileAsync(factory, userId);

        var result = await Build(factory, userId)
            .SetPhoto(new SetMyPhotoRequest(fileId, IsPublic: true, "me"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var photo = Assert.IsType<AppUserPhotoRecord>(ok.Value);
        Assert.True(photo.IsActive);
        Assert.True(photo.IsPublic);

        var profile = await GetProfileAsync(factory, userId);
        Assert.Equal(photo.Id, profile.PublicPhoto?.Id);
        Assert.Null(profile.PrivatePhoto);
    }

    [Fact]
    public async Task SetPhoto_MakesThePublicSlotsFilePublic()
    {
        var (factory, userId) = await SeedAsync();
        var fileId = await AddFileAsync(factory, userId, isPublic: false);

        await Build(factory, userId)
            .SetPhoto(new SetMyPhotoRequest(fileId, IsPublic: true, null), default);

        // The avatar endpoint serves this to anyone, so a private underlying file would leave the
        // photo set but unservable — the row and the storage flag have to agree.
        await using var db = await factory.CreateDbContextAsync();
        Assert.True((await db.UploadFiles.FindAsync(fileId))!.IsPublic);
    }

    [Fact]
    public async Task SetPhoto_ReplacingASlotDeactivatesOnlyThatSlot()
    {
        var (factory, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);

        var privateFile = await AddFileAsync(factory, userId);
        var firstPublic = await AddFileAsync(factory, userId);
        var secondPublic = await AddFileAsync(factory, userId);

        await ctrl.SetPhoto(new SetMyPhotoRequest(privateFile, IsPublic: false, null), default);
        await ctrl.SetPhoto(new SetMyPhotoRequest(firstPublic, IsPublic: true, null), default);
        await ctrl.SetPhoto(new SetMyPhotoRequest(secondPublic, IsPublic: true, null), default);

        var profile = await GetProfileAsync(factory, userId);
        Assert.Equal(secondPublic, profile.PublicPhoto?.UploadFileId);
        // The private slot was never mentioned by the two public calls and must survive them.
        Assert.Equal(privateFile, profile.PrivatePhoto?.UploadFileId);

        // The replaced photo is kept, just no longer active — the user can re-select it later.
        await using var db = await factory.CreateDbContextAsync();
        var all = await db.AppUserPhotos.Where(p => p.AppUserId == userId).ToListAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Count(p => p.IsActive));
        Assert.False(all.Single(p => p.UploadFileId == firstPublic).IsActive);
    }

    [Fact]
    public async Task SetPhoto_ConcurrentWritesToOneSlotLeaveExactlyOneActive()
    {
        var (factory, userId) = await SeedAsync();
        var files = new List<Guid>();
        for (var i = 0; i < 6; i++) files.Add(await AddFileAsync(factory, userId));

        // A double-click is the realistic version of this. Against SQL Server the filtered unique
        // index rejects the losers, which is why SetPhoto retries; the InMemory provider ignores
        // index filters entirely, so this asserts the controller's own last-write-wins bookkeeping
        // rather than the constraint. The constraint itself was verified live against SQL Server.
        await Task.WhenAll(files.Select(f =>
            Build(factory, userId).SetPhoto(new SetMyPhotoRequest(f, IsPublic: true, null), default)));

        await using var db = await factory.CreateDbContextAsync();
        var active = await db.AppUserPhotos
            .Where(p => p.AppUserId == userId && p.IsPublic && p.IsActive)
            .ToListAsync();

        Assert.Single(active);
        Assert.Equal(6, await db.AppUserPhotos.CountAsync(p => p.AppUserId == userId));
    }

    [Fact]
    public async Task SetPhoto_RefusesAFileTheCallerDoesNotOwn()
    {
        var (factory, userId) = await SeedAsync();
        var someoneElsesFile = await AddFileAsync(factory, Guid.NewGuid());

        var result = await Build(factory, userId)
            .SetPhoto(new SetMyPhotoRequest(someoneElsesFile, IsPublic: true, null), default);

        // Without this the site would happily serve another user's private upload as an avatar.
        Assert.IsType<ForbidResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.AppUserPhotos.AnyAsync());
        Assert.False((await db.UploadFiles.FindAsync(someoneElsesFile))!.IsPublic);
    }

    [Fact]
    public async Task SetPhoto_ReturnsNotFound_ForAMissingFile()
    {
        var (factory, userId) = await SeedAsync();

        var result = await Build(factory, userId)
            .SetPhoto(new SetMyPhotoRequest(Guid.NewGuid(), IsPublic: true, null), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeletePhoto_ClearsTheSlotButKeepsTheFile()
    {
        var (factory, userId) = await SeedAsync();
        var fileId = await AddFileAsync(factory, userId);
        var ctrl = Build(factory, userId);

        var created = (AppUserPhotoRecord)((OkObjectResult)(await ctrl
            .SetPhoto(new SetMyPhotoRequest(fileId, IsPublic: true, null), default)).Result!).Value!;

        Assert.IsType<NoContentResult>(await ctrl.DeletePhoto(created.Id, default));

        Assert.Null((await GetProfileAsync(factory, userId)).PublicPhoto);
        await using var db = await factory.CreateDbContextAsync();
        Assert.NotNull(await db.UploadFiles.FindAsync(fileId));
    }

    [Fact]
    public async Task DeletePhoto_ReturnsNotFound_ForSomeoneElsesPhoto()
    {
        var (factory, ownerId) = await SeedAsync();
        var fileId = await AddFileAsync(factory, ownerId);
        var created = (AppUserPhotoRecord)((OkObjectResult)(await Build(factory, ownerId)
            .SetPhoto(new SetMyPhotoRequest(fileId, IsPublic: true, null), default)).Result!).Value!;

        var result = await Build(factory, Guid.NewGuid()).DeletePhoto(created.Id, default);

        // NotFound rather than Forbid: telling a stranger "forbidden" confirms the row exists.
        Assert.IsType<NotFoundResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.AppUserPhotos.AnyAsync(p => p.Id == created.Id));
    }

    [Fact]
    public async Task GetPhotos_ReturnsOnlyTheCallersOwn()
    {
        var (factory, userId) = await SeedAsync();
        var mine = await AddFileAsync(factory, userId);
        await Build(factory, userId).SetPhoto(new SetMyPhotoRequest(mine, true, null), default);

        var strangerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = strangerId, UserName = "x@test.com", NormalizedUserName = "X@TEST.COM",
                Email = "x@test.com", NormalizedEmail = "X@TEST.COM", DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var theirs = await AddFileAsync(factory, strangerId);
        await Build(factory, strangerId).SetPhoto(new SetMyPhotoRequest(theirs, true, null), default);

        var result = await Build(factory, userId).GetPhotos(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var photos = Assert.IsAssignableFrom<IEnumerable<AppUserPhotoRecord>>(ok.Value).ToList();

        Assert.Single(photos);
        Assert.Equal(mine, photos[0].UploadFileId);
    }

    // ── Private-photo consent (U2a) ───────────────────────────────────────────

    private static async Task AddMembershipAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId, bool orgAllows, bool isActive = true)
    {
        var orgId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Org", UrlName = $"org-{orgId:N}",
            AllowMemberPrivatePhotosToClients = orgAllows,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = isActive,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Profile_ConsentDefaultsToWithheld()
    {
        var (factory, userId) = await SeedAsync();

        // Consent is something you give. A new account must not start out sharing.
        Assert.False((await GetProfileAsync(factory, userId)).SharePrivatePhotoWithClients);
    }

    [Fact]
    public async Task UpdateProfile_TogglesTheConsentOptIn()
    {
        var (factory, userId) = await SeedAsync();

        await Build(factory, userId).UpdateProfile(
            new UpdateMyProfileRequest(null, SharePrivatePhotoWithClients: true), default);
        Assert.True((await GetProfileAsync(factory, userId)).SharePrivatePhotoWithClients);

        await Build(factory, userId).UpdateProfile(
            new UpdateMyProfileRequest(null, SharePrivatePhotoWithClients: false), default);
        Assert.False((await GetProfileAsync(factory, userId)).SharePrivatePhotoWithClients);
    }

    [Fact]
    public async Task UpdateProfile_EditingTheNameLeavesConsentAlone()
    {
        var (factory, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.UpdateProfile(new UpdateMyProfileRequest(null, true), default);

        // Renaming yourself must not silently revoke consent you already gave — the whole reason
        // the request field is nullable rather than a plain bool.
        await ctrl.UpdateProfile(new UpdateMyProfileRequest("New Name"), default);

        var profile = await GetProfileAsync(factory, userId);
        Assert.Equal("New Name", profile.DisplayName);
        Assert.True(profile.SharePrivatePhotoWithClients);
    }

    [Theory]
    [InlineData(true,  true)]
    [InlineData(false, false)]
    public async Task Profile_ReportsWhetherAnyOrgPermitsSharing(bool orgAllows, bool expected)
    {
        var (factory, userId) = await SeedAsync();
        await AddMembershipAsync(factory, userId, orgAllows);

        Assert.Equal(expected, (await GetProfileAsync(factory, userId)).AnyOrgAllowsPrivatePhotoSharing);
    }

    [Fact]
    public async Task Profile_IgnoresPermissiveOrgsTheUserHasLeft()
    {
        var (factory, userId) = await SeedAsync();
        await AddMembershipAsync(factory, userId, orgAllows: true, isActive: false);

        // A lapsed membership is not a current relationship, so it must not make the opt-in look
        // effective — nor, later, actually share anything.
        Assert.False((await GetProfileAsync(factory, userId)).AnyOrgAllowsPrivatePhotoSharing);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true,  false, false)]
    [InlineData(false, true,  false)]
    [InlineData(true,  true,  true)]
    public void Consent_RequiresBothKeys(bool memberOptedIn, bool orgAllows, bool expected)
    {
        // The rule the whole feature rests on, asserted on the single helper both U3 and U4 will
        // call. Three of these four rows are the ones that matter: any single yes is still a no.
        Assert.Equal(expected, PrivatePhotoConsent.MayShowToClient(memberOptedIn, orgAllows));

        var member = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "u", DateCreated = DateTime.UtcNow,
            SharePrivatePhotoWithClients = memberOptedIn,
        };
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "O", UrlName = "o",
            AllowMemberPrivatePhotosToClients = orgAllows,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = member.Id,
        };
        Assert.Equal(expected, PrivatePhotoConsent.MayShowToClient(member, org));
    }

    [Fact]
    public void Consent_TreatsAMissingUserOrOrgAsNo()
    {
        // A null row must never read as permission — the failure mode is showing a face to
        // someone who was never agreed to.
        var member = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "u", DateCreated = DateTime.UtcNow,
            SharePrivatePhotoWithClients = true,
        };
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "O", UrlName = "o",
            AllowMemberPrivatePhotosToClients = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = member.Id,
        };

        Assert.False(PrivatePhotoConsent.MayShowToClient(null, org));
        Assert.False(PrivatePhotoConsent.MayShowToClient(member, null));
        Assert.False(PrivatePhotoConsent.MayShowToClient(null, null));
    }
}
