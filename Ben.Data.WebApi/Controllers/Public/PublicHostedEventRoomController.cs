using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// An event's room: what the people at the event post while they are there (item 235 phase 11).
/// </summary>
/// <remarks>
/// <para><b>Private to the people at the event</b> — see <see cref="EventRoom"/> — and never on the
/// public feed. Every feed query asks for <c>PublicFeed</c> by name, so a room message cannot leak
/// into it; <c>EventRoomTests</c> holds that.</para>
///
/// <para><b>A post's file is the poster's.</b> It is stored under their own account, and sending it to
/// the organizer or the venue is a share they choose — when posting, or later from the post. Taking
/// the post down takes it out of the room and leaves the file in their library.</para>
///
/// <para><b>Photos are screened like the feed's</b> when the site has an automatic screener. Without
/// one they show straight away: the room is a known group of people at one event, not the open feed,
/// and a manual queue built for the feed would leave a guest's photos waiting for a moderator who is
/// not looking at rooms. Anything the screener holds is seen only by its author and the event's
/// moderators. A report reaches the event's moderators and the site's report queue.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/public/hosted-events/{eventId:guid}/room")]
public sealed class PublicHostedEventRoomController : BenControllerBase
{
    private const int Page = 50;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;
    private readonly IFileStorageService _storage;
    private readonly IMediaIngestService _ingest;
    private readonly IFeedMediaScreener _screener;

    public PublicHostedEventRoomController(
        IDbContextFactory<BenDataContext> dbFactory, Services.Access.HostedEventAccess access,
        IFileStorageService storage, IMediaIngestService ingest, IFeedMediaScreener screener)
    { _dbFactory = dbFactory; _access = access; _storage = storage; _ingest = ingest; _screener = screener; }

    [HttpGet]
    public async Task<ActionResult<EventRoomRecord>> Get(Guid eventId, [FromQuery] DateTime? before, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        return Ok(await RoomAsync(db, hosted, userId, standing, before, null, ct));
    }

    [HttpPost]
    [RequestSizeLimit(200L * 1024 * 1024)]
    public async Task<ActionResult<EventRoomRecord>> Post(
        Guid eventId, [FromForm] string? body, IFormFile? media, [FromForm] bool sendToHosts, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        var now = DateTime.UtcNow;
        if (EventRoom.WhyClosed(hosted, [.. hosted.Nights.Select(n => n.Date)], now) is { } closed) return Conflict(closed);

        var text = body?.Trim() ?? "";
        if (text.Length == 0 && media is null) return BadRequest("Write something, or add a photo.");
        if (text.Length > EventRoom.MaxBody) return BadRequest($"Keep it under {EventRoom.MaxBody:N0} characters.");

        var message = new OrgMessage
        {
            Id = Guid.NewGuid(), HostedEventId = eventId, OrganizationId = null, AuthorAppUserId = userId,
            ChannelType = OrgMessageChannel.EventRoom, Body = text,
            DateCreated = now, CreatedByAppUserId = userId,
        };
        db.OrgMessages.Add(message);

        string? sentTo = null;
        if (media is not null)
        {
            if (!EventRoom.MayAddPhotos(hosted, standing))
                return Conflict("The organizers have kept photos to the event's own team. You can still write in the room.");

            if (media.Length == 0) return BadRequest("That file is empty.");
            var type = media.ContentType ?? "";
            if (!(type.StartsWith("image/") || type.StartsWith("video/")) || type.Contains("svg", StringComparison.OrdinalIgnoreCase))
                return BadRequest("The room takes photos and videos. Other files go to the organizers directly.");

            var uploadId = Guid.NewGuid();
            var storedName = $"{uploadId:N}{Path.GetExtension(media.FileName).ToLowerInvariant()}";
            var storagePath = _storage.UserFilePath(userId, storedName);

            IngestedMedia ingested;
            try
            {
                ingested = await _ingest.IngestAsync(media, storagePath, uploadId, ct);
            }
            catch (UnreadableImageException ex)
            {
                return BadRequest(ex.Message);
            }

            // The poster's own file: AppUserId, not the group's.
            db.UploadFiles.Add(new UploadFile
            {
                Id = uploadId, UploadFileTypeId = UploadFileTypeSeeder.FeedMediaFileTypeId, AppUserId = userId,
                FileName = Path.GetFileName(media.FileName), StoredFileName = storedName,
                ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
                StoragePath = storagePath, IsPublic = false, DateCreated = now, CreatedByAppUserId = userId,
            });
            db.UploadFileMetadata.Add(ingested.Metadata);
            message.MediaUploadFileId = uploadId;

            if (_screener.IsAutomatic)
            {
                try
                {
                    var verdict = await _screener.ScreenAsync(storagePath, ingested.ServedContentType, ct);
                    message.MediaReviewState = verdict.State;
                    message.MediaReviewNote = verdict.Reason;
                    message.MediaScreenerScore = verdict.Score;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    message.MediaReviewState = FeedMediaReviewState.Pending;
                }
            }
            else
            {
                message.MediaReviewState = FeedMediaReviewState.Approved;
                message.MediaReviewNote = "Shown without a screener: an event room is a known group of people.";
            }

            if (sendToHosts)
                sentTo = string.Join(" and ", await EventRoom.ShareWithTheHostsAsync(db, hosted, uploadId, userId, now, ct));
        }

        await db.SaveChangesAsync(ct);
        return Ok(await RoomAsync(db, hosted, userId, standing, null,
            sentTo is null ? null : $"Posted, and your photo was sent to {sentTo}. It's still yours.", ct));
    }

