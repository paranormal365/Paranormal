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

    public OrgMessageController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db; _mapper = mapper;
    }

    /// <summary>Returns the current user's inbox for this org (messages they received).</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<IEnumerable<OrgMessageRecord>>> GetInbox(
        Guid orgId, CancellationToken ct)
    {
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

    /// <summary>Gets a single message + increments ViewCount and records the view.</summary>
    [HttpGet("{messageId:guid}")]
    public async Task<ActionResult<OrgMessageRecord>> GetById(
        Guid orgId, Guid messageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var message = await db.OrgMessages
            .Include(m => m.AuthorAppUser)
            .Include(m => m.Replies)
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.OrganizationId == orgId, ct);
        if (message is null) return NotFound();

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
            Body               = request.Body.Trim(),
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
