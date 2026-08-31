using Ben.Data.Common.Constants;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
// Policy, not Roles: [Authorize(Roles = ...)] re-authenticates with the default scheme only, so it
// answers 401 to a valid Entra caller - which would have made this page unreachable for exactly
// the sign-in most likely to be in use. AdminAuthorizationIsAPolicyTests guards the whole
// controller folder against the mistake; every other admin controller uses the policy form.
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminMailDiagnosticsController : ControllerBase
{
    private readonly IEmailService _email;
    private readonly SmtpOptions _options;
    private readonly ILogger<AdminMailDiagnosticsController> _logger;

    public AdminMailDiagnosticsController(IEmailService email,
                                          IOptions<SmtpOptions> options,
                                          ILogger<AdminMailDiagnosticsController> logger)
    {
        _email = email;
        _options = options.Value;
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
}

public sealed record MailTestRequest(string To);

public sealed record MailTestResult(bool Sent, string Message, string? ServerSaid);

public sealed record MailSettingsView(
    bool IsConfigured, string? Host, int Port, string Security,
    string? User, string? FromAddress, bool HasPassword);
