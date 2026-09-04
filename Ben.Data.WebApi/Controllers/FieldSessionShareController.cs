using System.Security.Cryptography;
using System.Text;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Sharing one field session, or one recording out of it, with somebody who has no account
/// (item 207).
/// </summary>
/// <remarks>
/// <para><b>The problem this solves.</b> A client wants to see what was recorded in their house.
/// A producer wants to decide whether a night is worth a crew. Neither will make an account to
/// look at one thing once, so what actually happens today is that somebody emails the files — and
/// at that moment the group has lost every control it had over them. There is no expiry on an
/// attachment, no way to take it back, and no way to know whether it was ever opened. A link that
/// does all three is a straight improvement on the thing people already do.</para>
///
/// <para><b>Read-only, and structurally so.</b> There is nothing on the anonymous side but two
/// GETs. No comment, no download of the whole night as a bundle, no navigation to anything else —
/// the shared page is the end of the road, not a doorway into the site.</para>
///
/// <para><b>The link names a row, and the row holds every rule.</b> Per item 201, nothing
/// token-sized travels in a URL: the token is twenty-two characters that mean nothing anywhere
/// except in this table. Expiry, revocation, which file, and whether coordinates travel are all
/// read from the row on every single request — so revoking is one column write and takes effect
/// on the next click, and a link that has been copied into three inboxes dies in all three at
/// once.</para>
///
/// <para><b>Who may create one.</b> The person who sent the session up, or an active member of the
/// organization running its investigation. Deliberately narrower than who may READ a session:
/// <c>MayContributeAsync</c> lets anybody at all read a public investigation's sessions, and
/// letting a passer-by mint a permanent-until-expiry link to somebody else's recordings would
/// hand out an authority the group never granted. Reading is not the same act as republishing.</para>
///
/// <para><b>Every open is logged.</b> The person who sent the link has to be able to answer "did
/// they look at it?", and a count alone cannot tell one person opening it eight times from eight
/// people opening it once. The viewer's address is hashed rather than stored — these are people
/// with no account who agreed to nothing.</para>
/// </remarks>
[ApiController]
public sealed class FieldSessionShareController : BenControllerBase
{
    /// <summary>How long a link may be made to last. A month is long enough for a producer to get
    /// round to it and short enough that a forgotten link stops mattering.</summary>
    private const int MaxShareDays = 30;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<FieldSessionShareController> _log;

    public FieldSessionShareController(
        IDbContextFactory<BenDataContext> db,
        IFileStorageService fileStorage,
        ILogger<FieldSessionShareController> log)
    {
        _db = db;
        _fileStorage = fileStorage;
        _log = log;
    }

    // ── The owner's side ──────────────────────────────────────────────────────

