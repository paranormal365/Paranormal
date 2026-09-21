using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/app-users")]
public sealed class AdminAppUserController : AdminEntityControllerBase<AppUser, AppUserAdminRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;
    private readonly UserManager<AppUser> _userManager;
    private readonly Ben.Data.WebApi.Services.UserHandleService _handles;
    private readonly IAuditLogService _auditLog;
    private readonly Ben.Data.WebApi.Services.IConfirmationMailer? _mailer;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<AdminAppUserController>? _log;

    // The mailer, the site's own address and the log are OPTIONAL and TRAILING on purpose. Twenty
    // test files broke the last time a service gained a required constructor argument, and a
    // letter is not a reason to make every fixture that builds this controller supply one.
    public AdminAppUserController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper,
        IAuditLogService auditLog, UserManager<AppUser> userManager,
        Ben.Data.WebApi.Services.UserHandleService handles,
        Ben.Data.WebApi.Services.IConfirmationMailer? mailer = null,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity>? site = null,
        ILogger<AdminAppUserController>? log = null)
        : base(dbContextFactory, mapper, auditLog)
    {
        _handles     = handles;
        _dbFactory   = dbContextFactory;
        _mapper      = mapper;
        _userManager = userManager;
        _auditLog    = auditLog;
        _mailer      = mailer;
        _site        = site?.Value ?? new Ben.Data.Common.SiteIdentity();
        _log         = log;
    }

    /// <summary>Suppresses the base Create(TEntity) route — use CreateUser instead.</summary>
    [NonAction]
    public override Task<ActionResult<AppUserAdminRecord>> Create(
        [FromBody] AppUser entity, CancellationToken cancellationToken)
        => throw new NotSupportedException("Use POST /api/admin/app-users with AdminCreateUserRequest.");

    /// <summary>
    /// When each account last signed in, and how many times.
    /// </summary>
    /// <remarks>
    /// <para><b>One grouped query for the whole page</b>, not a subquery per row. The users grid
    /// draws every account, and asking the sign-in table once per row would be a query per person
    /// per page load; <c>SignInEvents</c> is indexed on <c>(AppUserId, Utc)</c>, so grouping by
    /// the account is the shape it is built for.</para>
    ///
    /// <para><b>Accounts that have never signed in are simply absent.</b> Returning a row of
    /// zeroes for each of them would be a larger answer that says nothing; the screen shows
    /// "Never" for anybody it finds no row for, which is the same fact without the freight.</para>
    ///
    /// <para><b>What the numbers do and do not cover.</b> Successful sign-ins only, of every
    /// method the site records — password, Apple, the editor handoff, and since 2026-09-19 one row
    /// per twelve-hour Microsoft visit (<c>EntraSignInSessions</c>). Nothing goes further back
    /// than 2026-08-20, when the table was created: an account older than that has a count from
    /// that day, not from the day it was made.</para>
    /// </remarks>
    [HttpGet("sign-in-summary")]
    public async Task<ActionResult<IEnumerable<UserSignInSummary>>> GetSignInSummary(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var rows = await db.SignInEvents.AsNoTracking()
            .Where(e => e.Succeeded && e.AppUserId != null)
            .GroupBy(e => e.AppUserId!.Value)
            .Select(g => new UserSignInSummary(g.Key, g.Max(e => e.Utc), g.Count()))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Returns the full user aggregate including all related records.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<AppUserDetailAdminRecord>> GetDetail(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var addresses   = await db.UserAddresses.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var emails      = await db.UserEmails.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var phones      = await db.UserPhones.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var links       = await db.UserLinks.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var notes       = await db.UserNotes.AsNoTracking().Where(x => x.CreatedByAppUserId == id).ToListAsync(ct);
        var messages    = await db.UserMessages.AsNoTracking().Where(x => x.CreatedByAppUserId == id).ToListAsync(ct);
        var memberships = await db.OrganizationUserMemberships.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var files       = await db.UploadFiles.AsNoTracking().Where(x => x.AppUserId == id).ToListAsync(ct);
        var roles       = (await _userManager.GetRolesAsync(user) ?? []).OrderBy(r => r).ToList();

        return Ok(new AppUserDetailAdminRecord
        {
            User        = _mapper.Map<AppUserAdminRecord>(user),
            Addresses   = _mapper.Map<IReadOnlyList<UserAddressAdminRecord>>(addresses),
            Emails      = _mapper.Map<IReadOnlyList<UserEmailAdminRecord>>(emails),
            Phones      = _mapper.Map<IReadOnlyList<UserPhoneAdminRecord>>(phones),
            Links       = _mapper.Map<IReadOnlyList<UserLinkAdminRecord>>(links),
            Notes       = _mapper.Map<IReadOnlyList<UserNoteAdminRecord>>(notes),
            Messages    = _mapper.Map<IReadOnlyList<UserMessageAdminRecord>>(messages),
            Memberships = _mapper.Map<IReadOnlyList<OrganizationUserMembershipAdminRecord>>(memberships),
            UploadFiles = _mapper.Map<IReadOnlyList<UploadFileAdminRecord>>(files),
            Roles       = roles
        });
    }

    /// <summary>
    /// Sets the site roles a person holds — the whole set, not a delta (item 216).
    /// </summary>
    /// <remarks>
    /// <para>Until this existed, the only way into Admin or Moderator was a row typed into
    /// <c>AspNetUserRoles</c> by hand: the Site Roles page could create a role and count its
    /// members but never add one, and the New User form offered SuperAdmin alone, at creation
    /// only. The roles were seeded "so a SuperAdmin can assign them" and nothing let one.</para>
    ///
    /// <para><b>Two refusals, both about SuperAdmin.</b> A SuperAdmin may not remove that role
    /// from themselves, and nobody may remove it from the last person holding it. Either one
    /// leaves the site with nobody able to reach this screen, and the fix for that is a database
    /// edit — the very thing this endpoint exists to make unnecessary.</para>
    ///
    /// <para><b>Why the security stamp moves.</b> The bearer tokens the Identity API issues carry
    /// the role claims minted at sign-in and are not re-read on each request, so a change here
    /// would otherwise sit unnoticed until the token expired. Refreshing a token checks the
    /// stamp, so bumping it makes every existing session of theirs fall back to sign-in at its
    /// next refresh — for a removed role that is the revocation, and for an added one it is how
    /// they get it without waiting an hour. A token that never refreshes still runs to its
    /// expiry; that is the honest limit, and the help says so.</para>
    /// </remarks>
    [HttpPut("{id:guid}/roles")]
    public async Task<ActionResult<AppUserRolesAdminRecord>> SetRoles(
        Guid id, [FromBody] AdminSetUserRolesRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        // Names are checked against the roles that exist and canonicalised to the stored
        // spelling, so "moderator" lands in Moderator rather than failing or, worse, creating
        // a second role that nothing checks for.
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var known = await db.Roles.AsNoTracking()
            .Where(r => r.Name != null)
            .Select(r => r.Name!)
            .ToListAsync(ct);

        var wanted = new List<string>();
        foreach (var name in (request.Roles ?? []).Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            var match = known.FirstOrDefault(k => string.Equals(k, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return BadRequest($"'{name.Trim()}' is not a site role. Create it under Site Roles first.");
            if (!wanted.Contains(match, StringComparer.OrdinalIgnoreCase))
                wanted.Add(match);
        }

        var before   = (await _userManager.GetRolesAsync(user) ?? []).OrderBy(r => r).ToList();
        var toAdd    = wanted.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = before.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Contains(RoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            if (id == GetCurrentUserId())
                return Conflict("You cannot remove your own SuperAdmin role. Ask another SuperAdmin to do it.");

            var superAdmins = await _userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin);
            if (superAdmins.Count <= 1)
                return Conflict("This is the only SuperAdmin. Make somebody else a SuperAdmin before removing this one.");
        }

        if (toAdd.Count == 0 && toRemove.Count == 0)
            return Ok(new AppUserRolesAdminRecord(id, before));

        // Identity THROWS rather than failing for two ordinary states — a role whose lookup name does not match its
        // name, and a membership that is already there or already gone — and an exception here reaches the page as a
        // 500 with no body, which reads as "the server did not say why" (Ben, 2026-09-15). Named instead.
        try
        {
            if (toRemove.Count > 0)
            {
                var removed = await _userManager.RemoveFromRolesAsync(user, toRemove);
                if (!removed.Succeeded)
                    return BadRequest(string.Join(" ", removed.Errors.Select(e => e.Description)));
            }
            if (toAdd.Count > 0)
            {
                var added = await _userManager.AddToRolesAsync(user, toAdd);
                if (!added.Succeeded)
                    return BadRequest(string.Join(" ", added.Errors.Select(e => e.Description)));
            }

            await _userManager.UpdateSecurityStampAsync(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest($"The account system refused the change: {ex.Message}");
        }

        var after = before.Except(toRemove, StringComparer.OrdinalIgnoreCase)
            .Concat(toAdd)
            .OrderBy(r => r)
            .ToList();

        _ = TryAuditAsync(_auditLog.LogUpdateAsync("AppUserRoles", id,
            new { Roles = before }, new { Roles = after }, GetCurrentUserId(), AppSources.WebApi));

        return Ok(new AppUserRolesAdminRecord(id, after));
    }

    /// <summary>Creates a new application user with an initial password.</summary>
    [HttpPost]
    public async Task<ActionResult<AppUserAdminRecord>> CreateUser(
        [FromBody] AdminCreateUserRequest request, CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName       = string.IsNullOrWhiteSpace(request.UserName) ? request.Email : request.UserName,
            Email          = request.Email,
            DisplayName    = request.DisplayName,
            EmailConfirmed = request.IsEmailConfirmed,
            DateCreated    = DateTime.UtcNow
        };
        user.NormalizedUserName = user.UserName?.ToUpperInvariant();
        user.NormalizedEmail    = user.Email?.ToUpperInvariant();

        // C1 in the 2026-09-06 evaluation: an account created here had Handle = null until the
        // next API restart's backfill ran, and until then it could not be mentioned anywhere.
        // Allocated at creation, like every other path that has no @name to ask for.
        user.Handle = await _handles.AllocateAsync(user.DisplayName, user.Email, ct);

        var result = await _userManager.CreateAsync(user, request.Password);
        // One sentence the New User page shows as it is. This used to be a JSON list of Identity's descriptions, which
        // the website could not show as prose, so it said "the server rejected the request" for every refusal — and a
        // SuperAdmin adding a family member could not tell a space in the username from a taken email (2026-09-15).
        if (!result.Succeeded)
            return BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        if (request.IsSuperAdmin)
            await _userManager.AddToRoleAsync(user, RoleNames.SuperAdmin);

        await TellThemTheirAccountExistsAsync(user);

        return CreatedAtAction(nameof(GetDetail), new { id = user.Id }, _mapper.Map<AppUserAdminRecord>(user));
    }

    /// <summary>
    /// Writes to somebody whose account was just made for them.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is not optional and has no tick box.</b> The account exists under their
    /// address with a password somebody else chose, and until this letter arrives the only person
    /// who can get into it is not its owner. "Should we tell them?" is not a question the person
    /// who made it gets to answer — which is also why the letter itself cannot be declined.</para>
    ///
    /// <para><b>A reset link, not the password.</b> Mailing the password would make two people who
    /// know it rather than one, permanently, in a message that sits in an inbox. The link only
    /// grants the power to replace it, so it hands the account over instead of copying the key.
    /// It is the ordinary reset token, so it expires the way every other one does.</para>
    ///
    /// <para><b>It never fails the creation.</b> The account is made either way; a letter that
    /// could not be sent is logged with the link in it, the same fallback every other flow here
    /// uses, so somebody can still be reached by hand.</para>
    /// </remarks>
    private async Task TellThemTheirAccountExistsAsync(AppUser user)
    {
        if (_mailer is null || string.IsNullOrWhiteSpace(user.Email)) return;

        try
        {
            // BASE64URL, not the raw token. Identity's own /resetPassword decodes what it is
            // given before checking it, and a raw token makes it throw FormatException and answer
            // "invalid token" — so the button in the letter would have failed every single time,
            // for a reason nobody could have diagnosed from the message. Its /forgotPassword
            // endpoint encodes the same way before mailing, which is the shape the reset page and
            // this link both have to match.
            var raw = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
                System.Text.Encoding.UTF8.GetBytes(raw));

            // handover=1 sends the page at the door that accepts an UNCONFIRMED address, which
            // every account made this way has. Without it the button reads "invalid token" every
            // single time, and blames the code for a rule about the address.
            var link = _site.AbsoluteUrl(
                $"/reset-password?email={Uri.EscapeDataString(user.Email)}"
              + $"&code={Uri.EscapeDataString(code)}&handover=1");

            // Named rather than anonymous: "Somebody made you an account" is alarming in a way
            // that a name is not, and the reader has to decide whether they expected this.
            var madeBy = await WhoIsAskingAsync() ?? _site.Name;

            if (!await _mailer.TrySendAccountMadeForYouAsync(user, user.Email!, link, madeBy))
            {
                _log?.LogWarning(
                    "Could not tell {UserId} that an account was made for them. Their handover link "
                  + "is {Link}", user.Id, link);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Could not tell {UserId} that an account was made for them.", user.Id);
        }
    }

    /// <summary>The name of whoever is making the account, for the letter to say.</summary>
    private async Task<string?> WhoIsAskingAsync()
    {
        var actorId = GetCurrentUserId();
        if (actorId == Guid.Empty) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync();
    }

    /// <summary>Updates editable profile fields including audit timestamps.</summary>
    [HttpPut("{id:guid}/profile")]
    public async Task<ActionResult<AppUserAdminRecord>> UpdateProfile(
        Guid id, [FromBody] AdminUpdateUserProfileRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var before = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (before is null) return NotFound();
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.DisplayName          = request.DisplayName;
        user.UserName             = request.UserName ?? user.UserName;
        user.NormalizedUserName   = request.UserName?.ToUpperInvariant() ?? user.NormalizedUserName;
        user.Email                = request.Email ?? user.Email;
        user.NormalizedEmail      = request.Email?.ToUpperInvariant() ?? user.NormalizedEmail;
        user.PhoneNumber          = request.PhoneNumber;
        user.EmailConfirmed       = request.IsEmailConfirmed;

        // An administrator may switch two-factor authentication OFF but never ON.
        //
        // Turning it on from here sets the flag without an authenticator key behind it, so the
        // next sign-in demands a code that account can never produce — an administrator locking
        // somebody out by ticking a box meant to protect them. Enrolment is the account holder's
        // own act, through /api/me/2fa, because only they have the app.
        //
        // Off stays available on purpose: it is the rescue for exactly the person who lost their
        // phone and used up their recovery codes.
        // TwoFactorEnabled is deliberately NOT written here.
        //
        // Ben, 2026-08-20: "Let the end user determine if they want 2FA or not. It is not an
        // administrator-related setting." Right, and the mechanism agrees with the principle:
        // enrolment needs an authenticator app that only the account holder has, so an
        // administrator setting the flag would switch on a second factor nobody could satisfy and
        // lock that person out of their own account. It is theirs to turn on and off, through
        // /api/me/2fa.
        //
        // The field stays on the request record so the shape is unchanged for existing callers;
        // it simply has no effect.
        user.LockoutEnabled       = request.IsLockoutEnabled;
        user.LockoutEnd           = request.LockoutEnd;
        user.DateCreated          = request.DateCreated;
        user.DateUpdated          = request.DateUpdated;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(AppUser), id, before, user, GetCurrentUserId(), AppSources.WebApi));
        return Ok(_mapper.Map<AppUserAdminRecord>(user));
    }
}

public sealed record AdminCreateUserRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? UserName,
    bool IsEmailConfirmed,
    bool IsSuperAdmin);

public sealed record AdminUpdateUserProfileRequest(
    string? DisplayName,
    string? UserName,
    string? Email,
    string? PhoneNumber,
    bool IsEmailConfirmed,
    bool IsTwoFactorEnabled,
    bool IsLockoutEnabled,
    DateTimeOffset? LockoutEnd,
    DateTime DateCreated,
    DateTime? DateUpdated);

