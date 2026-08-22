using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The staff queue for contact-form tickets.
/// </summary>
/// <remarks>
/// Open to both app-wide roles. Support is the one thing <see cref="RoleNames.Admin"/> is plausibly
/// for beyond reading the administration help — but note this is a deliberate, specific grant, not
/// a general widening: every other SuperAdmin check in the app is still SuperAdmin-only.
/// </remarks>
[ApiController]
[Authorize(Policy = AuthPolicyNames.AppAdministrator)]
[Route("api/admin/support-tickets")]
public sealed class AdminSupportTicketController : BenControllerBase
{
    private const int MaxBodyLength = 8000;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IAuditLogService _auditLog;

    public AdminSupportTicketController(IDbContextFactory<BenDataContext> db, IAuditLogService auditLog)
    {
        _db = db;
        _auditLog = auditLog;
    }

    /// <summary>The queue, filtered and paged on the server.</summary>
    [HttpGet]
    public async Task<ActionResult<SupportTicketPage>> GetAll(
        [FromQuery] SupportTicketStatus? status = null,
        [FromQuery] SupportTicketTopic? topic = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        await using var db = await _db.CreateDbContextAsync(ct);

        var query = db.SupportTickets.AsNoTracking();

        if (status is not null) query = query.Where(t => t.Status == status);
        if (topic is not null) query = query.Where(t => t.Topic == topic);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t =>
                t.Reference.ToLower().Contains(term) ||
                t.Subject.ToLower().Contains(term) ||
                t.FromName.ToLower().Contains(term) ||
                t.FromEmail.Contains(term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            // Newest first, with Id breaking ties so paging is stable when two arrive together.
            .OrderByDescending(t => t.DateCreated)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new SupportTicketAdminRecord(
                t.Id,
                t.Reference,
                t.FromName,
                t.FromEmail,
                t.Topic,
                t.Subject,
                t.Body,
                t.Status,
                t.AppUserId,
                t.AssignedToAppUserId,
                t.AssignedToAppUser != null ? t.AssignedToAppUser.DisplayName : null,
                t.Replies.Count,
                t.DateCreated,
                t.DateUpdated,
                t.DateClosed))
            .ToListAsync(ct);

        return Ok(new SupportTicketPage(items, total, page, pageSize));
    }

    /// <summary>One ticket's full thread, internal notes included.</summary>
    [HttpGet("{id:guid}/replies")]
    public async Task<ActionResult<IReadOnlyList<SupportTicketReplyRecord>>> GetReplies(
        Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.SupportTickets.AnyAsync(t => t.Id == id, ct)) return NotFound();

        var replies = await db.SupportTicketReplies.AsNoTracking()
            .Where(r => r.SupportTicketId == id)
            .OrderBy(r => r.DateCreated)
            .Select(r => new SupportTicketReplyRecord(
                r.Id,
                r.Body,
                r.IsFromStaff,
                r.IsInternalNote,
                r.AuthorAppUser != null ? r.AuthorAppUser.DisplayName : null,
                r.DateCreated))
            .ToListAsync(ct);

        return Ok(replies);
    }

    /// <summary>Replies to the sender, or leaves an internal note.</summary>
    [HttpPost("{id:guid}/replies")]
    public async Task<IActionResult> AddReply(
        Guid id, [FromBody] AddSupportTicketReplyRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var body = request.Body?.Trim();

        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Please write a message.");
        if (body.Length > MaxBodyLength) return BadRequest($"Message must be {MaxBodyLength} characters or fewer.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        db.SupportTicketReplies.Add(new SupportTicketReply
        {
            Id = Guid.NewGuid(),
            SupportTicketId = id,
            Body = body,
            AuthorAppUserId = userId == Guid.Empty ? null : userId,
            IsFromStaff = true,
            IsInternalNote = request.IsInternalNote,
            DateCreated = DateTime.UtcNow,
        });

        // An internal note is staff talking among themselves, so it does not mean the sender has
        // been answered. Only a real reply moves the ticket on.
        if (!request.IsInternalNote && ticket.Status != SupportTicketStatus.Closed)
            ticket.Status = SupportTicketStatus.Answered;

        // Picking it up by replying is the common case; save the extra click.
        ticket.AssignedToAppUserId ??= userId == Guid.Empty ? null : userId;
        ticket.DateUpdated = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Changes status and/or assignment.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupportTicketAdminRecord>> Update(
        Guid id, [FromBody] UpdateSupportTicketRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        if (request.Status is { } requested && !Enum.IsDefined(requested))
            return BadRequest("Unknown status.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        var before = new SupportTicket
        {
            Id = ticket.Id,
            Status = ticket.Status,
            AssignedToAppUserId = ticket.AssignedToAppUserId,
        };

        if (request.Status is { } status)
        {
            ticket.Status = status;
            // Set on the way in, cleared on the way out, so a reopened ticket does not keep a
            // closing date that is no longer true.
            ticket.DateClosed = status == SupportTicketStatus.Closed ? DateTime.UtcNow : null;
        }

        if (request.AssignedToAppUserId is { } assignee)
        {
            if (assignee != Guid.Empty && !await db.Users.AnyAsync(u => u.Id == assignee, ct))
                return BadRequest("That user does not exist.");

            ticket.AssignedToAppUserId = assignee == Guid.Empty ? null : assignee;
        }

        ticket.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await _auditLog.LogUpdateAsync(
            nameof(SupportTicket), ticket.Id, before, ticket, userId, AppSources.WebApi);

        return Ok(new SupportTicketAdminRecord(
            ticket.Id, ticket.Reference, ticket.FromName, ticket.FromEmail, ticket.Topic,
            ticket.Subject, ticket.Body, ticket.Status, ticket.AppUserId,
            ticket.AssignedToAppUserId, null,
            await db.SupportTicketReplies.CountAsync(r => r.SupportTicketId == id, ct),
            ticket.DateCreated, ticket.DateUpdated, ticket.DateClosed));
    }
}
