using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The public contact form, and an anonymous sender's way back to their own ticket.
/// </summary>
/// <remarks>
/// <para>Anonymous by design — someone who cannot sign in is exactly the person most likely to need
/// this. Anti-spam lives in <see cref="SupportFormGuard"/>.</para>
///
/// <para>The stored ticket is the record; email is a notification on top and is allowed to fail.
/// That is what lets the whole feature work today, with SMTP unconfigured — a sender keeps their
/// thread through the tracking link, and staff see it in the queue regardless.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/support-tickets")]
public sealed class PublicSupportTicketController : ControllerBase
{
    private const int MaxSubjectLength = 200;
    private const int MaxBodyLength = 8000;
    private const int MaxNameLength = 120;
    private const int MaxEmailLength = 256;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly SupportFormGuard _guard;
    private readonly ILogger<PublicSupportTicketController> _log;

    public PublicSupportTicketController(
        IDbContextFactory<BenDataContext> db,
        SupportFormGuard guard,
        ILogger<PublicSupportTicketController> log)
    {
        _db = db;
        _guard = guard;
        _log = log;
    }

    /// <summary>Issued when the form renders; proves later how long it was on screen.</summary>
    [HttpGet("form-token")]
    public ActionResult<SupportFormTokenResponse> GetFormToken()
        => Ok(new SupportFormTokenResponse(_guard.IssueFormToken(DateTimeOffset.UtcNow)));

    /// <summary>Accepts a contact-form submission.</summary>
    [HttpPost]
    public async Task<ActionResult<SubmitSupportTicketResponse>> Submit(
        [FromBody] SubmitSupportTicketRequest request, CancellationToken ct)
    {
        // ── Spam checks, cheapest first ───────────────────────────────────────

        // A tripped honeypot gets 200 and an invented reference. Telling a bot which check caught
        // it is free tuning information for whoever wrote it; a submission that appears to succeed
        // and goes nowhere is not.
        if (SupportFormGuard.IsHoneypotTripped(request.Website))
        {
            _log.LogInformation("Support form honeypot tripped; submission discarded.");
            return Ok(new SubmitSupportTicketResponse($"SUP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}", Guid.NewGuid()));
        }

        var tokenResult = _guard.ValidateFormToken(request.FormToken, DateTimeOffset.UtcNow);
        if (tokenResult != FormTokenResult.Valid)
        {
            // A real person can genuinely hit Expired by leaving the tab open, so that one gets an
            // honest, actionable message. The rest are indistinguishable to a human being.
            return tokenResult == FormTokenResult.Expired
                ? BadRequest("This form has been open too long. Please reload the page and try again.")
                : BadRequest("Could not verify this submission. Please reload the page and try again.");
        }

        // ── Validation ────────────────────────────────────────────────────────

        var name = request.FromName?.Trim();
        var email = request.FromEmail?.Trim();
        var subject = request.Subject?.Trim();
        var body = request.Body?.Trim();

        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Please tell us your name.");
        if (string.IsNullOrWhiteSpace(email)) return BadRequest("Please give us an email address.");
        if (!IsPlausibleEmail(email)) return BadRequest("That email address doesn't look right.");
        if (string.IsNullOrWhiteSpace(subject)) return BadRequest("Please give your message a subject.");
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Please write your message.");

        if (name.Length > MaxNameLength) return BadRequest($"Name must be {MaxNameLength} characters or fewer.");
        if (email.Length > MaxEmailLength) return BadRequest($"Email must be {MaxEmailLength} characters or fewer.");
        if (subject.Length > MaxSubjectLength) return BadRequest($"Subject must be {MaxSubjectLength} characters or fewer.");
        if (body.Length > MaxBodyLength) return BadRequest($"Message must be {MaxBodyLength} characters or fewer.");
        if (!Enum.IsDefined(request.Topic)) return BadRequest("Please choose what your message is about.");

        await using var db = await _db.CreateDbContextAsync(ct);

        // ── Rate limits ───────────────────────────────────────────────────────

        var now = DateTime.UtcNow;
        var ipHash = _guard.HashIp(HttpContext.Connection.RemoteIpAddress?.ToString());
        var emailLower = email.ToLowerInvariant();

        var perEmail = await db.SupportTickets.CountAsync(
            t => t.FromEmail == emailLower && t.DateCreated > now.AddDays(-1), ct);
        if (perEmail >= SupportFormGuard.MaxPerEmailPerDay)
            return StatusCode(429, "You've sent us several messages today. We'll reply to those first.");

        if (ipHash is not null)
        {
            var perIp = await db.SupportTickets.CountAsync(
                t => t.SourceIpHash == ipHash && t.DateCreated > now.AddHours(-1), ct);
            if (perIp >= SupportFormGuard.MaxPerIpPerHour)
                return StatusCode(429, "Too many messages just now. Please try again in a little while.");
        }

        // ── Store ─────────────────────────────────────────────────────────────

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            Reference = $"SUP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            AccessToken = Guid.NewGuid(),
            FromName = name,
            // Stored lower-cased so the rate limit cannot be walked around with capital letters.
            FromEmail = emailLower,
            Topic = request.Topic,
            Subject = subject,
            Body = body,
            Status = SupportTicketStatus.New,
            AppUserId = CurrentUserId(),
            SourceIpHash = ipHash,
            DateCreated = now,
        };
        db.SupportTickets.Add(ticket);

