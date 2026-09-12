using Ben.Data.Common.Enums;
using Ben.Service.RepositoryService.GenericInterfaces;

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

    private Task<bool> AllowedAsync(
        Guid userId, Guid orgId,
        OrganizationSecurityTable table, OrganizationSecurityAction action, CancellationToken ct)
        => _security.HasAccessAsync(userId, orgId, table, action, ct);
}
