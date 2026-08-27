using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The chunked upload lifecycle (Cloudflare's 100 MB ceiling is why it exists): a session is
/// opened with the file's facts, chunks arrive as raw PUTs in any order, and Complete assembles
/// exactly the declared bytes into an ordinary <see cref="UploadFile"/> — or refuses.
/// </summary>
/// <remarks>
/// Storage is a real in-memory implementation rather than a Moq stub because the controller's
/// correctness IS its storage behaviour: bytes concatenated in index order, chunk files and the
/// manifest deleted afterwards. A mock that ignores writes would pass a controller that
/// assembles garbage.
/// </remarks>
public class ChunkedUploadControllerTests
{
    // ── An in-memory IFileStorageService with real semantics ─────────────────

    private sealed class InMemoryFileStorage : IFileStorageService
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);

        public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, ct);
            Files[relativePath] = buffer.ToArray();
        }

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => Files.TryGetValue(relativePath, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new FileNotFoundException(relativePath);

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            Files.TryRemove(relativePath, out _);
            return Task.CompletedTask;
        }

        public bool Exists(string relativePath) => Files.ContainsKey(relativePath);

        public IReadOnlyList<string> ListFiles(string relativeDirectory)
        {
            var prefix = relativeDirectory.TrimEnd('/') + "/";
            return Files.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && !k[prefix.Length..].Contains('/'))
                .ToList();
        }

        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly Guid Caller = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = new("22222222-2222-2222-2222-222222222222");

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> SeedFileTypeAsync(
        IDbContextFactory<BenDataContext> factory, bool allowAll = true, params string[] patterns)
    {
        var typeId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = typeId, Name = "Test Type", AllowAllExtensions = allowAll,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Caller,
        });
        foreach (var pattern in patterns)
            db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
            {
                Id = Guid.NewGuid(), UploadFileTypeId = typeId, Pattern = pattern,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Caller,
            });
        await db.SaveChangesAsync();
        return typeId;
    }

    private static async Task SetSettingAsync(IDbContextFactory<BenDataContext> factory, string key, string value)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = key, Value = value,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Caller,
        });
        await db.SaveChangesAsync();
    }

    private static ChunkedUploadController Build(
        IDbContextFactory<BenDataContext> factory, InMemoryFileStorage storage, Guid? userId = null)
    {
        var mapperMock = new Mock<IMapper>();
        mapperMock.Setup(m => m.Map<UploadFileRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is UploadFile f
                ? new UploadFileRecord
                {
                    Id = f.Id, AppUserId = f.AppUserId, FileName = f.FileName,
                    StoredFileName = f.StoredFileName, ContentType = f.ContentType,
                    FileSize = f.FileSize, StoragePath = f.StoragePath,
                }
                : new UploadFileRecord { FileName = "?", StoredFileName = "?", ContentType = "?" });

        var ctrl = new ChunkedUploadController(factory, mapperMock.Object, storage,
            new Mock<IAuditLogService>().Object,
            new FileMetadataExtractorService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChunkedUploadController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, (userId ?? Caller).ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    /// <summary>Each chunk PUT is its own request; this points Request.Body at the chunk's bytes.</summary>
    private static void SetBody(ChunkedUploadController ctrl, byte[] bytes)
        => ctrl.ControllerContext.HttpContext.Request.Body = new MemoryStream(bytes);

    private static ChunkedUploadSessionRecord SessionOf(ActionResult<ChunkedUploadSessionRecord> result)
        => Assert.IsType<ChunkedUploadSessionRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static StartChunkedUploadRequest Request(Guid typeId, string name, long total)
        => new(name, "application/octet-stream", total, typeId, null, false);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lifecycle_ChunksInAnyOrder_AssembleToTheDeclaredBytes()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        var session = SessionOf(await ctrl.Start(Request(typeId, "recording.mp4", 8), default));

        // Out of order on purpose — the wire makes no ordering promise.
        SetBody(ctrl, "BBB"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 1, default));
        SetBody(ctrl, "AAA"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));
        SetBody(ctrl, "CC"u8.ToArray());
        var afterLast = SessionOf(await ctrl.PutChunk(session.Id, 2, default));

        Assert.Equal(8, afterLast.BytesReceived);
        Assert.Equal([0, 1, 2], afterLast.ReceivedChunks);

        var completed = await ctrl.Complete(session.Id, default);
        var record = Assert.IsType<UploadFileRecord>(Assert.IsType<CreatedResult>(completed.Result).Value);

        // The assembled file holds the chunks in INDEX order, not arrival order.
        Assert.Equal("AAABBBCC", Encoding.UTF8.GetString(storage.Files[record.StoragePath!]));

        // The row is an ordinary UploadFile, owned by the caller.
        await using var db = await factory.CreateDbContextAsync();
        var entity = await db.UploadFiles.SingleAsync();
        Assert.Equal(Caller, entity.AppUserId);
        Assert.Equal(8, entity.FileSize);
        Assert.Equal("recording.mp4", entity.FileName);

        // The session's pieces are gone: only the assembled file remains in storage.
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task Chunk_Resent_OverwritesInsteadOfCorrupting()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 4), default));

        // A retry after a timeout re-sends the same chunk; the second write must win cleanly.
        SetBody(ctrl, "XX"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));
        SetBody(ctrl, "AB"u8.ToArray());
        var after = SessionOf(await ctrl.PutChunk(session.Id, 0, default));
        Assert.Equal(2, after.BytesReceived);

        SetBody(ctrl, "CD"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 1, default));

        var completed = await ctrl.Complete(session.Id, default);
        var record = Assert.IsType<UploadFileRecord>(Assert.IsType<CreatedResult>(completed.Result).Value);
        Assert.Equal("ABCD", Encoding.UTF8.GetString(storage.Files[record.StoragePath!]));
    }

    [Fact]
    public async Task Status_ReportsWhatArrived_SoAClientCanResume()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 6), default));
        SetBody(ctrl, "AA"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));
        SetBody(ctrl, "CC"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 2, default));

        var status = SessionOf(await ctrl.GetStatus(session.Id, default));
        Assert.Equal([0, 2], status.ReceivedChunks);
        Assert.Equal(4, status.BytesReceived);
        Assert.Equal(6, status.TotalBytes);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_RefusesAFileOverTheConfiguredLimit()
    {
        var factory = CreateFactory();
        var typeId  = await SeedFileTypeAsync(factory);
        await SetSettingAsync(factory, SiteSettingKeys.UploadMaxFileBytes, "100");
        var ctrl = Build(factory, new InMemoryFileStorage());

        var result = await ctrl.Start(Request(typeId, "big.mp4", 101), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("largest allowed upload is 100", refusal.Value!.ToString());
    }

    [Fact]
    public async Task Chunk_OverTheConfiguredChunkLimit_IsRefusedAndDiscarded()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        await SetSettingAsync(factory, SiteSettingKeys.UploadChunkMaxBytes, "4");
        var ctrl = Build(factory, storage);

        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 100), default));
        SetBody(ctrl, "TOOBIG"u8.ToArray());
        var result = await ctrl.PutChunk(session.Id, 0, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        // The oversize chunk left nothing behind — only the session manifest is in storage.
        Assert.DoesNotContain(storage.Files.Keys, k => k.EndsWith(".chunk"));
        Assert.Equal(0, SessionOf(await ctrl.GetStatus(session.Id, default)).BytesReceived);
    }

    [Fact]
    public async Task Chunks_BeyondTheDeclaredTotal_AreRefused()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        // Declared 4 bytes; the second chunk would take the total to 6. The declaration wins.
        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 4), default));
        SetBody(ctrl, "AAA"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));
        SetBody(ctrl, "BBB"u8.ToArray());
        var result = await ctrl.PutChunk(session.Id, 1, default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("more than the declared", refusal.Value!.ToString());
    }

    [Fact]
    public async Task Complete_WithAGap_ConflictsAndNamesTheMissingChunk()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 6), default));
        SetBody(ctrl, "AA"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));
        SetBody(ctrl, "CC"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 2, default));

        var result = await ctrl.Complete(session.Id, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("1", conflict.Value!.ToString());

        // Nothing was assembled and no row was created.
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.UploadFiles);
    }

    [Fact]
    public async Task Complete_ShortOfTheDeclaredBytes_Conflicts()
    {
        var factory = CreateFactory();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, new InMemoryFileStorage());

        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 10), default));
        SetBody(ctrl, "ABC"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));

        var result = await ctrl.Complete(session.Id, default);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("3", conflict.Value!.ToString());
        Assert.Contains("10", conflict.Value!.ToString());
    }

    [Fact]
    public async Task Start_RefusesSvg_WhichOnlyTheClassicPathSanitises()
    {
        var factory = CreateFactory();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, new InMemoryFileStorage());

        var result = await ctrl.Start(Request(typeId, "logo.svg", 500), default);
        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("regular upload", refusal.Value!.ToString());
    }

    [Fact]
    public async Task Start_EnforcesTheFileTypeExtensionPolicy()
    {
        var factory = CreateFactory();
        var typeId  = await SeedFileTypeAsync(factory, allowAll: false, ".mp4");
        var ctrl    = Build(factory, new InMemoryFileStorage());

        var refused = await ctrl.Start(Request(typeId, "malware.exe", 10), default);
        Assert.IsType<BadRequestObjectResult>(refused.Result);

        var allowed = await ctrl.Start(Request(typeId, "evidence.mp4", 10), default);
        Assert.IsType<OkObjectResult>(allowed.Result);
    }

    // ── Ownership ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnotherUsersSession_Answers404_NeverConfirmingItExists()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);

        var owner = Build(factory, storage);
        var session = SessionOf(await owner.Start(Request(typeId, "a.bin", 4), default));

        var stranger = Build(factory, storage, Stranger);
        SetBody(stranger, "AB"u8.ToArray());
        Assert.IsType<NotFoundResult>((await stranger.PutChunk(session.Id, 0, default)).Result);
        Assert.IsType<NotFoundResult>((await stranger.GetStatus(session.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await stranger.Complete(session.Id, default)).Result);
        Assert.IsType<NotFoundResult>(await stranger.Abort(session.Id, default));
    }

    // ── Abort and housekeeping ───────────────────────────────────────────────

    [Fact]
    public async Task Abort_RemovesEveryTraceOfTheSession()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        var session = SessionOf(await ctrl.Start(Request(typeId, "a.bin", 4), default));
        SetBody(ctrl, "AB"u8.ToArray());
        SessionOf(await ctrl.PutChunk(session.Id, 0, default));

        Assert.IsType<NoContentResult>(await ctrl.Abort(session.Id, default));
        Assert.Empty(storage.Files);
        Assert.IsType<NotFoundResult>((await ctrl.GetStatus(session.Id, default)).Result);
    }

    [Fact]
    public async Task Start_SweepsSessionsAbandonedForADay_AndLeavesLiveOnesAlone()
    {
        var factory = CreateFactory();
        var storage = new InMemoryFileStorage();
        var typeId  = await SeedFileTypeAsync(factory);
        var ctrl    = Build(factory, storage);

        // A session with a chunk, then its manifest backdated past the 24-hour lifetime.
        var stale = SessionOf(await ctrl.Start(Request(typeId, "old.bin", 4), default));
        SetBody(ctrl, "AB"u8.ToArray());
        SessionOf(await ctrl.PutChunk(stale.Id, 0, default));
        var manifestPath = storage.Files.Keys.Single(k => k.EndsWith($"{stale.Id:N}.json"));
        var manifest = JsonNode.Parse(Encoding.UTF8.GetString(storage.Files[manifestPath]))!;
        manifest["CreatedUtc"] = DateTime.UtcNow.AddHours(-25);
        storage.Files[manifestPath] = Encoding.UTF8.GetBytes(manifest.ToJsonString());

        // A fresh session survives the sweep the next Start performs.
        var live = SessionOf(await ctrl.Start(Request(typeId, "live.bin", 4), default));
        SessionOf(await ctrl.Start(Request(typeId, "trigger.bin", 4), default));

        Assert.IsType<NotFoundResult>((await ctrl.GetStatus(stale.Id, default)).Result);
        Assert.DoesNotContain(storage.Files.Keys, k => k.Contains(stale.Id.ToString("N")));
        Assert.IsType<OkObjectResult>((await ctrl.GetStatus(live.Id, default)).Result);
    }
}