    /// <summary>Every link ever made for this session, live and dead, newest first.</summary>
    /// <remarks>
    /// Revoked and expired links stay in the list rather than disappearing. "Was this ever shared,
    /// and with whom, and when did that stop" is a question somebody eventually has to answer —
    /// most sharply when evidence turns up somewhere it should not have — and a list that quietly
    /// drops its dead rows cannot answer it.
    /// </remarks>
    [HttpGet("api/field-sessions/{sessionId:guid}/shares")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<FieldSessionShareRecord>>> GetShares(
        Guid sessionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();
        if (!await MayShareAsync(db, session, userId, ct)) return NotFound();

        var links = await db.FieldSessionShareLinks.AsNoTracking()
            .Where(l => l.FieldSessionUploadId == sessionId)
            .OrderByDescending(l => l.DateCreated)
            .ToListAsync(ct);

        return Ok(links.Select(l => ToRecord(l, DateTime.UtcNow)));
    }

    /// <param name="FileId">
    /// One recording, when the share is of a single piece of evidence rather than the whole night.
    /// </param>
    /// <param name="ExpiresInDays">1 to 30. Required, because a share with no end is a public URL.</param>
    /// <param name="Note">Who it is for, so a list of five links is five decisions rather than five identical rows.</param>
    /// <param name="IncludePositions">
    /// Whether the readings' GPS fixes travel. Defaults to false, and the default is the point: a
    /// fix taken indoors is somebody's street address, so forgetting to think about it withholds.
    /// </param>
    public sealed record CreateShareRequest(
        Guid? FileId, int ExpiresInDays, string? Note = null, bool IncludePositions = false);

    /// <summary>Makes a link.</summary>
    [HttpPost("api/field-sessions/{sessionId:guid}/shares")]
    [Authorize]
    public async Task<ActionResult<FieldSessionShareRecord>> CreateShare(
        Guid sessionId, [FromBody] CreateShareRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (request.ExpiresInDays is < 1 or > MaxShareDays)
            return BadRequest($"A link can last between 1 and {MaxShareDays} days.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();
        if (!await MayShareAsync(db, session, userId, ct)) return NotFound();

        if (request.FileId is Guid fileId)
        {
            // Checked against THIS session, not merely for existence: a file id from another
            // session would otherwise create a link that reads bytes its session never had.
            var belongs = await db.FieldSessionUploadFiles.AsNoTracking()
                .AnyAsync(f => f.Id == fileId && f.FieldSessionUploadId == sessionId, ct);
            if (!belongs) return NotFound();
        }

        var link = new FieldSessionShareLink
        {
            Token                    = NewToken(),
            FieldSessionUploadId     = sessionId,
            FieldSessionUploadFileId = request.FileId,
            Note                     = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ExpiresUtc               = DateTime.UtcNow.AddDays(request.ExpiresInDays),
            IncludePositions         = request.IncludePositions,
            DateCreated              = DateTime.UtcNow,
            CreatedByAppUserId       = userId,
        };

        db.FieldSessionShareLinks.Add(link);
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Field session {SessionId} shared by {UserId} until {Expires:o} (file {FileId}, positions {Positions})",
            sessionId, userId, link.ExpiresUtc, request.FileId, request.IncludePositions);

        return Ok(ToRecord(link, DateTime.UtcNow));
    }

    /// <summary>Pulls a link back. Takes effect on the next click, everywhere it was pasted.</summary>
    /// <remarks>
    /// The row is kept and stamped rather than deleted, for the reason <see cref="GetShares"/>
    /// gives. Revoking twice is not an error: somebody clicking the button again because they were
    /// not sure the first one worked should be reassured, not refused.
    /// </remarks>
    [HttpDelete("api/field-sessions/{sessionId:guid}/shares/{shareId:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeShare(Guid sessionId, Guid shareId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();
        if (!await MayShareAsync(db, session, userId, ct)) return NotFound();

        var link = await db.FieldSessionShareLinks
            .FirstOrDefaultAsync(l => l.Id == shareId && l.FieldSessionUploadId == sessionId, ct);
        if (link is null) return NotFound();

        if (link.RevokedUtc is null)
        {
            link.RevokedUtc         = DateTime.UtcNow;
            link.RevokedByAppUserId = userId;
            link.DateUpdated        = DateTime.UtcNow;
            link.UpdatedByAppUserId = userId;
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Share link {ShareId} on session {SessionId} revoked by {UserId}",
                shareId, sessionId, userId);
        }

        return NoContent();
    }

    /// <summary>Who opened it, newest first.</summary>
    [HttpGet("api/field-sessions/{sessionId:guid}/shares/{shareId:guid}/views")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<FieldSessionShareViewRecord>>> GetShareViews(
        Guid sessionId, Guid shareId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();
        if (!await MayShareAsync(db, session, userId, ct)) return NotFound();

        var owns = await db.FieldSessionShareLinks.AsNoTracking()
            .AnyAsync(l => l.Id == shareId && l.FieldSessionUploadId == sessionId, ct);
        if (!owns) return NotFound();

        var views = await db.FieldSessionShareLinkViews.AsNoTracking()
            .Where(v => v.FieldSessionShareLinkId == shareId)
            .OrderByDescending(v => v.ViewedUtc)
            .Take(200)
            .ToListAsync(ct);

        // ViewerHash is shortened to eight characters here and nowhere else. It exists so an owner
        // can see that three DIFFERENT people opened the link; the full digest would invite
        // somebody to try to reverse it, and eight characters answer the only question asked.
        return Ok(views.Select(v => new FieldSessionShareViewRecord(
            v.ViewedUtc,
            v.ViewerHash is { Length: >= 8 } h ? h[..8] : v.ViewerHash,
            v.UserAgent,
            v.FieldSessionUploadFileId)));
    }

    // ── The recipient's side: no account, no session, no bearer token ─────────

    /// <summary>
    /// The shared session and its document.
    /// </summary>
    /// <remarks>
    /// <para><b>404 for everything.</b> Unknown, expired, revoked and never-existed all answer the
    /// same way. A distinct "this link has expired" would confirm to somebody guessing tokens that
    /// they had found a real one, and the person holding a genuine expired link is told what
    /// happened by whoever sent it, not by a probe-friendly endpoint.</para>
    ///
    /// <para><b>The record is a separate shape.</b> <c>FieldSessionRecord</c> carries the
    /// investigation id, the place id, the uploader's user id and the publication state — none of
    /// which is any of a recipient's business, and all of which would arrive by default if this
    /// reused it. <see cref="SharedFieldSessionDetail"/> has no field for any of them, so the leak
    /// is not prevented by a line of code somebody could delete.</para>
    /// </remarks>
    [HttpGet("api/shared-sessions/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetShared(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var link = await ResolveAsync(db, token, ct);
        if (link is null) return NotFound();

        var session = await db.FieldSessionUploads.AsNoTracking()
            .Include(s => s.Files).ThenInclude(f => f.UploadFile)
            .Include(s => s.DocumentUploadFile)
            .FirstOrDefaultAsync(s => s.Id == link.FieldSessionUploadId, ct);
        if (session is null) return NotFound();

        if (session.DocumentUploadFile.StoragePath is not { } path || !_fileStorage.Exists(path))
            return NotFound("This session's readings are no longer on the server.");

        string document;
        await using (var stream = await _fileStorage.OpenReadAsync(path, ct))
        {
            if (stream is null) return NotFound("This session's readings are no longer on the server.");
            using var reader = new StreamReader(stream);
            document = await reader.ReadToEndAsync(ct);
        }

        var prepared = SharedSessionDocument.Prepare(document, link.IncludePositions);
        // Null means the document could not be parsed, so its coordinates could not be removed.
        // Sending it anyway on the hope that it holds no fix is the one failure this feature must
        // not have, so the honest answer is that the readings cannot be shown.
        if (prepared is null)
            return NotFound("This session's readings cannot be shown through a shared link.");

        await RecordViewAsync(db, link, fileId: null, ct);

        // Only the shared file, when the link names one: a list of everything else recorded that
        // night would tell the recipient exactly what they were not given.
        var files = session.Files
            .Where(f => link.FieldSessionUploadFileId is null || f.Id == link.FieldSessionUploadFileId)
            .OrderBy(f => f.RelativePath)
            .Select(f => new SharedFieldSessionFile(
                f.Id, f.RelativePath, f.UploadFile?.FileSize ?? 0, f.UploadFile?.ContentType))
            .ToList();

        return Ok(new SharedFieldSessionDetail(
            Document:          prepared.Value.Document,
            PositionsWithheld: prepared.Value.PositionsWithheld,
            DeviceModel:       session.DeviceModel,
            LocationLabel:     session.LocationLabel,
            StartedAt:         session.StartedAt,
            EndedAt:           session.EndedAt,
            ReadingCount:      session.ReadingCount,
            MarkerCount:       session.MarkerCount,
            SingleFileOnly:    link.FieldSessionUploadFileId is not null,
            ExpiresUtc:        link.ExpiresUtc,
            Note:              link.Note,
            Files:             files));
    }

    /// <summary>Streams one recording from a shared session.</summary>
    /// <remarks>
    /// Outside the rate limiter for the same reason the signed-in file route is: every anonymous
    /// visitor shares one partition, so a page fetching four recordings at once could exhaust the
    /// allowance for the whole site.
    /// </remarks>
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    [HttpGet("api/shared-sessions/{token}/files/{fileId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSharedFile(string token, Guid fileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var link = await ResolveAsync(db, token, ct);
        if (link is null) return NotFound();

        // A link to ONE recording reaches exactly that recording. Without this line the token
        // would be a key to the whole night, and the single-evidence share would be a fiction.
        if (link.FieldSessionUploadFileId is Guid only && only != fileId) return NotFound();

        var file = await db.FieldSessionUploadFiles.AsNoTracking()
            .Include(f => f.UploadFile)
            .FirstOrDefaultAsync(
                f => f.Id == fileId && f.FieldSessionUploadId == link.FieldSessionUploadId, ct);
        if (file is null) return NotFound();

        if (file.UploadFile.StoragePath is not { } recordingPath || !_fileStorage.Exists(recordingPath))
            return NotFound("That recording is no longer on the server.");

        await RecordViewAsync(db, link, fileId, ct);

        var stream = await _fileStorage.OpenReadAsync(recordingPath, ct);
        return File(stream, file.UploadFile.ContentType ?? "application/octet-stream",
                    Path.GetFileName(file.RelativePath), enableRangeProcessing: true);
    }

    // ── The rules, in one place each ──────────────────────────────────────────

    /// <summary>
    /// Reads a token back, returning the link only when it is live.
    /// </summary>
    /// <remarks>
    /// Every anonymous request goes through here, and every reason to refuse is checked in this
    /// one method so no endpoint can be added later that forgets one of them.
    /// </remarks>
    private static async Task<FieldSessionShareLink?> ResolveAsync(
        BenDataContext db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;

        var link = await db.FieldSessionShareLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Token == token, ct);
        if (link is null) return null;
        if (link.RevokedUtc is not null) return null;
        if (link.ExpiresUtc <= DateTime.UtcNow) return null;
        return link;
    }

    /// <summary>
    /// Who may hand this session to an outsider.
    /// </summary>
    /// <remarks>
    /// Narrower than reading it, on purpose — see the class remarks. The public-investigation door
    /// that <c>MayContributeAsync</c> opens is deliberately absent: a stranger reading an open
    /// investigation's session is the bargain that makes open investigations worth running, and
    /// minting links to it in somebody else's name is not part of that bargain.
    /// </remarks>
    private static async Task<bool> MayShareAsync(
        BenDataContext db, FieldSessionUpload session, Guid userId, CancellationToken ct)
    {
        if (session.SubmittedByAppUserId == userId) return true;
        if (session.InvestigationId is not Guid investigationId) return false;

        var organizationId = await db.Investigations.AsNoTracking()
            .Where(i => i.Id == investigationId)
            .Select(i => (Guid?)i.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (organizationId is not Guid orgId) return false;

        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
    }

    /// <summary>Writes the view down and moves the counters on the link.</summary>
    /// <remarks>
    /// A failure here must never cost the recipient their view: the log is the owner's
    /// reassurance, not a condition of the sharing, and a database hiccup that turned a working
    /// link into a 500 would be the counter breaking the thing it counts.
    /// </remarks>
    private async Task RecordViewAsync(
        BenDataContext db, FieldSessionShareLink link, Guid? fileId, CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            db.FieldSessionShareLinkViews.Add(new FieldSessionShareLinkView
            {
                FieldSessionShareLinkId  = link.Id,
                ViewedUtc                = now,
                ViewerHash               = ViewerHash(link.Token),
                UserAgent                = Truncate(Request.Headers.UserAgent.ToString(), 300),
                FieldSessionUploadFileId = fileId,
            });

            // Attached rather than re-read: the link came back AsNoTracking, and the two counters
            // are the only columns this write touches.
            var counters = new FieldSessionShareLink { Id = link.Id };
            db.FieldSessionShareLinks.Attach(counters);
            counters.ViewCount     = link.ViewCount + 1;
            counters.LastViewedUtc = now;
            db.Entry(counters).Property(l => l.ViewCount).IsModified     = true;
            db.Entry(counters).Property(l => l.LastViewedUtc).IsModified = true;

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not log a view of share link {ShareId}", link.Id);
        }
    }

    /// <summary>
    /// A salted digest of the caller's address — enough to separate two visitors, never enough to
    /// name one.
    /// </summary>
    /// <remarks>
    /// Salted with the link's own token, so the same address on two different links produces two
    /// unrelated hashes. Without that, an owner comparing digests across their links could tell
    /// that the same person opened both, which is a fact about a stranger that nobody asked for.
    /// An address space small enough to brute-force is exactly why the salt has to be per-link.
    /// </remarks>
    private string? ViewerHash(string salt)
    {
        var address = HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(address)) return null;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}|{address}"));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= max ? value : value[..max];

    /// <summary>
    /// A new token: 128 bits from the cryptographic RNG, base64url, twenty-two characters.
    /// </summary>
    /// <remarks>
    /// Not derived from anything. A derived token would come back the moment its inputs recurred,
    /// which is precisely what a revoked link must never do. 128 bits is unguessable against an
    /// endpoint that answers 404 to everything and is short enough to survive being pasted into a
    /// mail client that wraps lines.
    /// </remarks>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static FieldSessionShareRecord ToRecord(FieldSessionShareLink link, DateTime now) =>
        new(link.Id, link.Token, link.FieldSessionUploadFileId, link.Note,
            link.ExpiresUtc, link.RevokedUtc, link.IncludePositions,
            link.ViewCount, link.LastViewedUtc, link.DateCreated,
            IsLive: link.RevokedUtc is null && link.ExpiresUtc > now);
}

/// <summary>One share link, as its owner sees it.</summary>
/// <remarks>
/// The token IS here, unlike every other secret on the site — the whole purpose of the row is to
/// give its owner a string to paste into an email, and a link they cannot read is no link at all.
/// </remarks>
public sealed record FieldSessionShareRecord(
    Guid Id, string Token, Guid? FileId, string? Note,
    DateTime ExpiresUtc, DateTime? RevokedUtc, bool IncludePositions,
    int ViewCount, DateTime? LastViewedUtc, DateTime DateCreated, bool IsLive);

/// <summary>One opening of a link, as its owner sees it.</summary>
public sealed record FieldSessionShareViewRecord(
    DateTime ViewedUtc, string? ViewerHash, string? UserAgent, Guid? FileId);

/// <summary>
/// What somebody holding a share link is shown.
/// </summary>
/// <remarks>
/// <para><b>Absence is the design.</b> There is no investigation id, no place id, no case, no
/// uploader, no publication state and no storage path on this record — not withheld by a
/// condition, simply absent from the shape. A projection with nowhere to put a thing cannot leak
/// it, and cannot start leaking it because somebody widened a query three months from now.</para>
///
/// <para><see cref="PositionsWithheld"/> is here so the page can say so plainly. A viewer who is
/// not told that coordinates were removed will read "no fix" as a fact about the night rather
/// than a decision about them.</para>
/// </remarks>
public sealed record SharedFieldSessionDetail(
    string Document, bool PositionsWithheld,
    string DeviceModel, string? LocationLabel,
    DateTime StartedAt, DateTime? EndedAt,
    int ReadingCount, int MarkerCount,
    bool SingleFileOnly, DateTime ExpiresUtc, string? Note,
    IReadOnlyList<SharedFieldSessionFile> Files);

/// <summary>One recording a share link reaches. No digest, no upload-file id, no uploader.</summary>
public sealed record SharedFieldSessionFile(
    Guid Id, string RelativePath, long FileSize, string? ContentType);
