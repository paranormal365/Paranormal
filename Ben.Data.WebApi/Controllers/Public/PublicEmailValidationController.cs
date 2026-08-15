using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Redeems an email-validation link. The counterpart to
/// <see cref="Ben.Data.WebApi.Controllers.MyContactInfoController"/>'s <c>send-validation</c> action.
/// </summary>
/// <remarks>
/// <para><c>[AllowAnonymous]</c> throughout: possessing the token is the entire credential, exactly
/// as with the case-invite links in <see cref="CaseInviteController"/> — the person confirming an
/// email is not necessarily signed in on the device they're confirming from.</para>
///
/// <para><see cref="Confirm"/> is a <c>POST</c>, never a <c>GET</c>, on purpose. Corporate mail
/// scanners and link-preview services fetch every URL in an email automatically; a <c>GET</c> that
/// validated as a side effect would validate addresses nobody actually confirmed. The landing page
/// renders a button and the click is the confirmation.</para>
/// </remarks>
[ApiController]
[Route("api/public/email-validation")]
[AllowAnonymous]
public sealed class PublicEmailValidationController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IAuditLogService _auditLog;

    public PublicEmailValidationController(IDbContextFactory<BenDataContext> db, IAuditLogService auditLog)
    {
        _db = db;
        _auditLog = auditLog;
    }

    /// <summary>
    /// What the landing page needs to show before the person clicks Confirm: the address, masked,
    /// and whether the link is still good.
    /// </summary>
    [HttpGet("{token}")]
    public async Task<ActionResult<EmailValidationInfoRecord>> GetInfo(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var row = await db.UserEmails.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ValidationToken == token, ct);
        if (row is null) return NotFound();

        var expired = row.DateValidationSent is null
            || DateTime.UtcNow - row.DateValidationSent.Value > MyContactInfoController.ValidationLifetime;

        return Ok(new EmailValidationInfoRecord(Mask(row.EmailAddress), expired));
    }

    /// <summary>Confirms ownership of the address the token was issued for.</summary>
    [HttpPost("{token}")]
    public async Task<IActionResult> Confirm(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var row = await db.UserEmails.FirstOrDefaultAsync(e => e.ValidationToken == token, ct);
        if (row is null) return NotFound();

        var expired = row.DateValidationSent is null
            || DateTime.UtcNow - row.DateValidationSent.Value > MyContactInfoController.ValidationLifetime;
        if (expired) return StatusCode(StatusCodes.Status410Gone, "This validation link has expired. Request a new one from your profile.");

        var before = new UserEmail { Id = row.Id, IsValidated = row.IsValidated };

        row.IsValidated = true;
        row.DateValidated = DateTime.UtcNow;
        // Cleared, not merely marked used: a redeemed token must never validate a second time
        // (e.g. after the address is later changed back), and NotFound on replay is simpler and
        // safer than tracking a separate "already redeemed" state.
        row.ValidationToken = null;

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(UserEmail), row.Id, before, row, row.AppUserId, AppSources.WebApi, ct));

        return NoContent();
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email; // too short to usefully mask
        var name = email[..at];
        var visible = Math.Min(2, name.Length);
        return $"{name[..visible]}{new string('*', Math.Max(1, name.Length - visible))}{email[at..]}";
    }
}

public sealed record EmailValidationInfoRecord(string MaskedEmail, bool IsExpired);
