using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Somebody saying yes to helping at an event (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>Anonymous, because the person invited usually has no account.</b> A hotel's weekend
/// steward is invited by the venue they already work for; asking them to make an account first,
/// out of nowhere, to accept a job they have already agreed to in person, is the wrong way
/// round.</para>
///
/// <para><b>Reading the invitation and accepting it are two calls</b>, and only the second
/// changes anything. A link prefetched by a mail client or a scanner must not enrol anybody: it
/// takes a person pressing a button on a page that first told them what they were agreeing
/// to.</para>
///
/// <para><b>Accepting creates a passwordless account</b>, exactly as the guest's own email door
/// does — the address was proved by the click, and the door screen needs somebody to be signed in
/// as. Setting a password later is an upgrade, not a requirement.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/hosted-event-staff")]
public sealed class PublicHostedEventStaffController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly UserManager<AppUser> _users;
    private readonly Services.UserHandleService _handles;
    private readonly ILogger<PublicHostedEventStaffController> _log;

    public PublicHostedEventStaffController(
        IDbContextFactory<BenDataContext> db,
        UserManager<AppUser> users,
        Services.UserHandleService handles,
        ILogger<PublicHostedEventStaffController> log)
    { _db = db; _users = users; _handles = handles; _log = log; }

    /// <summary>What this invitation is, before anybody accepts it.</summary>
    [HttpGet("{token}")]
    public async Task<ActionResult<HostedEventStaffInviteRecord>> Get(
        string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var staff = await LoadAsync(db, token, ct);
        if (staff is null) return NotFound();

        return Ok(Describe(staff));
    }

    /// <summary>
    /// Says yes, and attaches the invitation to an account.
    /// </summary>
    /// <remarks>
    /// <para><b>The token is single-use and cleared here</b>, so a forwarded letter cannot enrol a
    /// second person into somebody else's job.</para>
    ///
    /// <para><b>Whoever is signed in wins over the address.</b> Somebody already signed in as
    /// themselves who clicks a link sent to an old address means "me" — attaching it to a
    /// second, half-made account would leave them helping as a person they never use.</para>
    /// </remarks>
    [HttpPost("{token}/accept")]
    public async Task<ActionResult<HostedEventStaffInviteRecord>> Accept(
        string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var staff = await LoadAsync(db, token, ct);
        if (staff is null) return NotFound();

        if (staff.DateConfirmed is not null) return Ok(Describe(staff));

        var user = GetCurrentUserIdOrNull() is { } signedIn
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == signedIn, ct)
            : null;

        if (user is null && staff.Email is { Length: > 0 } email)
        {
            user = await _users.FindByEmailAsync(email);

            if (user is null)
            {
                user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    UserName = email,
                    NormalizedEmail = email.ToUpperInvariant(),
                    NormalizedUserName = email.ToUpperInvariant(),
                    // Proved by clicking a link sent to it, which is what confirmation means.
                    EmailConfirmed = true,
                    DisplayName = staff.DisplayName ?? email.Split('@')[0],
                    Handle = await _handles.AllocateAsync(staff.DisplayName, email, ct),
                    DateCreated = DateTime.UtcNow,
                };

                var created = await _users.CreateAsync(user);
                if (!created.Succeeded)
                {
                    _log.LogWarning("Could not create an account for a helper: {Errors}",
                        string.Join("; ", created.Errors.Select(e => e.Description)));
                    return BadRequest("That account could not be created.");
                }
            }
        }

        if (user is null)
            return BadRequest("This invitation has no address on it. Sign in first, then open the "
                            + "link again.");

        // ALREADY HELPING? Then this invitation is a duplicate of a row that already works, and
        // the right answer is to fold it in rather than to leave two rows and a unique index
        // refusing the save.
        var already = await db.HostedEventStaff.FirstOrDefaultAsync(
            s => s.HostedEventId == staff.HostedEventId && s.AppUserId == user.Id, ct);

        if (already is not null && already.Id != staff.Id)
        {
            already.SeesBookings |= staff.SeesBookings;
            already.Decides |= staff.Decides;
            already.RunsTheDoor |= staff.RunsTheDoor;
            already.SeesMenus |= staff.SeesMenus;
            already.SeesFiles |= staff.SeesFiles;
            already.RoleLabel ??= staff.RoleLabel;
            already.DateUpdated = DateTime.UtcNow;

            db.HostedEventStaff.Remove(staff);
            await db.SaveChangesAsync(ct);

            return Ok(Describe(already, alreadyAccepted: true,
                              accountHasNoPassword: !await _users.HasPasswordAsync(user)));
        }

        staff.AppUserId = user.Id;
        staff.Email = null;
        staff.Token = null;
        staff.DateConfirmed = DateTime.UtcNow;
        staff.DateUpdated = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(Describe(staff, accountHasNoPassword: !await _users.HasPasswordAsync(user)));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private static async Task<HostedEventStaff?> LoadAsync(
        BenDataContext db, string token, CancellationToken ct)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        var staff = await db.HostedEventStaff
            .Include(s => s.HostedEvent).ThenInclude(e => e.Organization)
            .Include(s => s.HostedEvent).ThenInclude(e => e.Place)
            .FirstOrDefaultAsync(s => s.Token == trimmed, ct);

        // An expired link is not found rather than refused: there is nothing the reader can do
        // with the difference, and the venue can send another.
        return staff is { DateExpires: { } expires } && expires < DateTime.UtcNow ? null : staff;
    }

    private static HostedEventStaffInviteRecord Describe(
        HostedEventStaff staff, bool alreadyAccepted = false, bool accountHasNoPassword = false)
        => new(
            staff.HostedEventId,
            staff.HostedEvent?.Name ?? "An event",
            staff.HostedEvent?.Organization?.Name ?? "A group",
            staff.HostedEvent?.Place?.Name,
            staff.HostedEvent?.StartsOn ?? default,
            staff.HostedEvent?.EndsOn ?? default,
            staff.RoleLabel,
            EventGuestMailer.WhatTheyCanDo(staff),
            AlreadyAccepted: alreadyAccepted || staff.DateConfirmed is not null,
            AccountHasNoPassword: accountHasNoPassword);
}