    /// <summary>Sends an earlier post's file to the organizer and venue. The author only.</summary>
    [HttpPost("messages/{messageId:guid}/send-to-hosts")]
    public async Task<ActionResult<EventRoomRecord>> SendToHosts(Guid eventId, Guid messageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        var message = await db.OrgMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.HostedEventId == eventId && m.ChannelType == OrgMessageChannel.EventRoom, ct);
        if (message is null) return NotFound();
        if (message.AuthorAppUserId != userId) return Conflict("Only the person who posted it can send it on. It's theirs.");
        if (message.MediaUploadFileId is not Guid fileId) return Conflict("There's no photo on that post to send.");

        var sentTo = await EventRoom.ShareWithTheHostsAsync(db, hosted, fileId, userId, DateTime.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return Ok(await RoomAsync(db, hosted, userId, standing, null, $"Sent to {string.Join(" and ", sentTo)}. It's still yours.", ct));
    }

    /// <summary>The author takes their own post down. The file stays in their library.</summary>
    [HttpDelete("messages/{messageId:guid}")]
    public async Task<ActionResult<EventRoomRecord>> Remove(Guid eventId, Guid messageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        var message = await db.OrgMessages.FirstOrDefaultAsync(
            m => m.Id == messageId && m.HostedEventId == eventId && m.ChannelType == OrgMessageChannel.EventRoom, ct);
        if (message is null) return NotFound();
        if (message.AuthorAppUserId != userId) return Conflict("You can only take down your own posts.");

        await db.OrgMessageReports.Where(r => r.OrgMessageId == messageId).ExecuteDeleteAsync(ct);
        db.OrgMessages.Remove(message);
        await db.SaveChangesAsync(ct);
        return Ok(await RoomAsync(db, hosted, userId, standing, null, "Taken down. Any photo is still in your own files.", ct));
    }

