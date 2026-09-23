using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Who is helping at one event, and what they may do (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>The person on the door is not a person with billing rights.</b> A hotel gives the door
/// to a weekend helper who is not a member of the group and never will be; until now the only way
/// to let them scan a pass was to make them somebody who could change the group's settings.</para>
///
/// <para><b>Adding staff is not delegable.</b> Editing the event is what it takes, and a helper
/// cannot pass their own key on — otherwise the door could be handed to anybody by anybody who
/// once held it, which is exactly what this table exists to stop.</para>
///
/// <para><b>An invitation is the same row without an account attached.</b> Not a second table: a
/// helper who has not accepted yet is not a different kind of thing, and two tables carrying the
/// same five flags is two places for them to disagree.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/staff")]
public sealed class HostedEventStaffController : OrgCmsControllerBase
{
    /// <summary>
    /// How long a staff invitation stands.
    /// </summary>
    /// <remarks>
    /// A fortnight, like the guest's. Long enough for somebody to find the letter on a Sunday and
    /// short enough that a forwarded link is not a permanent way into an event.
    /// </remarks>
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    private readonly Services.Access.HostedEventAccess _access;
    private readonly EventGuestMailer _mail;

    public HostedEventStaffController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access,
        EventGuestMailer mail)
        : base(dbFactory, mapper, security)
    { _access = access; _mail = mail; }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>Everybody helping at this event.</summary>
    [HttpGet]
    public async Task<ActionResult<HostedEventStaffListRecord>> Get(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageStaffAsync(userId.Value, orgId, ct)) return Forbid();
        if (!await IsThisEventAsync(db, orgId, eventId, ct)) return NotFound();

        return Ok(await ListAsync(db, eventId, ct));
    }

    // ── adding, changing, removing ───────────────────────────────────────────

    /// <summary>
    /// Adds somebody, or changes what an existing helper may do.
    /// </summary>
    /// <remarks>
    /// <para><b>One door for both</b>, keyed on the person or the address: a host who adds the
    /// same helper twice means "these are the flags now", and two rows for one steward is a
    /// revocation that only half works.</para>
    ///
    /// <para>An address with no account here gets a link. A member is added at once — they already
    /// proved who they are when they joined the group.</para>
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<HostedEventStaffListRecord>> Save(
        Guid orgId, Guid eventId, [FromBody] SaveHostedEventStaffRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageStaffAsync(userId.Value, orgId, ct)) return Forbid();

        var ev = await db.HostedEvents
            .Include(e => e.Organization)
            .Include(e => e.Place)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var email = request.Email?.Trim().ToLowerInvariant();
        var wantsAnAddress = !string.IsNullOrWhiteSpace(email);

        if (request.AppUserId is null && !wantsAnAddress)
            return BadRequest("Choose somebody from the group, or give an email address to invite.");
        if (wantsAnAddress && (!email!.Contains('@') || email.Length > 320))
            return BadRequest("That email address doesn't look right.");

        if (!Grants(request))
            return BadRequest("Say what they may do. A helper who can do nothing is not a helper.");

        // AN ADDRESS IS ALWAYS AN INVITATION, even when it already belongs to somebody here.
        //
        // The first version resolved the address to its account and added them at once, which
        // reads as an efficiency and is actually a venue granting a stranger access to an event
        // without asking them. Being handed other people's names and allergies is a thing to say
        // yes to. Somebody already in the group is added by picking them, which is a different
        // field and a different act.
        var appUserId = request.AppUserId;

        // AND PICKING SOMEBODY MEANS PICKING A MEMBER (2026-09-17 audit).
        //
        // The comment above states the rule and nothing enforced it: request.AppUserId came
        // straight from the body, and the row was written with DateConfirmed = now — live
        // immediately, no acceptance. So a host could name ANY account GUID on the site and hand
        // it the booking board with guests' names, emails and dietary notes, the door, the files,
        // and the event room as a moderator. OrAsStaffAsync matches on the event and the user and
        // nothing else, so the row itself is the grant. EventBookingAlertSettings would then start
        // mailing that stranger the guest list.
        //
        // Non-members are still perfectly welcome as staff — a hotel's weekend helper is the case
        // the feature exists for — but they arrive through the ADDRESS, which is an invitation
        // they have to accept. That is the consent gate, and this path went round it.
        //
        // TourController.SetGuides asks the same question of the same shape of request; its
        // refusal is worded for guides, so this one says it in its own words.
        if (appUserId is { } picked)
        {
            var isMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == picked && m.IsActive, ct);

            if (!isMember)
            {
                var pickedName = await db.AppUsers.AsNoTracking()
                    .Where(u => u.Id == picked)
                    .Select(u => u.DisplayName)
                    .FirstOrDefaultAsync(ct);

                return BadRequest(
                    $"{pickedName ?? "That person"} isn't a member of this group, so they can't be picked "
                    + "as staff. Invite them by email address instead — an invitation is theirs to "
                    + "accept, and being handed other people's names and allergies is a thing to "
                    + "say yes to.");
            }
        }

        var staff = appUserId is { } who
            ? await db.HostedEventStaff.FirstOrDefaultAsync(
                  s => s.HostedEventId == eventId && s.AppUserId == who, ct)
            : await db.HostedEventStaff.FirstOrDefaultAsync(
                  s => s.HostedEventId == eventId && s.Email == email && s.AppUserId == null, ct);

        var now = DateTime.UtcNow;
        var invited = false;

        if (staff is null)
        {
            staff = new HostedEventStaff
            {
                Id = Guid.NewGuid(),
                HostedEventId = eventId,
                AppUserId = appUserId,
                Email = appUserId is null ? email : null,
                DisplayName = Trimmed(request.DisplayName),
                // A member is helping from the moment they are added; an invitation is not.
                DateConfirmed = appUserId is null ? null : now,
                DateCreated = now,
                CreatedByAppUserId = userId.Value,
            };
            db.HostedEventStaff.Add(staff);
            invited = appUserId is null;
        }
        else
        {
            staff.DisplayName = Trimmed(request.DisplayName) ?? staff.DisplayName;
            staff.DateUpdated = now;
            staff.UpdatedByAppUserId = userId.Value;
        }

        staff.RoleLabel = Trimmed(request.RoleLabel);
        staff.SeesBookings = request.SeesBookings;
        staff.Decides = request.Decides;
        staff.RunsTheDoor = request.RunsTheDoor;
        staff.SeesMenus = request.SeesMenus;
        staff.SeesFiles = request.SeesFiles;

        if (invited) Reissue(staff, now);

        // An invitation and the letter carrying its link commit together, or neither does (item
        // 239b): the mailer reads the row back by id, so it is saved, the letter queued, and both
        // written by a second save inside one transaction. A member added directly has no letter
        // and no token, so theirs is one ordinary save.
        if (invited)
            await SaveInOneTransactionAsync(db, () => _mail.SendStaffInviteAsync(db, staff.Id, ct), ct);
        else
            await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, eventId, ct));
    }

    /// <summary>Sends an outstanding invitation again, with a fresh link.</summary>
    /// <remarks>
    /// The commonest thing that goes wrong with an invitation is that it never arrived. A new
    /// token each time, so a link that was forwarded to the wrong person stops working the moment
    /// the right one is sent.
    /// </remarks>
    [HttpPost("{staffId:guid}/resend")]
    public async Task<ActionResult<HostedEventStaffListRecord>> Resend(
        Guid orgId, Guid eventId, Guid staffId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageStaffAsync(userId.Value, orgId, ct)) return Forbid();
        if (!await IsThisEventAsync(db, orgId, eventId, ct)) return NotFound();

        var staff = await db.HostedEventStaff.FirstOrDefaultAsync(
            s => s.Id == staffId && s.HostedEventId == eventId, ct);
        if (staff is null) return NotFound();
        if (staff.DateConfirmed is not null)
            return Conflict("They have already accepted — there is nothing to send.");

        // Asked BEFORE reissuing. A fresh token kills the link in the letter they already have, so
        // rotating it when there is no way to send the new one leaves them with nothing that works
        // — which is what happened when this was asked afterwards.
        if (!_mail.IsConfigured)
            return Conflict("This site has no outgoing mail set up, so nothing was sent.");

        Reissue(staff, DateTime.UtcNow);
        staff.UpdatedByAppUserId = userId.Value;

        // The new token and the letter carrying it commit together (item 239b), so a failure
        // leaves the old link working rather than replaced by one nobody was sent. Nothing to send
        // is a failure too: it throws inside the transaction so the reissue is rolled back with it.
        try
        {
            await SaveInOneTransactionAsync(db, async () =>
            {
                if (!await _mail.SendStaffInviteAsync(db, staff.Id, ct))
                    throw new NothingToSendException();
            }, ct);
        }
        catch (NothingToSendException)
        {
            return Conflict("There was no address to send the invitation to, so nothing was sent.");
        }

        return Ok(await ListAsync(db, eventId, ct));
    }

    /// <summary>
    /// Takes somebody off this event.
    /// </summary>
    /// <remarks>
    /// Deleted rather than flagged: a helper who is no longer helping should not be a row anybody
    /// has to read past, and what they did at the door is recorded on the check-ins rather than
    /// here. It takes effect on their next request, because every check reads this table.
    /// </remarks>
    [HttpDelete("{staffId:guid}")]
    public async Task<ActionResult<HostedEventStaffListRecord>> Remove(
        Guid orgId, Guid eventId, Guid staffId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageStaffAsync(userId.Value, orgId, ct)) return Forbid();
        if (!await IsThisEventAsync(db, orgId, eventId, ct)) return NotFound();

        var staff = await db.HostedEventStaff.FirstOrDefaultAsync(
            s => s.Id == staffId && s.HostedEventId == eventId, ct);
        if (staff is null) return NotFound();

        db.HostedEventStaff.Remove(staff);
        await db.SaveChangesAsync(ct);

        return Ok(await ListAsync(db, eventId, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private static bool Grants(SaveHostedEventStaffRequest r)
        => r.SeesBookings || r.Decides || r.RunsTheDoor || r.SeesMenus || r.SeesFiles;

    private static void Reissue(HostedEventStaff staff, DateTime now)
    {
        staff.Token = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        staff.DateExpires = now.Add(InviteLifetime);
    }

    private static Task<bool> IsThisEventAsync(
        BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
        => db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);

    /// <summary>Everybody helping, accepted first and then who is still to answer.</summary>
    public static async Task<HostedEventStaffListRecord> ListAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var rows = await db.HostedEventStaff.AsNoTracking()
            .Include(s => s.AppUser)
            .Where(s => s.HostedEventId == eventId)
            .ToListAsync(ct);

        return new HostedEventStaffListRecord(
            eventId,
            [.. rows
                .OrderByDescending(s => s.DateConfirmed is not null)
                .ThenBy(s => NameOf(s), StringComparer.OrdinalIgnoreCase)
                .Select(s => new HostedEventStaffRecord(
                    s.Id,
                    s.AppUserId,
                    NameOf(s),
                    s.AppUserId is null ? s.Email : null,
                    s.RoleLabel,
                    s.SeesBookings,
                    s.Decides,
                    s.RunsTheDoor,
                    s.SeesMenus,
                    s.SeesFiles,
                    Accepted: s.DateConfirmed is not null,
                    s.DateExpires,
                    s.DateCreated))]);
    }

    /// <summary>What to call somebody, whichever of the three names exists.</summary>
    private static string NameOf(HostedEventStaff staff)
        => staff.AppUser?.DisplayName is { Length: > 0 } account ? account
         : staff.DisplayName is { Length: > 0 } given ? given
         : staff.Email is { Length: > 0 } address ? address
         : "Somebody";

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;

    /// <summary>
    /// The mailer had nothing to send, raised inside a transaction so that what it was about is
    /// rolled back with it rather than committed on its own.
    /// </summary>
    private sealed class NothingToSendException : Exception;
}
