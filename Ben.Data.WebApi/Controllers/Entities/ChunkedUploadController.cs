using System.Collections.Concurrent;
using System.Text.Json;
using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Helpers;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Uploads a large file in pieces, so no single request has to carry the whole thing.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The site is served through Cloudflare, which refuses any request
/// body over 100 MB — a hard ceiling on the classic multipart upload, met by evidence recordings
/// long before they reach the configured whole-file limit. Chunking sends the same bytes as a
/// series of small PUTs, each comfortably under the ceiling; the limits themselves are site
/// settings (<see cref="SiteSettingKeys.UploadMaxFileBytes"/>,
/// <see cref="SiteSettingKeys.UploadChunkMaxBytes"/>) read through
/// <see cref="UploadLimitsReader"/>, not numbers baked in here.</para>
///
/// <para><b>Where a session lives.</b> On the file storage, not in the database: chunks as
/// <c>chunk-sessions/{owner}/{session}.{index}.chunk</c> and a JSON manifest beside them. Session
/// state is short-lived bookkeeping about bytes that are themselves on that storage — keeping the
/// two together means no schema migration, and a session survives an app restart for free because
/// the disk is the state.</para>
///
/// <para><b>The size declared up front is a promise the server holds the client to.</b> Start
/// refuses a declared size over the limit before any byte is sent; each chunk is counted as it is
/// written and refused where the running total would exceed the declaration; Complete refuses an
/// assembly whose bytes do not add up to exactly the declared size. A client cannot declare small
/// and send big.</para>
///
/// <para><b>Ownership is the caller's token.</b> A session belongs to whoever started it; every
/// other endpoint answers 404 — not 403 — to anyone else, so a guessed session id confirms
/// nothing. The classic upload's SuperAdmin on-behalf-of feature is deliberately absent: nothing
/// that needed it uploads gigabytes.</para>
/// </remarks>
[ApiController]
[Route("api/chunked-uploads")]
[Authorize]
public sealed class ChunkedUploadController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;
    private readonly FileMetadataExtractorService _metadataExtractor;
    private readonly ILogger<ChunkedUploadController> _logger;

    /// <summary>Highest chunk index one session may use — 10,000 × 64 MiB ≈ 640 GiB, far past any
    /// configurable file limit, so the bound never bites a legitimate upload.</summary>
    private const int MaxChunkIndex = 9_999;

    /// <summary>A session untouched this long is abandoned and swept on the owner's next start.</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Serialises manifest read-modify-write per session. Chunk bodies land in distinct files and
    /// need no lock; the manifest that records them is one JSON document and does. Entries are
    /// removed when the session ends, so this stays as small as the number of in-flight uploads.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();

    public ChunkedUploadController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IFileStorageService fileStorage,
        IAuditLogService auditLog,
        FileMetadataExtractorService metadataExtractor,
        ILogger<ChunkedUploadController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    // ── The session manifest ──────────────────────────────────────────────────

    /// <summary>What the storage remembers about one in-flight upload.</summary>
    private sealed class SessionManifest
    {
        public Guid OwnerId { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
        public long TotalBytes { get; set; }
        public Guid UploadFileTypeId { get; set; }
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public DateTime CreatedUtc { get; set; }
        /// <summary>Chunk index → byte count, exactly as received.</summary>
        public Dictionary<int, long> Chunks { get; set; } = [];

        public long BytesReceived => Chunks.Values.Sum();
    }

    private static string SessionDir(Guid ownerId) => $"chunk-sessions/{ownerId:N}";
    private static string ManifestPath(Guid ownerId, Guid sessionId) => $"{SessionDir(ownerId)}/{sessionId:N}.json";
    private static string ChunkPath(Guid ownerId, Guid sessionId, int index) => $"{SessionDir(ownerId)}/{sessionId:N}.{index:D4}.chunk";

    private async Task<SessionManifest?> ReadManifestAsync(Guid ownerId, Guid sessionId, CancellationToken ct)
    {
        var path = ManifestPath(ownerId, sessionId);
        if (!_fileStorage.Exists(path)) return null;
        await using var stream = await _fileStorage.OpenReadAsync(path, ct);
        return await JsonSerializer.DeserializeAsync<SessionManifest>(stream, cancellationToken: ct);
    }

    private async Task WriteManifestAsync(Guid sessionId, SessionManifest manifest, CancellationToken ct)
    {
        using var buffer = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest));
        await _fileStorage.WriteAsync(ManifestPath(manifest.OwnerId, sessionId), buffer, ct);
    }

    private static ChunkedUploadSessionRecord ToRecord(Guid sessionId, SessionManifest m, UploadLimits limits)
        => new(sessionId, m.TotalBytes, m.BytesReceived, limits.ChunkMaxBytes, limits.MaxFileBytes,
               m.Chunks.Keys.OrderBy(i => i).ToList());

    // ── Start ─────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<ChunkedUploadSessionRecord>> Start(
        [FromBody] StartChunkedUploadRequest request, CancellationToken ct)
    {
        var callerId = GetCurrentUserId();
        if (callerId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest("A file name is required.");
        if (request.TotalBytes <= 0)
            return BadRequest("The file size must be greater than zero.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var limits = await UploadLimitsReader.ReadAsync(db, ct);

        if (request.TotalBytes > limits.MaxFileBytes)
            return BadRequest($"That file is {request.TotalBytes:N0} bytes; the largest allowed upload is {limits.MaxFileBytes:N0} bytes.");

        // Same policy as the classic upload: the extension must satisfy the chosen type.
        var fileType = await db.UploadFileTypes
            .Include(t => t.AllowedExtensions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.UploadFileTypeId, ct);
        if (fileType is null)
            return BadRequest("Upload file type not found.");

        var ext = Path.GetExtension(request.FileName);
        if (!fileType.AllowAllExtensions)
        {
            var patterns = fileType.AllowedExtensions.Select(e => e.Pattern);
            if (!FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, ext))
                return BadRequest($"File extension '{ext}' is not permitted for file type '{fileType.Name}'.");
        }

        // SVGs are sanitised by parsing and rewriting the whole document, which has no streaming
        // shape — and they are small text files with no business being chunked. The classic
        // upload handles them.
        var isSvg = ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                 || (request.ContentType?.Contains("svg", StringComparison.OrdinalIgnoreCase) ?? false);
        if (isSvg)
            return BadRequest("SVG files are sanitised as a whole document — upload them through the regular upload, not in chunks.");

        // Housekeeping on the way in: sessions this owner started and walked away from.
        await SweepAbandonedSessionsAsync(callerId, ct);

        var sessionId = Guid.NewGuid();
        var manifest = new SessionManifest
        {
            OwnerId = callerId,
            FileName = request.FileName,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream" : request.ContentType,
            TotalBytes = request.TotalBytes,
            UploadFileTypeId = request.UploadFileTypeId,
            Description = request.Description,
            IsPublic = request.IsPublic,
            CreatedUtc = DateTime.UtcNow,
        };
        await WriteManifestAsync(sessionId, manifest, ct);

        return Ok(ToRecord(sessionId, manifest, limits));
    }

    // ── Chunks ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Receives one chunk as a raw request body. Idempotent per index: re-sending a chunk
    /// overwrites the same file and updates the same manifest entry, so a client retrying a
    /// timed-out PUT cannot corrupt anything.
    /// </summary>
    // The framework's request-size ceiling is disabled because the real ceiling is the
    // configurable chunk limit, enforced while counting the bytes as they stream to storage.
    [HttpPut("{sessionId:guid}/chunks/{index:int}")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<ChunkedUploadSessionRecord>> PutChunk(
        Guid sessionId, int index, CancellationToken ct)
    {
        var callerId = GetCurrentUserId();
        if (callerId == Guid.Empty) return Unauthorized();
        if (index is < 0 or > MaxChunkIndex) return BadRequest("Chunk index out of range.");

        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var manifest = await ReadManifestAsync(callerId, sessionId, ct);
            if (manifest is null) return NotFound();

            await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
            var limits = await UploadLimitsReader.ReadAsync(db, ct);

            // Copy to storage while counting; a chunk that runs past the ceiling is cut off
            // exactly there rather than read to the end first.
            var chunkPath = ChunkPath(callerId, sessionId, index);
            long written;
            try
            {
                var capped = new CappedReadStream(Request.Body, limits.ChunkMaxBytes);
                await _fileStorage.WriteAsync(chunkPath, capped, ct);
                written = capped.BytesRead;
            }
            catch (CappedReadStream.CapExceededException)
            {
                await _fileStorage.DeleteAsync(chunkPath, ct);
                return BadRequest($"A chunk may be at most {limits.ChunkMaxBytes:N0} bytes.");
            }

            if (written == 0)
            {
                await _fileStorage.DeleteAsync(chunkPath, ct);
                return BadRequest("The chunk was empty.");
            }

            // The declared size is a promise: chunks that would exceed it are refused.
            var othersTotal = manifest.Chunks.Where(kv => kv.Key != index).Sum(kv => kv.Value);
            if (othersTotal + written > manifest.TotalBytes)
            {
                await _fileStorage.DeleteAsync(chunkPath, ct);
                return BadRequest("The chunks add up to more than the declared file size.");
            }

            manifest.Chunks[index] = written;
            await WriteManifestAsync(sessionId, manifest, ct);

            return Ok(ToRecord(sessionId, manifest, limits));
        }
        finally
        {
            gate.Release();
        }
    }

    // ── Status (resume) ───────────────────────────────────────────────────────

    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<ChunkedUploadSessionRecord>> GetStatus(Guid sessionId, CancellationToken ct)
    {
        var callerId = GetCurrentUserId();
        if (callerId == Guid.Empty) return Unauthorized();

        var manifest = await ReadManifestAsync(callerId, sessionId, ct);
        if (manifest is null) return NotFound();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var limits = await UploadLimitsReader.ReadAsync(db, ct);
        return Ok(ToRecord(sessionId, manifest, limits));
    }

    // ── Complete ──────────────────────────────────────────────────────────────

    [HttpPost("{sessionId:guid}/complete")]
    public async Task<ActionResult<UploadFileRecord>> Complete(Guid sessionId, CancellationToken ct)
    {
        var callerId = GetCurrentUserId();
        if (callerId == Guid.Empty) return Unauthorized();

        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var manifest = await ReadManifestAsync(callerId, sessionId, ct);
            if (manifest is null) return NotFound();

            // Contiguity and the exact declared size, or no file at all. 409 rather than 400:
            // the request is well-formed, the session just isn't in a completable state yet.
            var indexes = manifest.Chunks.Keys.OrderBy(i => i).ToList();
            var gaps = Enumerable.Range(0, indexes.Count).Where(i => !manifest.Chunks.ContainsKey(i)).ToList();
            if (gaps.Count > 0)
                return Conflict($"Chunks are missing: {string.Join(", ", gaps.Take(10))}.");
            if (manifest.BytesReceived != manifest.TotalBytes)
                return Conflict($"Received {manifest.BytesReceived:N0} bytes of the declared {manifest.TotalBytes:N0}.");

            var entity = new UploadFile
            {
                Id = Guid.NewGuid(),
                UploadFileTypeId = manifest.UploadFileTypeId,
                AppUserId = callerId,
                FileName = manifest.FileName,
                StoredFileName = $"{Guid.NewGuid()}{Path.GetExtension(manifest.FileName)}",
                ContentType = manifest.ContentType,
                FileSize = manifest.TotalBytes,
                FileData = null,
                Description = manifest.Description,
                IsPublic = manifest.IsPublic,
                SortOrder = 0,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = callerId,
            };

            // Assemble to the final path first; the DB row is only committed once the bytes are
            // in place — the same write-then-record order the classic upload keeps.
            var relativePath = _fileStorage.UserFilePath(callerId, entity.StoredFileName);
            var sources = indexes
                .Select(i => ChunkPath(callerId, sessionId, i))
                .Select(path => (Func<CancellationToken, Task<Stream>>)(token => _fileStorage.OpenReadAsync(path, token)))
                .ToList();
            await using (var concat = new ConcatenatingReadStream(sources))
                await _fileStorage.WriteAsync(relativePath, concat, ct);
            entity.StoragePath = relativePath;

            await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
            db.UploadFiles.Add(entity);
            await db.SaveChangesAsync(ct);
            _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, callerId, AppSources.WebApi));

            // The session's pieces have served their purpose.
            foreach (var i in indexes)
                await _fileStorage.DeleteAsync(ChunkPath(callerId, sessionId, i), ct);
            await _fileStorage.DeleteAsync(ManifestPath(callerId, sessionId), ct);
            _sessionLocks.TryRemove(sessionId, out _);

            // Metadata extraction, fire-and-forget — identical reasoning to the classic upload:
            // read back off storage so the request never holds the file.
            var metadataFileId = entity.Id;
            var metadataPath = relativePath;
            var contentType = entity.ContentType;
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stored = await _fileStorage.OpenReadAsync(metadataPath, CancellationToken.None);
                    var meta = _metadataExtractor.Extract(metadataFileId, contentType, stored);
                    await using var dbMeta = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                    dbMeta.UploadFileMetadata.Add(meta);
                    await dbMeta.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Metadata extraction failed for chunked upload {UploadFileId}", metadataFileId);
                }
            });

            var record = _mapper.Map<UploadFileRecord>(entity);
            return Created($"/api/upload-files/{entity.Id}", record);
        }
        finally
        {
            gate.Release();
        }
    }

    // ── Abort ─────────────────────────────────────────────────────────────────

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Abort(Guid sessionId, CancellationToken ct)
    {
        var callerId = GetCurrentUserId();
        if (callerId == Guid.Empty) return Unauthorized();

        var manifest = await ReadManifestAsync(callerId, sessionId, ct);
        if (manifest is null) return NotFound();

        foreach (var i in manifest.Chunks.Keys)
            await _fileStorage.DeleteAsync(ChunkPath(callerId, sessionId, i), ct);
        await _fileStorage.DeleteAsync(ManifestPath(callerId, sessionId), ct);
        _sessionLocks.TryRemove(sessionId, out _);

        return NoContent();
    }

    // ── Housekeeping ──────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes this owner's sessions older than <see cref="SessionLifetime"/>, manifests and
    /// chunks alike. Runs on session start — the one moment an abandoning uploader is guaranteed
    /// to come back through — so nothing needs a scheduler. Orphaned chunk files whose manifest is
    /// gone are removed by name shape.
    /// </summary>
    private async Task SweepAbandonedSessionsAsync(Guid ownerId, CancellationToken ct)
    {
        try
        {
            var files = _fileStorage.ListFiles(SessionDir(ownerId));
            var cutoff = DateTime.UtcNow - SessionLifetime;

            // First pass: expire manifests, remembering which sessions remain live.
            var liveSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                SessionManifest? manifest = null;
                try
                {
                    await using var stream = await _fileStorage.OpenReadAsync(path, ct);
                    manifest = await JsonSerializer.DeserializeAsync<SessionManifest>(stream, cancellationToken: ct);
                }
                catch (Exception) { /* unreadable manifest — treated as expired below */ }

                if (manifest is not null && manifest.CreatedUtc >= cutoff)
                {
                    liveSessions.Add(name);
                    continue;
                }
                await _fileStorage.DeleteAsync(path, ct);
            }

            // Second pass: chunks whose session is not live — expired above, or orphaned earlier.
            foreach (var path in files.Where(f => f.EndsWith(".chunk", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(path);
                var sessionPart = name.Split('.')[0];
                if (!liveSessions.Contains(sessionPart))
                    await _fileStorage.DeleteAsync(path, ct);
            }
        }
        catch (Exception ex)
        {
            // Housekeeping must never block an upload.
            _logger.LogWarning(ex, "Chunk-session sweep failed for owner {OwnerId}", ownerId);
        }
    }

    // ── Counting cap ──────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a source stream, counting bytes and throwing once a cap is passed — so an oversize
    /// chunk is stopped at the cap plus one buffer, not buffered to the end and measured after.
    /// </summary>
    private sealed class CappedReadStream : Stream
    {
        public sealed class CapExceededException : IOException;

        private readonly Stream _source;
        private readonly long _cap;
        public long BytesRead { get; private set; }

        public CappedReadStream(Stream source, long cap)
        {
            _source = source;
            _cap = cap;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await _source.ReadAsync(buffer, ct);
            BytesRead += read;
            if (BytesRead > _cap) throw new CapExceededException();
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