    [HttpPost("messages/{messageId:guid}/report")]
    public async Task<ActionResult<EventRoomRecord>> Report(
        Guid eventId, Guid messageId, [FromBody] ReportEventRoomMessageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        if (!await db.OrgMessages.AnyAsync(m => m.Id == messageId && m.HostedEventId == eventId, ct)) return NotFound();

        if (!await db.OrgMessageReports.AnyAsync(r => r.OrgMessageId == messageId && r.ReportedByAppUserId == userId, ct))
        {
            db.OrgMessageReports.Add(new OrgMessageReport
            {
                Id = Guid.NewGuid(), OrgMessageId = messageId, ReportedByAppUserId = userId,
                Reason = request.Reason?.Trim() is { Length: > 0 } r ? (r.Length > 500 ? r[..500] : r) : null,
                DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        return Ok(await RoomAsync(db, hosted, userId, standing, null, "Reported. The organizers will see it.", ct));
    }

    [HttpPost("messages/{messageId:guid}/hide")]
    public Task<ActionResult<EventRoomRecord>> Hide(Guid eventId, Guid messageId, CancellationToken ct)
        => SetHiddenAsync(eventId, messageId, hide: true, ct);

    [HttpPost("messages/{messageId:guid}/unhide")]
    public Task<ActionResult<EventRoomRecord>> Unhide(Guid eventId, Guid messageId, CancellationToken ct)
        => SetHiddenAsync(eventId, messageId, hide: false, ct);

    /// <summary>Who may add photos: the team only, or the team and confirmed guests.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<EventRoomRecord>> Settings(Guid eventId, [FromBody] EventRoomSettingsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();
        if (!standing.CanModerate) return Forbid();
        if (!Enum.IsDefined(request.PhotoPosting)) return BadRequest("Choose who may add photos.");

        var tracked = await db.HostedEvents.FirstAsync(e => e.Id == eventId, ct);
        tracked.PhotoPosting = request.PhotoPosting;
        await db.SaveChangesAsync(ct);
        hosted.PhotoPosting = request.PhotoPosting;

        return Ok(await RoomAsync(db, hosted, userId, standing, null,
            request.PhotoPosting == EventPhotoPosting.TeamOnly
                ? "Only the event's team can add photos now. Guests can still write."
                : "The team and confirmed guests can add photos.", ct));
    }

    /// <summary>
    /// The photo wall's pictures: approved, not hidden, newest first, for the people in the room.
    /// </summary>
    /// <remarks>
    /// The same rules as the room, because it is the room's photos on a bigger screen: nothing held by
    /// the screener, nothing a moderator hid, and nobody outside the event. Polled by the wall page, so
    /// a photo taken at 9 PM is on the ballroom's TV by 9:01.
    /// </remarks>
    [HttpGet("photos")]
    public async Task<ActionResult<EventWallRecord>> Photos(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        var photos = await db.OrgMessages.AsNoTracking()
            .Where(m => m.HostedEventId == eventId && m.ChannelType == OrgMessageChannel.EventRoom
                     && m.MediaUploadFileId != null && m.HiddenUtc == null
                     && m.MediaReviewState == FeedMediaReviewState.Approved)
            .OrderByDescending(m => m.DateCreated)
            .Take(200)
            .Select(m => new EventWallPhotoRecord(
                m.Id, m.MediaUploadFile!.ContentType, m.AuthorAppUser.DisplayName ?? "A guest",
                m.Body == "" ? null : m.Body, m.DateCreated))
            .ToListAsync(ct);

        return Ok(new EventWallRecord(hosted.Name, photos));
    }

    [HttpPost("close")]
    public Task<ActionResult<EventRoomRecord>> Close(Guid eventId, CancellationToken ct) => SetClosedAsync(eventId, close: true, ct);

    [HttpPost("reopen")]
    public Task<ActionResult<EventRoomRecord>> Reopen(Guid eventId, CancellationToken ct) => SetClosedAsync(eventId, close: false, ct);

    /// <summary>A post's photo or video, for the people in the room.</summary>
    [HttpGet("messages/{messageId:guid}/media")]
    public async Task<IActionResult> Media(Guid eventId, Guid messageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();

        var message = await db.OrgMessages.AsNoTracking()
            .Include(m => m.MediaUploadFile)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.HostedEventId == eventId && m.ChannelType == OrgMessageChannel.EventRoom, ct);
        if (message?.MediaUploadFile?.StoragePath is not { } stored) return NotFound();

        var mayLook = standing.CanModerate || message.AuthorAppUserId == userId
                   || (message.MediaReviewState == FeedMediaReviewState.Approved && message.HiddenUtc == null);
        if (!mayLook) return NotFound();

        var path = _ingest.ServingPathFor(stored);
        if (!_storage.Exists(path)) return NotFound();
        return File(await _storage.OpenReadAsync(path, ct), message.MediaUploadFile.ContentType, enableRangeProcessing: true);
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task<ActionResult<EventRoomRecord>> SetHiddenAsync(Guid eventId, Guid messageId, bool hide, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();
        if (!standing.CanModerate) return Forbid();

        var message = await db.OrgMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.HostedEventId == eventId, ct);
        if (message is null) return NotFound();

        message.HiddenUtc = hide ? DateTime.UtcNow : null;
        message.HiddenByAppUserId = hide ? userId : null;
        await db.SaveChangesAsync(ct);
        return Ok(await RoomAsync(db, hosted, userId, standing, null, hide ? "Hidden from the room." : "Back in the room.", ct));
    }

    private async Task<ActionResult<EventRoomRecord>> SetClosedAsync(Guid eventId, bool close, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (hosted, standing) = await LoadAsync(db, eventId, userId, ct);
        if (hosted is null || !standing.IsMember) return NotFound();
        if (!standing.CanModerate) return Forbid();

        var tracked = await db.HostedEvents.FirstAsync(e => e.Id == eventId, ct);
        tracked.RoomClosedUtc = close ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        hosted.RoomClosedUtc = tracked.RoomClosedUtc;

        return Ok(await RoomAsync(db, hosted, userId, standing, null, close ? "The room is closed to new posts." : "The room is open again.", ct));
    }

    private async Task<(HostedEvent? Hosted, EventRoom.Standing Standing)> LoadAsync(
        BenDataContext db, Guid eventId, Guid userId, CancellationToken ct)
    {
        var hosted = await db.HostedEvents.AsNoTracking().Include(e => e.Nights).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (hosted is null) return (null, new(false, false));
        return (hosted, await EventRoom.StandingAsync(db, _access, hosted, userId, ct));
    }

    private static async Task<EventRoomRecord> RoomAsync(
        BenDataContext db, HostedEvent hosted, Guid viewerId, EventRoom.Standing standing, DateTime? before, string? note, CancellationToken ct)
    {
        var query = db.OrgMessages.AsNoTracking()
            .Where(m => m.HostedEventId == hosted.Id && m.ChannelType == OrgMessageChannel.EventRoom);
        if (!standing.CanModerate) query = query.Where(m => m.HiddenUtc == null || m.AuthorAppUserId == viewerId);
        if (before is { } b) query = query.Where(m => m.DateCreated < b);

        var hostOrgs = new List<Guid> { hosted.OrganizationId };
        if (hosted.VenueGrantId is Guid grantId
            && await db.OrganizationVenueGrants.AsNoTracking().Where(g => g.Id == grantId && g.RevokedUtc == null)
                   .Select(g => (Guid?)g.VenueOrganizationId).FirstOrDefaultAsync(ct) is Guid venueOrg)
            hostOrgs.Add(venueOrg);

        var rows = await query
            .OrderByDescending(m => m.DateCreated)
            .Take(Page)
            .Select(m => new
            {
                m.Id, m.AuthorAppUserId, AuthorName = m.AuthorAppUser.DisplayName ?? "A guest", m.Body, m.DateCreated,
                m.MediaUploadFileId, MediaType = m.MediaUploadFile != null ? m.MediaUploadFile.ContentType : null,
                m.MediaReviewState, m.HiddenUtc,
                Sent = m.MediaUploadFileId != null && db.UploadFileOrganizationShares.Count(s =>
                    s.UploadFileId == m.MediaUploadFileId && s.IsActive && hostOrgs.Contains(s.OrganizationId)) >= hostOrgs.Count,
                Reports = db.OrgMessageReports.Count(r => r.OrgMessageId == m.Id),
            })
            .ToListAsync(ct);

        var names = await db.Organizations.AsNoTracking().Where(o => hostOrgs.Contains(o.Id)).Select(o => o.Name).ToListAsync(ct);
        var whyClosed = EventRoom.WhyClosed(hosted, [.. hosted.Nights.Select(n => n.Date)], DateTime.UtcNow);

        return new EventRoomRecord(
            whyClosed is null, whyClosed, standing.CanModerate, names,
            [.. rows
                // A photo the screener is holding is its author's and the moderators' to see, and nobody else's —
                // the post stays, its picture does not.
                .Select(r =>
                {
                    var waiting = r.MediaUploadFileId is not null && r.MediaReviewState != FeedMediaReviewState.Approved;
                    var showMedia = r.MediaUploadFileId is not null && (!waiting || standing.CanModerate || r.AuthorAppUserId == viewerId);
                    return new EventRoomMessageRecord(
                        r.Id, r.AuthorAppUserId, r.AuthorName, r.Body, r.DateCreated,
                        showMedia, showMedia ? r.MediaType : null, waiting && showMedia,
                        r.AuthorAppUserId == viewerId, r.HiddenUtc is not null,
                        r.Sent, standing.CanModerate ? r.Reports : 0);
                })],
            note,
            hosted.PhotoPosting,
            whyClosed is null && EventRoom.MayAddPhotos(hosted, standing));
    }
}
