using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>
/// Who may do what with a hosted event, its bookings and its door (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Every hosted endpoint asked the same question — "may you change this
/// group's settings?" — because that was the only key wired up when the feature began. The
/// consequence is that the person who arranges the rooms has to be somebody who can change the
/// group's billing, and, worse, that ANY member could read the booking board: guests' email
/// addresses and, until it was moved, their dietary notes. Those are two different jobs and two
/// different confidences.</para>
///
/// <para><b>Four questions, because there are four jobs.</b> Arranging an event is not deciding who
/// comes; deciding who comes is not standing at the door with a scanner; and none of the three is
/// spending the group's money. A hotel gives the door to a weekend helper who is not a member of
/// anything, and giving them the billing screen to do it would be absurd.</para>
///
/// <para><b>Publishing still needs the settings key</b>, and that is deliberate: it is the one act
/// that spends a credit. A guard asserts no other hosted endpoint reads that key, so the coupling
/// cannot creep back.</para>
///
/// <para><b>Owner, Administrator and SuperAdmin pass everything.</b> Below them the Events
/// permission area decides, and it fails closed — a band that does not include the area grants
/// nothing, which is why decision 10 puts the area on every band.</para>
///
/// <para><b>And from phase 7, one event's own staff.</b> A hotel's weekend helper is not a member
/// of the group and never will be, so the group's roles have nothing to say about them; the event
/// says instead, for that event only. The event-aware overloads below ask the group first and the
/// event's staff list second — <b>staff rows only ever ADD</b>. Nothing here can take a permission
/// away from somebody who has it through the group, because being handed a scanner must not
/// demote an owner.</para>
/// </remarks>
public sealed class HostedEventAccess
{
    private readonly IOrganizationSecurityService _security;
    private readonly IDbContextFactory<BenDataContext>? _db;

    /// <param name="db">
    /// Optional, and only ever used to answer "is this person a member" for
    /// <see cref="CanReadEventAsync"/>. Optional rather than required because a dozen test
    /// fixtures construct this class directly; without it the membership fallback is skipped and
    /// the grant decides, which is how this behaved before 2026-09-17.
    /// </param>
    public HostedEventAccess(
        IOrganizationSecurityService security, IDbContextFactory<BenDataContext>? db = null)
    { _security = security; _db = db; }

    /// <summary>See the event and its plan. Any member may.</summary>
    /// <remarks>
    /// <para>Knowing what an event is offering is not confidential — the public page says most of
    /// it. What is confidential is who has asked for it, which is a different question below.</para>
    ///
    /// <para><b>"Any member may" now means it.</b> Until the 2026-09-17 audit this asked only for
    /// the HostedEvent Read grant, which an ordinary member does not hold — and which no custom
    /// role can hold either while the Events permission area is off a band. Meanwhile
    /// <c>HostedEventController</c>'s own list and detail ask plain membership. So the event page
    /// loaded and every sub-surface behind it — the venue, the gallery, the sessions, the
    /// afterwards — refused the same person. The doc comment and the code disagreed, and the doc
    /// was the one describing what anybody would expect.</para>
    ///
    /// <para>The grant is still asked first, so this only ever ADDS: nothing that passed before
    /// stops passing, and a non-member is still refused.</para>
    /// </remarks>
    public async Task<bool> CanReadEventAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        if (await AllowedAsync(userId, orgId, OrganizationSecurityTable.HostedEvent,
                               OrganizationSecurityAction.Read, ct))
            return true;

