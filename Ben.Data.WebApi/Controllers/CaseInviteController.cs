using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Public accept-side of the sub-client email invite flow (item #4's remaining piece) — the
/// counterpart to <see cref="MyCaseController"/>'s invite-management endpoints. No class-level
/// <c>[Authorize]</c>: the invitee has no account when they first open the link, so most of this
/// controller must be anonymous by necessity. <see cref="AcceptExisting"/> is the one exception.
/// </summary>
[ApiController]
[Route("api/case-invites")]
public sealed class CaseInviteController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public CaseInviteController(IDbContextFactory<BenDataContext> db, UserManager<AppUser> userManager, IAuditLogService auditLog)
    {
        _db = db; _userManager = userManager; _auditLog = auditLog;
    }

    /// <summary>
    /// Public info for the accept page: case/inviter display info, invite status, and whether the
    /// invited email already has an account (drives the page's register-vs-sign-in mode). Token
    /// possession is the only gate — deliberately exposes nothing beyond what the accept page needs.
    /// </summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<InviteInfoRecord>> GetInfo(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var invite = await db.CaseClientInvites.AsNoTracking()
            .Include(i => i.Case)
            .FirstOrDefaultAsync(i => i.Token == token, ct);
        if (invite is null) return NotFound();

        var accountExists = await db.Users.AsNoTracking().AnyAsync(u => u.Email == invite.Email, ct);
        var inviter = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == invite.CreatedByAppUserId, ct);

        return Ok(new InviteInfoRecord(
            invite.CaseId, invite.Case.Title, inviter?.DisplayName ?? "Someone",
            invite.Email, GetStatus(invite), accountExists));
    }

    /// <summary>
    /// Creates a brand-new local account from the invite and links it to the case. Fails if the
    /// invite isn't pending, or if an account already exists for the email (that's the
    /// <see cref="AcceptExisting"/> / sign-in path instead).
    /// </summary>
    [HttpPost("{token}/accept")]
    [AllowAnonymous]
    public async Task<ActionResult<AcceptInviteResult>> Accept(string token, [FromBody] AcceptInviteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return BadRequest("Display name is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) return BadRequest("Password is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var invite = await db.CaseClientInvites.FirstOrDefaultAsync(i => i.Token == token, ct);
        if (invite is null) return NotFound();
        var status = GetStatus(invite);
        if (status != InviteStatus.Valid) return BadRequest($"This invite is {status.ToString().ToLowerInvariant()}.");

        var existing = await _userManager.FindByEmailAsync(invite.Email);
        if (existing is not null)
            return Conflict("An account already exists for this email — sign in instead.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = invite.Email,
            UserName = invite.Email,
            NormalizedEmail = invite.Email.ToUpperInvariant(),
            NormalizedUserName = invite.Email.ToUpperInvariant(),
            EmailConfirmed = true, // they proved control of the inbox by opening the invite link
            DisplayName = request.DisplayName.Trim(),
            DateCreated = DateTime.UtcNow,
        };
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return BadRequest(createResult.Errors.Select(e => e.Description));

        var access = new CaseClientAccess
        {
            Id = Guid.NewGuid(), CaseId = invite.CaseId, AppUserId = user.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.CaseClientAccesses.Add(access);
        invite.DateAccepted = DateTime.UtcNow;
        invite.AcceptedByAppUserId = user.Id;
        invite.UpdatedByAppUserId = user.Id;
        invite.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(AppUser), user.Id, user, user.Id, AppSources.WebApi, ct));
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(CaseClientAccess), access.Id, access, user.Id, AppSources.WebApi, ct));

        return Ok(new AcceptInviteResult(invite.CaseId));
    }

    /// <summary>
    /// A signed-in user accepts an invite. Links the CURRENT user, not necessarily the invited
    /// email — token possession is the credential here, which also covers "registered under a
    /// different email than the one invited." Idempotent-friendly: if they already have access
    /// (primary or a prior co-client grant), this just marks the invite accepted.
    /// </summary>
    [HttpPost("{token}/accept-existing")]
    [Authorize]
    public async Task<ActionResult<AcceptInviteResult>> AcceptExisting(string token, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var invite = await db.CaseClientInvites.FirstOrDefaultAsync(i => i.Token == token, ct);
        if (invite is null) return NotFound();
        var status = GetStatus(invite);
        if (status != InviteStatus.Valid) return BadRequest($"This invite is {status.ToString().ToLowerInvariant()}.");

        var alreadyHasAccess = await db.CaseClientAccesses.AnyAsync(a => a.CaseId == invite.CaseId && a.AppUserId == userId, ct)
            || await db.Cases.AnyAsync(c => c.Id == invite.CaseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);

        if (!alreadyHasAccess)
        {
            var access = new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = invite.CaseId, AppUserId = userId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            db.CaseClientAccesses.Add(access);
            _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(CaseClientAccess), access.Id, access, userId, AppSources.WebApi, ct));
        }

        invite.DateAccepted = DateTime.UtcNow;
        invite.AcceptedByAppUserId = userId;
        invite.UpdatedByAppUserId = userId;
        invite.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new AcceptInviteResult(invite.CaseId));
    }

    private static InviteStatus GetStatus(CaseClientInvite invite)
    {
        if (invite.DateAccepted is not null) return InviteStatus.Used;
        if (invite.DateRevoked is not null) return InviteStatus.Revoked;
        if (invite.DateExpires <= DateTime.UtcNow) return InviteStatus.Expired;
        return InviteStatus.Valid;
    }
}

public enum InviteStatus { Valid, Used, Expired, Revoked }

public sealed record InviteInfoRecord(Guid CaseId, string CaseTitle, string InviterDisplayName, string Email, InviteStatus Status, bool AccountExists);
public sealed record AcceptInviteRequest(string DisplayName, string Password);
public sealed record AcceptInviteResult(Guid CaseId);