        await NotifyStaffAsync(db, ticket, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new SubmitSupportTicketResponse(ticket.Reference, ticket.AccessToken));
    }

    /// <summary>A sender's own ticket, fetched with the token from their tracking link.</summary>
    [HttpGet("{accessToken:guid}")]
    public async Task<ActionResult<SupportTicketPublicRecord>> GetByToken(
        Guid accessToken, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var ticket = await db.SupportTickets.AsNoTracking()
            .Include(t => t.Replies.Where(r => !r.IsInternalNote))
                .ThenInclude(r => r.AuthorAppUser)
            .FirstOrDefaultAsync(t => t.AccessToken == accessToken, ct);

        if (ticket is null) return NotFound();

        return Ok(new SupportTicketPublicRecord(
            ticket.Reference,
            ticket.Topic,
            ticket.Subject,
            ticket.Body,
            ticket.Status,
            ticket.DateCreated,
            ticket.Replies
                .OrderBy(r => r.DateCreated)
                .Select(r => new SupportTicketReplyRecord(
                    r.Id,
                    r.Body,
                    r.IsFromStaff,
                    // Internal notes were filtered out above; this is always false here, and is
                    // carried so the sender and staff shapes stay comparable in tests.
                    false,
                    r.IsFromStaff ? (r.AuthorAppUser?.DisplayName ?? "Support") : ticket.FromName,
                    r.DateCreated))
                .ToList()));
    }

    /// <summary>Lets a sender add to their own thread through the tracking link.</summary>
    [HttpPost("{accessToken:guid}/replies")]
    public async Task<IActionResult> ReplyByToken(
        Guid accessToken, [FromBody] AddSupportTicketReplyRequest request, CancellationToken ct)
    {
        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Please write a message.");
        if (body.Length > MaxBodyLength) return BadRequest($"Message must be {MaxBodyLength} characters or fewer.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.AccessToken == accessToken, ct);
        if (ticket is null) return NotFound();
        if (ticket.Status == SupportTicketStatus.Closed)
            return BadRequest("This ticket is closed. Please send a new message.");

        db.SupportTicketReplies.Add(new SupportTicketReply
        {
            Id = Guid.NewGuid(),
            SupportTicketId = ticket.Id,
            Body = body,
            AuthorAppUserId = CurrentUserId(),
            IsFromStaff = false,
            // The sender's own reply can never be an internal note, whatever the request says.
            IsInternalNote = false,
            DateCreated = DateTime.UtcNow,
        });

        // Back into the queue: a sender who replies is waiting on staff again.
        ticket.Status = SupportTicketStatus.Open;
        ticket.DateUpdated = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id != Guid.Empty
            ? id
            : null;

    /// <summary>
    /// Deliberately loose. The address is for replying to, and a regex strict enough to be
    /// meaningful rejects valid addresses; the real test is whether mail to it arrives.
    /// </summary>
    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;
        if (email.IndexOf('@', at + 1) >= 0) return false;
        if (email.Contains(' ')) return false;
        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    /// <summary>
    /// Puts the new ticket in every app administrator's system-message inbox.
    /// </summary>
    /// <remarks>
    /// Added to the caller's change set rather than saved separately, so the notice and the ticket
    /// it announces commit together. Uses the inbox the notification bell already counts — no new
    /// delivery mechanism, and nothing here depends on SMTP.
    /// </remarks>
    private static async Task NotifyStaffAsync(
        BenDataContext db, SupportTicket ticket, CancellationToken ct)
    {
        var adminIds = await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == RoleNames.SuperAdmin || r.Name == RoleNames.Admin),
                  ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (adminIds.Count == 0) return;

        var message = new UserMessage
        {
            Id = Guid.NewGuid(),
            UserMessageTypeId = OrganizationSeeder.SupportTicketMessageTypeId,
            MessageSubject = $"[{ticket.Reference}] {ticket.Subject}",
            MessageBody =
                $"<strong>{System.Net.WebUtility.HtmlEncode(ticket.FromName)}</strong> " +
                $"({System.Net.WebUtility.HtmlEncode(ticket.FromEmail)}) sent a message via the contact form." +
                "<br><br>Open it from Administration → Support Tickets.",
            DateCreated = DateTime.UtcNow,
            // Anonymous senders have no account, so the notice is attributed to the sender's
            // account when there is one and to the recipient otherwise — CreatedByAppUserId is
            // non-nullable and a system message still has to belong to somebody.
            CreatedByAppUserId = ticket.AppUserId ?? adminIds[0],
        };
        db.UserMessages.Add(message);

        foreach (var adminId in adminIds)
        {
            db.UserMessageTos.Add(new UserMessageTo
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                ToAppUserId = adminId,
            });
        }
    }
}
