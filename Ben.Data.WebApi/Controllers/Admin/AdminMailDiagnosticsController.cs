using Ben.Data.Common.Constants;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Answers "is outgoing mail actually working, on this machine, right now?"
/// </summary>
/// <remarks>
/// <para><b>Why it has to run inside the application.</b> Mail depends on things that differ per
/// deployment and cannot be checked from anywhere else: the SMTP password arrives as the
/// <c>Smtp__Password</c> environment variable on the server, and the server's own outbound
/// firewall decides whether port 465 is reachable at all. A probe from a developer's laptop proves
/// the mail host is up and nothing about whether THIS box can send through it.</para>
///
/// <para><b>Why it exists.</b> Ben signed up on 2026-08-31, received nothing, and there was no way
/// to find out why: the sender swallowed its own failure, and the one log line that recorded it was
/// a Warning, below the database sink's Error threshold. So the failure left no trace at all. The
/// timestamps on <c>AppUser</c> fix the record after the fact; this answers the question before
/// anybody signs up.</para>
///
/// <para><b>It never reveals the password</b>, only whether one is present. A diagnostic that
/// prints a secret is a diagnostic nobody can safely run.</para>
/// </remarks>
[ApiController]
[Route("api/admin/mail")]
// Policy, NOT Roles: [Authorize(Roles = ...)] re-authenticates with the default scheme only
// and answers 401 to a valid Entra caller. AdminAuthorizationIsAPolicyTests enforces this, and
// caught it here.
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminMailDiagnosticsController : ControllerBase
{
    // The RAW sender, deliberately, and one of only three things that may hold it (item 239). A
    // diagnostic that queued the letter would prove that queueing works and nothing about whether
    // this machine can post — which is the only question this controller exists to answer.
    private readonly SmtpEmailService _email;
    private readonly SmtpOptions _options;
    private readonly IDbContextFactory<Ben.Data.Source.Context.BenDataContext> _db;
    private readonly ILogger<AdminMailDiagnosticsController> _logger;

    public AdminMailDiagnosticsController(SmtpEmailService email,
                                          IOptions<SmtpOptions> options,
                                          IDbContextFactory<Ben.Data.Source.Context.BenDataContext> db,
                                          ILogger<AdminMailDiagnosticsController> logger)
    {
        _email = email;
        _options = options.Value;
        _db = db;
        _logger = logger;
    }

    /// <summary>What this machine is configured to send with. No secrets.</summary>
    [HttpGet("settings")]
    public ActionResult<MailSettingsView> Settings() => Ok(new MailSettingsView(
        IsConfigured:    _email.IsConfigured,
        Host:            _options.Host,
        Port:            _options.Port,
        Security:        _options.Security.ToString(),
        User:            _options.User,
        FromAddress:     _options.FromAddress,
        // Presence, never the value. Its absence is the single most likely cause of a working
        // configuration that still cannot send.
        HasPassword:     !string.IsNullOrEmpty(_options.Password)));

    /// <summary>
    /// Sends one real message and reports exactly what the server said.
    /// </summary>
    /// <remarks>
    /// A real send, not a connection test, because the failures that matter here happen after the
    /// socket opens — authentication rejected, sender not permitted, relay denied. Those only
    /// appear when a message is actually offered.
    ///
    /// The SMTP error text is returned verbatim to the caller. That is safe because the route is
    /// SuperAdmin-only, and it is the entire value of the endpoint: "535 authentication failed"
    /// and "connection timed out" call for completely different fixes, and a tidied-up
    /// "could not send" tells you neither.
    /// </remarks>
    [HttpPost("test")]
    public async Task<ActionResult<MailTestResult>> SendTest(
        [FromBody] MailTestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.To))
            return BadRequest("Give an address to send to.");

        if (!_email.IsConfigured)
            return Ok(new MailTestResult(false,
                "SMTP is not configured on this machine — Smtp:Host is empty, so nothing is even "
              + "attempted. Every confirmation and invitation is silently going nowhere.", null));

        var stamp = DateTime.UtcNow.ToString("u");
        try
        {
            await _email.SendAsync(request.To,
                "Test message from IsHaunted.com",
                $"""
                 <p>Outgoing mail is working.</p>
                 <p>Sent {stamp} from the admin mail diagnostic.</p>
                 """, ct);

            _logger.LogInformation("Mail diagnostic sent a test message to {Recipient}.", request.To);
            return Ok(new MailTestResult(true,
                $"Sent to {request.To}. If it does not arrive, the message left this server and the "
              + "problem is delivery — spam filtering, or the recipient's provider rejecting it "
              + "after acceptance.", null));
        }
        catch (Exception ex)
        {
            // Error, so it survives in the Logs table. This is the line somebody reads a week
            // later when asking whether mail has ever worked here.
            _logger.LogError(ex, "Mail diagnostic could not send to {Recipient}.", request.To);
            return Ok(new MailTestResult(false,
                "The server refused it. The text below is what it said.", ex.Message));
        }
    }

    // ── The outbox (item 239) ────────────────────────────────────────────────

    /// <summary>
    /// Every letter the site has meant to send lately, and what became of it.
    /// </summary>
    /// <remarks>
    /// This is the screen that would have answered the 2026-08-31 question — "I signed up and got
    /// nothing" — in five seconds instead of not at all. Bodies are never returned: the list says
    /// whether one is still held, and a body carries somebody's name, what they booked and, for a
    /// hosted event, a working door code.
    /// </remarks>
    [HttpGet("outbox")]
    public async Task<ActionResult<IReadOnlyList<OutboxLetterView>>> Outbox(
        [FromQuery] string? state, [FromQuery] string? kind, [FromQuery] int take,
        CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var q = db.OutboxEmails.AsNoTracking();
        q = state?.ToLowerInvariant() switch
        {
            "failed"   => q.Where(e => e.FailedUtc != null),
            "waiting"  => q.Where(e => e.AcceptedBySmtpUtc == null && e.FailedUtc == null),
            "accepted" => q.Where(e => e.AcceptedBySmtpUtc != null),
            _          => q,
        };
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(e => e.Kind == kind);

        var rows = await q
            .OrderByDescending(e => e.CreatedUtc)
            .Take(Math.Clamp(take == 0 ? 100 : take, 1, 500))
            .Select(e => new OutboxLetterView(
                e.Id, e.To, e.Subject, e.Kind, e.CreatedUtc, e.Attempts, e.NextAttemptUtc,
                e.AcceptedBySmtpUtc, e.FailedUtc, e.LastError,
                e.HtmlBody != null, e.BodyScrubbedUtc, e.Attachments.Count))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Puts one given-up letter back in the queue.</summary>
    /// <remarks>
    /// <para>The attempt count goes back to zero, so the backoff starts again rather than the row
    /// being given up on immediately by a counter that has already run out. The last error is left
    /// where it is until the next attempt overwrites it, so somebody reading the row still sees
    /// why it stopped.</para>
    ///
    /// <para><b>A scrubbed letter cannot be retried</b>, and is refused in words rather than sent
    /// empty. Its words were cleared a month after it went, which is the trade the scrub makes.</para>
    /// </remarks>
    [HttpPost("outbox/{id:guid}/retry")]
    public async Task<ActionResult<OutboxRetryResult>> Retry(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var row = await db.OutboxEmails.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return NotFound();

        if (row.BodyScrubbedUtc is not null)
            return BadRequest("That letter's words were cleared a month after it was accepted, "
                            + "so there is nothing left to send. Ask whatever wrote it to write it again.");
        if (row.AcceptedBySmtpUtc is not null)
            return BadRequest("That letter was already accepted by the mail server. "
                            + "Sending it again would send a duplicate.");

        row.FailedUtc = null;
        row.Attempts = 0;
        row.NextAttemptUtc = DateTime.UtcNow;
        row.ClaimedUtc = null;
        row.ClaimedBy = null;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("A {Kind} letter to {To} was put back in the queue by hand.",
                               row.Kind, row.To);
        return Ok(new OutboxRetryResult(1, 0,
            "Back in the queue. The sender takes it on its next pass, within five minutes."));
    }

    /// <summary>Puts every given-up letter back in the queue.</summary>
    /// <remarks>
    /// The button somebody presses after fixing the relay. Scrubbed rows are skipped rather than
    /// counted, because sending an empty letter is worse than not sending one.
    /// </remarks>
    [HttpPost("outbox/retry-failed")]
    public async Task<ActionResult<OutboxRetryResult>> RetryAllFailed(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var revived = await db.OutboxEmails
            .Where(e => e.FailedUtc != null && e.BodyScrubbedUtc == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.FailedUtc, (DateTime?)null)
                .SetProperty(e => e.Attempts, 0)
                .SetProperty(e => e.NextAttemptUtc, now)
                .SetProperty(e => e.ClaimedUtc, (DateTime?)null)
                .SetProperty(e => e.ClaimedBy, (string?)null), ct);

        var unrevivable = await db.OutboxEmails
            .CountAsync(e => e.FailedUtc != null && e.BodyScrubbedUtc != null, ct);

        _logger.LogInformation("{Count} given-up letters were put back in the queue by hand.", revived);

        return Ok(new OutboxRetryResult(revived, unrevivable, unrevivable == 0
            ? $"{revived} letter(s) back in the queue."
            : $"{revived} letter(s) back in the queue. {unrevivable} could not be: their words were "
            + "cleared a month after they were accepted."));
    }
}

