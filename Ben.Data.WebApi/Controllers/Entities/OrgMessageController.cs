using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Internal org messaging: send, reply, inbox, mark-read, view tracking.</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/messages")]
[Authorize]
public sealed class OrgMessageController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;
    private readonly Ben.Data.WebApi.Services.ICmsMarkupSanitizer _sanitizer;

    public OrgMessageController(
        IDbContextFactory<BenDataContext> db, IMapper mapper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security,
        Ben.Data.WebApi.Services.ICmsMarkupSanitizer sanitizer)
    {
        _db = db; _mapper = mapper; _security = security; _sanitizer = sanitizer;
    }

    /// <summary>
    /// Whether the caller belongs to this organization at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Found by the write-endpoint audit of 2026-08-26.</b> This controller carried
    /// <c>[Authorize]</c> and nothing else: the organization id came from the route, the author
    /// from the token, and no step in between asked whether the two had anything to do with each
    /// other. Any signed-in person could read a group's message board, and post to it, by knowing
    /// its id — the same broken-ID-chain shape the Phase-B audit found across nine controllers.
    /// </para>
    ///
    /// <para>Membership, not a grant: no permission AREA covers the group's message board, so
    /// there is no grant to consult. Belonging is the whole rule — and the rule that was
    /// missing.</para>
    /// </remarks>
    private async Task<bool> IsMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)) return true;

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;

        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
    }

    /// <summary>Returns the current user's inbox for this org (messages they received).</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<IEnumerable<OrgMessageRecord>>> GetInbox(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var msgIds = await db.OrgMessageRecipients.AsNoTracking()
            .Where(r => r.RecipientAppUserId == userId && r.OrgMessage!.OrganizationId == orgId)
            .Select(r => r.OrgMessageId)
            .ToListAsync(ct);

        var messages = await db.OrgMessages.AsNoTracking()
            .Include(m => m.AuthorAppUser)
            .Include(m => m.Replies)
            .Include(m => m.Recipients)
            .Where(m => msgIds.Contains(m.Id))
            .OrderByDescending(m => m.DateCreated)
            .ToListAsync(ct);

        var readIds = await db.OrgMessageRecipients.AsNoTracking()
            .Where(r => r.RecipientAppUserId == userId && r.DateRead != null)
            .Select(r => r.OrgMessageId)
            .ToHashSetAsync(ct);

        var records = messages.Select(m =>
        {
            var rec = _mapper.Map<OrgMessageRecord>(m);
            return rec with { IsReadByCurrentUser = readIds.Contains(m.Id) };
        });

        return Ok(records);
    }

    /// <summary>Returns messages the current user sent in this org.</summary>
    [HttpGet("sent")]
    public async Task<ActionResult<IEnumerable<OrgMessageRecord>>> GetSent(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var messages = await db.OrgMessages.AsNoTracking()
            .Include(m => m.AuthorAppUser)
            .Include(m => m.Replies)
            .Include(m => m.Recipients)
            .Where(m => m.OrganizationId == orgId && m.CreatedByAppUserId == userId)
            .OrderByDescending(m => m.DateCreated)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<OrgMessageRecord>>(messages));
    }

    /// <summary>Gets a single message + increments ViewCount and records the view. Visible to the
    /// message's author, a recipient, or (for the PublicFeed channel) anyone — the same scoping
    /// <see cref="GetInbox"/>/<see cref="GetSent"/> already apply, plus the channel's own
    /// <c>IsPublic</c> flag.</summary>
    [HttpGet("{messageId:guid}")]
    public async Task<ActionResult<OrgMessageRecord>> GetById(
        Guid orgId, Guid messageId, CancellationToken ct)
    {
        // NOT gated on membership, deliberately — unlike every other action here. This one reads
        // ONE message and already decides per message just below: public feed posts are meant to
        // be readable by anyone, and the author and recipients may read their own. A blanket
        // membership check here refused a public post to the public, which is what
        // GetById_PublicFeedMessage_AnyoneCanView caught the moment it was added.
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        var message = await db.OrgMessages
            .Include(m => m.AuthorAppUser)
            .Include(m => m.Replies)
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.OrganizationId == orgId, ct);
        if (message is null) return NotFound();

        var isRecipient = message.Recipients.Any(r => r.RecipientAppUserId == userId);
        var canView = message.IsPublic || message.AuthorAppUserId == userId || isRecipient
                      || User.IsInRole(RoleNames.SuperAdmin);
        if (!canView) return Forbid();

        // Record view if not already recorded
        var alreadyViewed = await db.OrgMessageViews.AnyAsync(
            v => v.OrgMessageId == messageId && v.ViewerAppUserId == userId, ct);
        if (!alreadyViewed)
        {
            db.OrgMessageViews.Add(new OrgMessageView
            {
                OrgMessageId     = messageId,
                ViewerAppUserId  = userId,
                DateViewed       = DateTime.UtcNow,
            });
            message.ViewCount++;
        }

        // Mark as read for this recipient
        var recipient = await db.OrgMessageRecipients
            .FirstOrDefaultAsync(r => r.OrgMessageId == messageId && r.RecipientAppUserId == userId, ct);
        if (recipient is not null && recipient.DateRead is null)
            recipient.DateRead = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        var readIds = await db.OrgMessageRecipients.AsNoTracking()
            .Where(r => r.RecipientAppUserId == userId && r.DateRead != null)
            .Select(r => r.OrgMessageId)
            .ToHashSetAsync(ct);

        var record = _mapper.Map<OrgMessageRecord>(message);
        return Ok(record with { IsReadByCurrentUser = readIds.Contains(message.Id) });
    }

    /// <summary>Sends a new message or reply.</summary>
    [HttpPost]
    public async Task<ActionResult<OrgMessageRecord>> Send(
        Guid orgId, [FromBody] SendOrgMessageRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var message = new OrgMessage
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            AuthorAppUserId    = userId,
            ParentMessageId    = request.ParentMessageId,
            ChannelType        = request.ChannelType,
            Subject            = request.Subject?.Trim(),
            // Authored in a rich-text editor and rendered as markup by every reader, so the
            // stored body is attacker-controlled HTML unless it is cleaned here. It was not:
            // an <img onerror> in a broadcast ran in each recipient's session, on this site's
            // origin, with no CSP to stop it. Cleaned on the way IN, the same rule the CMS and
            // publications already follow — cleaning at render would leave every stored payload
            // one forgotten call site away from firing.
            Body               = _sanitizer.SanitizeHtml(request.Body.Trim()),
            IsEncrypted        = request.IsEncrypted,
            IsPublic           = request.ChannelType == OrgMessageChannel.PublicFeed,
            CaseId             = request.CaseId,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.OrgMessages.Add(message);

        // Add recipients
        var now = DateTime.UtcNow;
        var recipientIds = request.RecipientUserIds.Distinct().Where(id => id != userId).ToList();

        if (request.ChannelType == OrgMessageChannel.OrgBroadcast && !recipientIds.Any())
        {
            // Auto-add all active members as recipients
            recipientIds = await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.OrganizationId == orgId && m.IsActive && m.AppUserId != userId)
                .Select(m => m.AppUserId)
                .ToListAsync(ct);
        }

        foreach (var recipientId in recipientIds)
        {
            db.OrgMessageRecipients.Add(new OrgMessageRecipient
            {
                Id                 = Guid.NewGuid(),
                OrgMessageId       = message.Id,
                RecipientAppUserId = recipientId,
                DateCreated        = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var loaded = await db.OrgMessages.AsNoTracking()
            .Include(m => m.AuthorAppUser)
            .Include(m => m.Replies)
            .Include(m => m.Recipients)
            .FirstAsync(m => m.Id == message.Id, ct);
        return CreatedAtAction(nameof(GetById), new { orgId, messageId = message.Id },
            _mapper.Map<OrgMessageRecord>(loaded));
    }
}

public sealed record SendOrgMessageRequest(
    OrgMessageChannel ChannelType,
    string? Subject,
    string Body,
    bool IsEncrypted,
    Guid? ParentMessageId,
    Guid? CaseId,
    IList<Guid> RecipientUserIds);