        if (_db is null) return false;

        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
    }

    /// <summary>Change the event, its dates, its plan, its blocks and its menus.</summary>
    public Task<bool> CanEditEventAsync(Guid userId, Guid orgId, CancellationToken ct)
        => AllowedAsync(userId, orgId, OrganizationSecurityTable.HostedEvent,
                        OrganizationSecurityAction.Update, ct);

    /// <summary>
    /// See the board with guests' names, addresses and dietary notes.
    /// </summary>
    /// <remarks>
    /// The one that used to be open to every member. A dietary note is a health disclosure somebody
    /// made to a venue so they would not be poisoned, and an email address is theirs; neither is a
    /// thing a group's whole membership needs.
    /// </remarks>
    public Task<bool> CanReadBookingsAsync(Guid userId, Guid orgId, CancellationToken ct)
        => AllowedAsync(userId, orgId, OrganizationSecurityTable.EventBooking,
                        OrganizationSecurityAction.Read, ct);

    /// <summary>Confirm, turn down, release, edit, invite, book on behalf, issue passes.</summary>
    public Task<bool> CanDecideBookingsAsync(Guid userId, Guid orgId, CancellationToken ct)
        => AllowedAsync(userId, orgId, OrganizationSecurityTable.EventBooking,
                        OrganizationSecurityAction.Update, ct);

    /// <summary>
    /// Run the door: scan a pass, mark somebody arrived, see tonight's dietary FLAGS.
    /// </summary>
    /// <remarks>
    /// Flags and not notes, and only for tonight. Somebody letting people in needs to know that
    /// table four has a nut allergy; they do not need the guest's address, their phone number or
    /// what they wrote about last year.
    /// </remarks>
    public Task<bool> CanRunTheDoorAsync(Guid userId, Guid orgId, CancellationToken ct)
        => AllowedAsync(userId, orgId, OrganizationSecurityTable.EventCheckIn,
                        OrganizationSecurityAction.Create, ct);

    // ── and what one event's own staff may do (phase 7) ──────────────────────

    /// <summary>See this event's board, as a member OR as somebody helping at it.</summary>
    public Task<bool> CanReadBookingsAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => OrAsTheVenueAsync(
               OrAsStaffAsync(CanReadBookingsAsync(userId, orgId, ct),
                              userId, eventId, db, s => s.SeesBookings || s.Decides, ct),
               userId, eventId, db, OrganizationSecurityTable.EventBooking, OrganizationSecurityAction.Read, ct);

    /// <summary>Decide this event's bookings, as a member OR as somebody helping at it.</summary>
    public Task<bool> CanDecideBookingsAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => OrAsStaffAsync(CanDecideBookingsAsync(userId, orgId, ct),
                          userId, eventId, db, s => s.Decides, ct);

    /// <summary>
    /// Run this event's door, as a member OR as somebody helping at it.
    /// </summary>
    /// <remarks>
    /// The reason the whole staff table exists. Until now the only way to let a weekend helper
    /// scan a pass was to make them somebody who could change the group's settings.
    /// </remarks>
    public Task<bool> CanRunTheDoorAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => OrAsTheVenueAsync(
               OrAsStaffAsync(CanRunTheDoorAsync(userId, orgId, ct),
                              userId, eventId, db, s => s.RunsTheDoor, ct),
               userId, eventId, db, OrganizationSecurityTable.EventCheckIn, OrganizationSecurityAction.Create, ct);

    /// <summary>Read and write this event's menus, as a member OR as somebody helping at it.</summary>
    public Task<bool> CanEditMenusAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => OrAsStaffAsync(CanEditEventAsync(userId, orgId, ct),
                          userId, eventId, db, s => s.SeesMenus, ct);

    /// <summary>Add, change and remove this event's files, as a member OR as somebody helping with its files (phase 11).</summary>
    public Task<bool> CanManageFilesAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => OrAsStaffAsync(CanEditEventAsync(userId, orgId, ct),
                          userId, eventId, db, s => s.SeesFiles, ct);

    /// <summary>Say who is helping at this event: the group's own people only, never staff.</summary>
    /// <remarks>
    /// Deliberately not delegable. A helper who could add helpers could hand the door to anybody,
    /// and the whole point of the table is that the person on the door is trusted with the door
    /// and nothing else.
    /// </remarks>
    public Task<bool> CanManageStaffAsync(Guid userId, Guid orgId, CancellationToken ct)
        => CanEditEventAsync(userId, orgId, ct);

    /// <summary>
    /// The group's answer, and failing that the event's own staff list.
    /// </summary>
    /// <remarks>
    /// <para>The group is asked FIRST and short-circuits: it is a cached permission check, while
    /// this is a query, and the common case by far is somebody who is already allowed.</para>
    ///
    /// <para><b>An unaccepted invitation grants nothing</b>, and needs no rule of its own to say
    /// so: it has no <c>AppUserId</c>, and nobody's id is null. Anything past its expiry is out
    /// too, so a token that was never clicked stops being an open door.</para>
    /// </remarks>
    private async Task<bool> OrAsStaffAsync(
        Task<bool> asAMember, Guid userId, Guid eventId, BenDataContext db,
        Func<Source.Entities.HostedEventStaff, bool> grants, CancellationToken ct)
    {
        if (await asAMember) return true;

        var staff = await db.HostedEventStaff.AsNoTracking()
            .Where(s => s.HostedEventId == eventId
                     && s.AppUserId == userId
                     && s.DateConfirmed != null)
            .ToListAsync(ct);

        return staff.Any(grants);
    }

    /// <summary>
    /// And failing both, the venue's own people, when the venue lent them (phase 9).
    /// </summary>
    /// <remarks>
    /// <para>Only when the venue said <b>our staff may help</b> on the yes this event rests on, only
    /// while that yes stands, and only as far as the venue's own group trusts them — the hotel's
    /// night porter sees the list because the hotel lets him see its own bookings, not because he
    /// works in the building.</para>
    ///
    /// <para><b>Reading and the door, never deciding.</b> Whether a party comes to somebody else's
    /// weekend is the organizer's call; the venue lent a building, not a veto over the guest list.
    /// There is deliberately no overload of the deciding question here.</para>
    /// </remarks>
    private async Task<bool> OrAsTheVenueAsync(
        Task<bool> soFar, Guid userId, Guid eventId, BenDataContext db,
        OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken ct)
    {
        if (await soFar) return true;

        var grant = await db.HostedEvents.AsNoTracking()
            .Where(e => e.Id == eventId && e.VenueGrantId != null)
            .Select(e => e.VenueGrant)
            .FirstOrDefaultAsync(ct);

        if (grant is not { RevokedUtc: null, AllowStaff: true }) return false;

        return await AllowedAsync(userId, grant.VenueOrganizationId, table, action, ct);
    }

    private Task<bool> AllowedAsync(
        Guid userId, Guid orgId,
        OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken ct)
        => _security.HasAccessAsync(userId, orgId, table, action, ct);
}
