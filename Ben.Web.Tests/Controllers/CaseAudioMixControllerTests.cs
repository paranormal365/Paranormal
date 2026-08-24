using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
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

/// <summary>Tests for <see cref="CaseAudioMixController"/> — the Phase E mixer export endpoint.</summary>
public class CaseAudioMixControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static byte[] CreateSineWav(double frequencyHz, double seconds, int sampleRate = 8000)
    {
        var numSamples = (int)(sampleRate * seconds);
        var dataSize = numSamples * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataSize);
        for (var i = 0; i < numSamples; i++)
            w.Write((short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate)));
        return ms.ToArray();
    }

    private static CaseAudioMixController BuildController(
        IDbContextFactory<BenDataContext> factory, Guid userId, Dictionary<string, byte[]> store, Action<byte[]>? onExportWritten = null,
        bool isSuperAdmin = false)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("export/mix.wav");
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns<string, CancellationToken>((path, _) => Task.FromResult<Stream>(new MemoryStream(store[path])));
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns<string, Stream, CancellationToken>((_, stream, _) =>
               {
                   using var ms = new MemoryStream();
                   stream.CopyTo(ms);
                   onExportWritten?.Invoke(ms.ToArray());
                   return Task.CompletedTask;
               });

        var ctrl = new CaseAudioMixController(factory, storage.Object, Ben.Web.Tests.TestMedia.Ingest(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    isSuperAdmin
                        ? [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                           new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin)]
                        : [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId, Guid UserId)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Manager, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Test Case",
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return (factory, orgId, caseId, userId);
    }

    private static async Task<CaseFile> SeedCaseFileAsync(
        IDbContextFactory<BenDataContext> factory, Guid caseId, Guid userId,
        Dictionary<string, byte[]> store, string storagePath, byte[] bytes, string contentType = "audio/wav")
    {
        store[storagePath] = bytes;
        await using var db = await factory.CreateDbContextAsync();
        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = userId,
            FileName = "clip.wav", StoredFileName = "clip.wav", ContentType = contentType,
            FileSize = bytes.Length, StoragePath = storagePath,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFile.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);
        await db.SaveChangesAsync();
        caseFile.UploadFile = uploadFile;
        return caseFile;
    }

    private static ExportAudioMixRequest RequestFor(params (Guid CaseFileId, bool Muted, bool Solo)[] tracks)
        => new(tracks.Select(t => new MixTrackExportInput(t.CaseFileId, 0, 0, 0, t.Muted, t.Solo)).ToList());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_NoTracks_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, []);

        var result = await ctrl.Export(orgId, caseId, new ExportAudioMixRequest([]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_SuperAdminNonMember_IsNotForbidden()
    {
        // 2026-08-22: the SuperAdmin could open a case's audio mixer but every fetch under it
        // 403'd — IsOrgMember lacked the bypass the case endpoint itself already had.
        var (factory, orgId, caseId, _) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, Guid.NewGuid(), store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, Guid.NewGuid(), store, isSuperAdmin: true);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsNotType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Export_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, Guid.NewGuid(), store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, Guid.NewGuid(), store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Export_CaseNotFound_ReturnsNotFound()
    {
        var (factory, orgId, _, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, []);

        var result = await ctrl.Export(orgId, Guid.NewGuid(), RequestFor((Guid.NewGuid(), false, false)), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Export_UnknownCaseFileId_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, []);

        var result = await ctrl.Export(orgId, caseId, RequestFor((Guid.NewGuid(), false, false)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_NonAudioFile_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.jpg", [1, 2, 3], "image/jpeg");
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_AllTracksMuted_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, true, false)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_SoloedTrack_ExcludesNonSoloedTracks()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var soloed = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        var other = await SeedCaseFileAsync(factory, caseId, userId, store, "b.wav", CreateSineWav(880, 1));
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId,
            RequestFor((soloed.Id, false, true), (other.Id, false, false)), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<CaseFileRecord>(ok.Value);
    }

    [Fact]
    public async Task Export_Success_CreatesNewUploadFileAndCaseFile()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        byte[]? exported = null;
        var ctrl = BuildController(factory, userId, store, bytes => exported = bytes);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<CaseFileRecord>(ok.Value);
        Assert.Equal(caseId, record.CaseId);
        Assert.Equal("audio/wav", record.ContentType);
        Assert.NotNull(exported);
        Assert.True(exported!.Length > 0);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == record.UploadFileId));
        Assert.True(await db.CaseFiles.AnyAsync(f => f.Id == record.Id && f.CaseId == caseId));
    }
}
