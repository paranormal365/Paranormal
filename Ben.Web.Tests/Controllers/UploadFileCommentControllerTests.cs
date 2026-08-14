using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="UploadFileCommentController"/> — audience-gated posting and
/// owner-controlled comment settings.
/// </summary>
public class UploadFileCommentControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<UploadFileCommentRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFileComment e) return new UploadFileCommentRecord { Text = "" };
             return ToRecord(e);
         });
        m.Setup(x => x.Map<IEnumerable<UploadFileCommentRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFileComment> list) return [];
             return list.Select(ToRecord);
         });
        return m.Object;
    }

    private static UploadFileCommentRecord ToRecord(UploadFileComment e) => new()
    {
        Id = e.Id, UploadFileId = e.UploadFileId, AuthorAppUserId = e.AuthorAppUserId, Text = e.Text,
        IsOwner = e.IsOwner, IsInvestigationTeamMember = e.IsInvestigationTeamMember, IsClient = e.IsClient,
        IsOrganizationMember = e.IsOrganizationMember, IsPublicCommenter = e.IsPublicCommenter,
        DateCreated = e.DateCreated, DateUpdated = e.DateUpdated,
        CreatedByAppUserId = e.CreatedByAppUserId, UpdatedByAppUserId = e.UpdatedByAppUserId,
    };

    private static UploadFileCommentController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new UploadFileCommentController(factory, CreateMapper(), new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<UploadFile> SeedFileAsync(
        IDbContextFactory<BenDataContext> factory, Guid ownerId, bool isPublic = false,
        bool allowPublicComments = false)
    {
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "f.jpg", StoredFileName = "f-stored.jpg", ContentType = "image/jpeg",
            FileSize = 1, IsPublic = isPublic, AllowPublicComments = allowPublicComments,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        };
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(file);
        await db.SaveChangesAsync();
        return file;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Owner_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, ownerId);

        var result = await ctrl.Create(file.Id, new CreateFileCommentRequest("Nice find"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rec = Assert.IsType<UploadFileCommentRecord>(ok.Value);
        Assert.True(rec.IsOwner);
    }

    [Fact]
    public async Task Create_PublicAudienceMatchButToggleOff_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId, isPublic: true, allowPublicComments: false);
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.Create(file.Id, new CreateFileCommentRequest("Hi"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Create_PublicAudienceMatchAndToggleOn_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId, isPublic: true, allowPublicComments: true);
        var commenterId = Guid.NewGuid();
        var ctrl = BuildController(factory, commenterId);

        var result = await ctrl.Create(file.Id, new CreateFileCommentRequest("Hi"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rec = Assert.IsType<UploadFileCommentRecord>(ok.Value);
        Assert.False(rec.IsOwner);
        Assert.True(rec.IsPublicCommenter);
        Assert.Equal(commenterId, rec.AuthorAppUserId);
    }

    [Fact]
    public async Task Create_NoAudienceMatchAtAll_ReturnsNotFound()
    {
        // A private, unshared file — the caller can't even see it, so this 404s rather than 403
        // (matches CanViewFileAsync's "don't confirm existence" convention).
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId, isPublic: false);
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.Create(file.Id, new CreateFileCommentRequest("Hi"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_EmptyText_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, ownerId);

        var result = await ctrl.Create(file.Id, new CreateFileCommentRequest("   "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Author_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, ownerId);
        var created = (UploadFileCommentRecord)((OkObjectResult)(await ctrl.Create(file.Id, new CreateFileCommentRequest("v1"), default)).Result!).Value!;

        var result = await ctrl.Update(file.Id, created.Id, new UpdateFileCommentRequest("v2"), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("v2", ((UploadFileCommentRecord)ok.Value!).Text);
    }

    [Fact]
    public async Task Update_NonAuthor_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, ownerId);
        var created = (UploadFileCommentRecord)((OkObjectResult)(await ctrl.Create(file.Id, new CreateFileCommentRequest("v1"), default)).Result!).Value!;

        var otherCtrl = BuildController(factory, Guid.NewGuid());
        var result = await otherCtrl.Update(file.Id, created.Id, new UpdateFileCommentRequest("v2"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Author_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, ownerId);
        var created = (UploadFileCommentRecord)((OkObjectResult)(await ctrl.Create(file.Id, new CreateFileCommentRequest("v1"), default)).Result!).Value!;

        var result = await ctrl.Delete(file.Id, created.Id, default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_FileOwner_CanDeleteAnyonesComment()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId, isPublic: true, allowPublicComments: true);
        var commenterId = Guid.NewGuid();
        var commenterCtrl = BuildController(factory, commenterId);
        var created = (UploadFileCommentRecord)((OkObjectResult)(await commenterCtrl.Create(file.Id, new CreateFileCommentRequest("spam"), default)).Result!).Value!;

        var ownerCtrl = BuildController(factory, ownerId);
        var result = await ownerCtrl.Delete(file.Id, created.Id, default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_NeitherAuthorNorOwner_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId, isPublic: true, allowPublicComments: true);
        var commenterId = Guid.NewGuid();
        var commenterCtrl = BuildController(factory, commenterId);
        var created = (UploadFileCommentRecord)((OkObjectResult)(await commenterCtrl.Create(file.Id, new CreateFileCommentRequest("hi"), default)).Result!).Value!;

        var strangerCtrl = BuildController(factory, Guid.NewGuid());
        var result = await strangerCtrl.Delete(file.Id, created.Id, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSettings_Owner_Succeeds()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, ownerId);

        var result = await ctrl.UpdateSettings(file.Id, new FileCommentSettingsRecord(true, false, true, false), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var reloaded = await db.UploadFiles.FirstAsync(f => f.Id == file.Id);
        Assert.True(reloaded.AllowInvestigationTeamComments);
        Assert.True(reloaded.AllowOrganizationComments);
        Assert.False(reloaded.AllowClientComments);
    }

    [Fact]
    public async Task UpdateSettings_NonOwner_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = await SeedFileAsync(factory, ownerId);
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.UpdateSettings(file.Id, new FileCommentSettingsRecord(true, true, true, true), default);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
