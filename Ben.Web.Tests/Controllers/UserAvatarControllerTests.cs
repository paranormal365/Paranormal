using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for the viewer-aware avatar endpoint. The question every case asks is the same: does this
/// viewer get the private photo, the public one, or nothing — and the private cases are the ones
/// that matter, because getting them wrong shows someone's face to a person they never agreed to.
/// </summary>
public class UserAvatarControllerTests
{
    private static readonly byte[] PublicBytes  = [1, 1, 1, 1];
    private static readonly byte[] PrivateBytes = [2, 2, 2, 2];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static UserAvatarController Build(IDbContextFactory<BenDataContext> factory, Guid? viewerId)
    {
        // No storage path is ever set in these tests, so resolution falls through to FileData and
        // the storage service is never called — the bytes prove which photo was chosen.
        var ctrl = new UserAvatarController(factory, Mock.Of<IFileStorageService>());
        var principal = viewerId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, viewerId.Value.ToString())], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return ctrl;
    }

    private static async Task<Guid> AddUserAsync(
        IDbContextFactory<BenDataContext> factory, string email, bool optedIn = false)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            SharePrivatePhotoWithClients = optedIn, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> AddOrgAsync(
        IDbContextFactory<BenDataContext> factory, bool allowsPhotos = false)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = id, Name = "Org", UrlName = $"org-{id:N}",
            AllowMemberPrivatePhotosToClients = allowsPhotos,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = id,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AddMemberAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId, bool isActive = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = isActive,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Makes <paramref name="clientId"/> the originating client of a case at an org.</summary>
    private static async Task AddClientCaseAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid clientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var requestId = Guid.NewGuid();
        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = clientId, Description = "d",
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.Cases.Add(new Case
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, ClientRequestId = requestId,
            Title = "Case", CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddPhotosAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId,
        bool withPublic = true, bool withPrivate = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (isPublic, bytes) in new[] { (true, PublicBytes), (false, PrivateBytes) })
        {
            if (isPublic && !withPublic) continue;
            if (!isPublic && !withPrivate) continue;

            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = userId,
                FileName = "p.png", StoredFileName = "p.png", ContentType = "image/png",
                FileSize = bytes.Length, FileData = bytes, IsPublic = isPublic,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.AppUserPhotos.Add(new AppUserPhoto
            {
                Id = Guid.NewGuid(), AppUserId = userId, UploadFileId = fileId,
                IsPublic = isPublic, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Which photo came back — compared by bytes, so there is no ambiguity.</summary>
    private static async Task<string> ResolveAsync(
        IDbContextFactory<BenDataContext> factory, Guid viewerId, Guid subjectId)
    {
        var result = await Build(factory, viewerId).GetAvatar(subjectId, default);
        if (result is NoContentResult) return "none";
        var file = Assert.IsType<FileContentResult>(result);
        if (file.FileContents.SequenceEqual(PrivateBytes)) return "private";
        if (file.FileContents.SequenceEqual(PublicBytes))  return "public";
        return "unknown";
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAvatar_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var result = await Build(CreateFactory(), viewerId: null).GetAvatar(Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result);
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_SeesTheirOwnPrivatePhoto()
    {
        var factory = CreateFactory();
        var me = await AddUserAsync(factory, "me@t.com");
        await AddPhotosAsync(factory, me);

        Assert.Equal("private", await ResolveAsync(factory, me, me));
    }

    [Fact]
    public async Task Colleagues_SeeEachOthersPrivatePhoto()
    {
        var factory = CreateFactory();
        var org     = await AddOrgAsync(factory);
        var subject = await AddUserAsync(factory, "s@t.com");
        var viewer  = await AddUserAsync(factory, "v@t.com");
        await AddMemberAsync(factory, org, subject);
        await AddMemberAsync(factory, org, viewer);
        await AddPhotosAsync(factory, subject);

        // Working together is the relationship; no consent flags are involved between colleagues.
        Assert.Equal("private", await ResolveAsync(factory, viewer, subject));
    }

    [Fact]
    public async Task AFormerColleague_DropsBackToThePublicPhoto()
    {
        var factory = CreateFactory();
        var org     = await AddOrgAsync(factory);
        var subject = await AddUserAsync(factory, "s@t.com");
        var viewer  = await AddUserAsync(factory, "v@t.com");
        await AddMemberAsync(factory, org, subject);
        await AddMemberAsync(factory, org, viewer, isActive: false);
        await AddPhotosAsync(factory, subject);

        // A lapsed membership is not a current relationship.
        Assert.Equal("public", await ResolveAsync(factory, viewer, subject));
    }

    [Fact]
    public async Task AStranger_GetsOnlyThePublicPhoto()
    {
        var factory = CreateFactory();
        var subject = await AddUserAsync(factory, "s@t.com");
        var viewer  = await AddUserAsync(factory, "v@t.com");
        await AddPhotosAsync(factory, subject);

        Assert.Equal("public", await ResolveAsync(factory, viewer, subject));
    }

    [Theory]
    [InlineData(false, false, "public")]
    [InlineData(true,  false, "public")]   // member opted in, org has not allowed it
    [InlineData(false, true,  "public")]   // org allows it, member has not opted in
    [InlineData(true,  true,  "private")]  // both keys — the only case that shares
    public async Task AClientSeesThePrivatePhotoOnlyWhenBothKeysAreSet(
        bool memberOptedIn, bool orgAllows, string expected)
    {
        var factory = CreateFactory();
        var org     = await AddOrgAsync(factory, allowsPhotos: orgAllows);
        var subject = await AddUserAsync(factory, "investigator@t.com", optedIn: memberOptedIn);
        var client  = await AddUserAsync(factory, "client@t.com");
        await AddMemberAsync(factory, org, subject);
        await AddClientCaseAsync(factory, org, client);
        await AddPhotosAsync(factory, subject);

        Assert.Equal(expected, await ResolveAsync(factory, client, subject));
    }

    [Fact]
    public async Task ConsentAtOneOrgDoesNotLeakToAClientOfAnother()
    {
        var factory     = CreateFactory();
        var sharingOrg  = await AddOrgAsync(factory, allowsPhotos: true);
        var otherOrg    = await AddOrgAsync(factory, allowsPhotos: true);
        var subject     = await AddUserAsync(factory, "s@t.com", optedIn: true);
        var otherClient = await AddUserAsync(factory, "c@t.com");

        await AddMemberAsync(factory, sharingOrg, subject);
        // The viewer is a client of an org the subject does not belong to.
        await AddClientCaseAsync(factory, otherOrg, otherClient);
        await AddPhotosAsync(factory, subject);

        // Both flags are true somewhere, but not for a pair that shares an org — permission has to
        // come from a relationship, not from the flags existing in the system.
        Assert.Equal("public", await ResolveAsync(factory, otherClient, subject));
    }

    // ── Fallbacks ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoPhotosAtAll_ReturnsNoContent()
    {
        var factory = CreateFactory();
        var subject = await AddUserAsync(factory, "s@t.com");
        var viewer  = await AddUserAsync(factory, "v@t.com");

        Assert.Equal("none", await ResolveAsync(factory, viewer, subject));
    }

    [Fact]
    public async Task PrivateOnlyPhoto_IsNotShownToSomeoneUnentitled()
    {
        var factory = CreateFactory();
        var subject = await AddUserAsync(factory, "s@t.com");
        var viewer  = await AddUserAsync(factory, "v@t.com");
        await AddPhotosAsync(factory, subject, withPublic: false);

        // No public photo to fall back to. It must return nothing rather than quietly serving the
        // private one — this is the case where a naive "just show whatever exists" leaks.
        Assert.Equal("none", await ResolveAsync(factory, viewer, subject));
    }

    [Fact]
    public async Task PublicOnlyPhoto_IsStillShownToAColleague()
    {
        var factory = CreateFactory();
        var org     = await AddOrgAsync(factory);
        var subject = await AddUserAsync(factory, "s@t.com");
        var viewer  = await AddUserAsync(factory, "v@t.com");
        await AddMemberAsync(factory, org, subject);
        await AddMemberAsync(factory, org, viewer);
        await AddPhotosAsync(factory, subject, withPrivate: false);

        // Entitled to the private one, but there isn't one — fall back rather than show nothing.
        Assert.Equal("public", await ResolveAsync(factory, viewer, subject));
    }
}
