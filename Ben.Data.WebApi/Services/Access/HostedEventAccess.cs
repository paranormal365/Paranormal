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

    public HostedEventAccess(IOrganizationSecurityService security) => _security = security;

    /// <summary>See the event and its plan. Any member may.</summary>
    /// <remarks>
    /// Knowing what an event is offering is not confidential — the public page says most of it.
    /// What is confidential is who has asked for it, which is a different question below.
    /// </remarks>
    public Task<bool> CanReadEventAsync(Guid userId, Guid orgId, CancellationToken ct)
        => AllowedAsync(userId, orgId, OrganizationSecurityTable.HostedEvent,
                        OrganizationSecurityAction.Read, ct);

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
        => OrAsStaffAsync(CanReadBookingsAsync(userId, orgId, ct),
                          userId, eventId, db, s => s.SeesBookings || s.Decides, ct);

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
        => OrAsStaffAsync(CanRunTheDoorAsync(userId, orgId, ct),
                          userId, eventId, db, s => s.RunsTheDoor, ct);

    /// <summary>Read and write this event's menus, as a member OR as somebody helping at it.</summary>
    public Task<bool> CanEditMenusAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => OrAsStaffAsync(CanEditEventAsync(userId, orgId, ct),
                          userId, eventId, db, s => s.SeesMenus, ct);

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

    private Task<bool> AllowedAsync(
        Guid userId, Guid orgId,
        OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken ct)
        => _security.HasAccessAsync(userId, orgId, table, action, ct);
}