/// <summary>What a retry did.</summary>
/// <param name="Skipped">
/// Letters that could not go back in the queue because their words were scrubbed. Counted rather
/// than hidden: "12 requeued" reads as everything when four of them were not.
/// </param>
public sealed record OutboxRetryResult(int Requeued, int Skipped, string Message);

/// <summary>One letter in the outbox, without its words.</summary>
/// <param name="AcceptedBySmtpUtc">
/// When the mail server took it. <b>Not</b> when anybody received it — that is only knowable from
/// bounce reports this site does not collect.
/// </param>
/// <param name="HasBody">Whether the words are still held, and so whether a retry is possible.</param>
public sealed record OutboxLetterView(
    Guid Id,
    string To,
    string Subject,
    string Kind,
    DateTime CreatedUtc,
    int Attempts,
    DateTime NextAttemptUtc,
    DateTime? AcceptedBySmtpUtc,
    DateTime? FailedUtc,
    string? LastError,
    bool HasBody,
    DateTime? BodyScrubbedUtc,
    int AttachmentCount);

public sealed record MailTestRequest(string To);

public sealed record MailTestResult(bool Sent, string Message, string? ServerSaid);

public sealed record MailSettingsView(
    bool IsConfigured, string? Host, int Port, string Security,
    string? User, string? FromAddress, bool HasPassword);
