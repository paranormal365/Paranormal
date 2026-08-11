using AutoMapper;
using Ben.Data.Common.Constants;
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
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="UploadFilePermissionRequestController"/>.
/// <para>
/// Phase-B regression focus: <c>Review</c> previously had no authorization at all — any
/// authenticated caller could approve their own (or anyone's) access request, with
/// <c>ReviewedByAppUserId</c> spoofable from the body. <c>Submit</c> similarly let the caller
/// spoof <c>RequestedByAppUserId</c>. <c>GetForFile</c> returned every request on a file to any
/// authenticated caller. Each fix's test below runs as the specific caller who should legitimately
/// be allowed, and asserts the exact previously-working attacker shape is now rejected.
/// </para>
/// </summary>
public class UploadFilePermissionRequestControllerTests
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
        m.Setup(x => x.Map<IEnumerable<UploadFilePermissionRequestRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFilePermissionRequest> list) return [];
             return list.Select(r => new UploadFilePermissionRequestRecord
             {
                 Id = r.Id, UploadFileId = r.UploadFileId,
                 RequestedByAppUserId = r.RequestedByAppUserId,
                 OrganizationId = r.OrganizationId,
                 PermissionType = r.PermissionType,
                 RequestStatus = r.RequestStatus,
             });
         });
        m.Setup(x => x.Map<UploadFilePermissionRequestRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFilePermissionRequest r) return new UploadFilePermissionRequestRecord();
             return new UploadFilePermissionRequestRecord
             {
                 Id = r.Id, UploadFileId = r.UploadFileId,
                 RequestedByAppUserId = r.RequestedByAppUserId,
                 OrganizationId = r.OrganizationId,
                 PermissionType = r.PermissionType,
                 RequestStatus = r.RequestStatus,
                 ReviewedByAppUserId = r.ReviewedByAppUserId,
             };
         });
        return m.Object;
    }

    private static UploadFilePermissionRequestController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId, bool isSuperAdmin = false)
    {
        var ctrl = new UploadFilePermissionRequestController(factory, CreateMapper(), new Mock<IAuditLogService>().Object);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
        return ctrl;
    }

    private static async Task<Guid> SeedFileAsync(IDbContextFactory<BenDataContext> factory,
        Guid ownerId)
    {
        var fileId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "f.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg",
            FileSize = 1, FileData = new byte[1],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private static async Task<Guid> SeedRequestAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, Guid requesterId,
        FilePermissionRequestStatus status = FilePermissionRequestStatus.Pending,
        Guid? organizationId = null,
        DateTime? created = null)
    {
        var reqId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFilePermissionRequests.Add(new UploadFilePermissionRequest
        {
            Id = reqId, UploadFileId = fileId, RequestedByAppUserId = requesterId,
            OrganizationId = organizationId,
            PermissionType = FilePermissionType.Use, RequestStatus = status,
            DateCreated = created ?? DateTime.UtcNow, CreatedByAppUserId = requesterId,
        });
        await db.SaveChangesAsync();
        return reqId;
    }

    private static async Task AddMembershipAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId,
        OrganizationMemberRole role = OrganizationMemberRole.Administrator, bool isActive = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = role, IsActive = isActive, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    // ── GetForFile ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetForFile_Owner_ReturnsAllStatuses_ForThatFile()
    {
        var factory     = CreateFactory();
        var ownerId     = Guid.NewGuid();
        var fileId      = await SeedFileAsync(factory, ownerId);
        var requesterId = Guid.NewGuid();
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Pending);
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Approved);
        var ctrl = Build(factory, ownerId);

        var result = await ctrl.GetForFile(fileId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetForFile_SuperAdmin_SeesAllRequests()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid());
        var ctrl = Build(factory, Guid.NewGuid(), isSuperAdmin: true);

        var result = await ctrl.GetForFile(fileId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }

    [Fact]
    public async Task GetForFile_OrgAdmin_SeesOnlyRequestsScopedToTheirOrg()
    {
        // The core of the fix: this used to return every request on the file to any caller.
        var factory  = CreateFactory();
        var ownerId  = Guid.NewGuid();
        var fileId   = await SeedFileAsync(factory, ownerId);
        var orgA     = Guid.NewGuid();
        var orgB     = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        await AddMembershipAsync(factory, orgA, adminId);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid(), organizationId: orgA);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid(), organizationId: orgB);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid(), organizationId: null); // person-to-person

        var ctrl = Build(factory, adminId);
        var result = await ctrl.GetForFile(fileId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value).ToList();
        Assert.Single(list);
        Assert.Equal(orgA, list[0].OrganizationId);
    }

    [Fact]
    public async Task GetForFile_UnrelatedCaller_SeesEmptyList()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid(), organizationId: Guid.NewGuid());

        var ctrl = Build(factory, Guid.NewGuid());
        var result = await ctrl.GetForFile(fileId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }

    [Fact]
    public async Task GetForFile_FileNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetForFile(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── GetPendingForReviewer ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingForReviewer_Self_ReturnsPendingRequests_OnOwnedFiles()
    {
        var factory      = CreateFactory();
        var ownerId      = Guid.NewGuid();
        var fileId       = await SeedFileAsync(factory, ownerId);
        var requesterId  = Guid.NewGuid();
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Pending);
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Approved); // not pending
        var ctrl = Build(factory, ownerId);

        var result = await ctrl.GetPendingForReviewer(ownerId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value).ToList();
        Assert.Single(list);
        Assert.Equal(FilePermissionRequestStatus.Pending, list[0].RequestStatus);
    }

    [Fact]
    public async Task GetPendingForReviewer_DifferentUser_ReturnsForbid()
    {
        // The core of the fix: reviewerAppUserId is a route parameter that used to be trusted
        // unchecked — any caller could pass any other user's id and see their pending requests.
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid(), FilePermissionRequestStatus.Pending);

        var ctrl = Build(factory, Guid.NewGuid());
        var result = await ctrl.GetPendingForReviewer(ownerId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetPendingForReviewer_SuperAdmin_CanQueryAnyReviewer()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        await SeedRequestAsync(factory, fileId, Guid.NewGuid(), FilePermissionRequestStatus.Pending);

        var ctrl = Build(factory, Guid.NewGuid(), isSuperAdmin: true);
        var result = await ctrl.GetPendingForReviewer(ownerId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }

    // ── Submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_UsesCallerIdentity_NotBody()
    {
        // The body no longer carries a RequestedByAppUserId field at all — confirms the created
        // request is tied to whoever actually called the endpoint.
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        var callerId = Guid.NewGuid();
        var ctrl = Build(factory, callerId);

        var result = await ctrl.Submit(fileId,
            new SubmitRequestBody(null, FilePermissionType.Use, "please"), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFilePermissionRequestRecord>(created.Value);
        Assert.Equal(callerId, record.RequestedByAppUserId);
    }

    // ── Review ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Review_FileOwner_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        var reqId   = await SeedRequestAsync(factory, fileId, Guid.NewGuid());
        var ctrl    = Build(factory, ownerId);

        var result = await ctrl.Review(reqId,
            new ReviewRequestBody(FilePermissionRequestStatus.Approved, "ok"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UploadFilePermissionRequestRecord>(ok.Value);
        Assert.Equal(FilePermissionRequestStatus.Approved, record.RequestStatus);
        Assert.Equal(ownerId, record.ReviewedByAppUserId);
    }

    [Fact]
    public async Task Review_OrgAdmin_OfRequestsOrg_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        var orgId   = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, adminId);
        var reqId = await SeedRequestAsync(factory, fileId, Guid.NewGuid(), organizationId: orgId);
        var ctrl  = Build(factory, adminId);

        var result = await ctrl.Review(reqId,
            new ReviewRequestBody(FilePermissionRequestStatus.Approved, null), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Review_RequesterCannotSelfApprove()
    {
        // The exact vulnerability this fix closes: the requester themselves, with no ownership
        // or org-admin relationship to the file, used to be able to approve their own request.
        var factory     = CreateFactory();
        var ownerId     = Guid.NewGuid();
        var fileId      = await SeedFileAsync(factory, ownerId);
        var requesterId = Guid.NewGuid();
        var reqId       = await SeedRequestAsync(factory, fileId, requesterId);
        var ctrl        = Build(factory, requesterId);

        var result = await ctrl.Review(reqId,
            new ReviewRequestBody(FilePermissionRequestStatus.Approved, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Review_UnrelatedCaller_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = await SeedFileAsync(factory, ownerId);
        var reqId   = await SeedRequestAsync(factory, fileId, Guid.NewGuid(), organizationId: Guid.NewGuid());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result = await ctrl.Review(reqId,
            new ReviewRequestBody(FilePermissionRequestStatus.Approved, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Review_RequestNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result = await ctrl.Review(Guid.NewGuid(),
            new ReviewRequestBody(FilePermissionRequestStatus.Approved, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Cancel (unchanged reference pattern) ─────────────────────────────────

    [Fact]
    public async Task Cancel_NonRequester_ReturnsForbid()
    {
        var factory      = CreateFactory();
        var ownerId      = Guid.NewGuid();
        var fileId       = await SeedFileAsync(factory, ownerId);
        var requesterId  = Guid.NewGuid();
        var reqId        = await SeedRequestAsync(factory, fileId, requesterId);
        var ctrl         = Build(factory, Guid.NewGuid());

        var result = await ctrl.Cancel(reqId, Guid.NewGuid(), default);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
