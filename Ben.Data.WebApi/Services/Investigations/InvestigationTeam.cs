using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Investigations;

/// <summary>
/// Who may be put on a visit's team, who may be taken off it, and taking them off (10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>One set of rules for both doors.</b> A team is filled from the case page
/// (<c>InvestigationController</c>, nested under a case) and from the group's Investigations page
/// (<c>OrgInvestigationsController</c>, which also serves visits with no case). Until 10/16/2026
/// only the second checked anything about the person being added, and neither cleared duties on
/// removal, so the same team could be filled by different rules depending on which screen was
/// used. Both controllers now ask here; the gate on the CALLER (who manages the visit) stays in
/// each controller, because the two prove the visit belongs to the group differently.</para>
///
/// <para>Nothing here saves. The caller owns the transaction and the audit row.</para>
/// </remarks>
public static class InvestigationTeam
{
    /// <summary>What kind of no it is, so the controller answers with the right status.</summary>
    public enum RefusalKind
    {
        /// <summary>The person is not somebody who can be on a team here. A 400.</summary>
        NotEligible,

        /// <summary>The request contradicts the team as it stands. A 409.</summary>
        Conflict,
    }

    /// <summary>A refusal and the sentence that explains it to the person who asked.</summary>
    public sealed record Refusal(RefusalKind Kind, string Sentence)
    {
        /// <summary>The response to send. Always carries the sentence, never a bare status.</summary>
        public ObjectResult ToResult() => Kind == RefusalKind.NotEligible
            ? new BadRequestObjectResult(Sentence)
            : new ConflictObjectResult(Sentence);
    }

    /// <summary>
    /// Why this person cannot be put on this visit's team, or null when they can.
    /// </summary>
    /// <remarks>
    /// <para><b>An active member with an open account, who can take part.</b> Being on the team is
    /// what lets somebody check in and file an account, and both are refused to anybody who is
    /// not an active member — so a stranger, a former member or a closed account on a team is a
    /// row that can do nothing. A Viewer can only look, so the same.</para>
    ///
    /// <para><b>Once each.</b> The table's unique index on (investigation, person) answers a second
    /// add with a 500 in SQL Server and is not enforced at all by the InMemory provider the tests
    /// use; checked here so both say the same thing, in words.</para>
    /// </remarks>
    public static async Task<Refusal?> WhyCannotJoinAsync(
        BenDataContext db, Guid organizationId, Guid investigationId, Guid appUserId, CancellationToken ct)
    {
        // A closed account keeps its membership row so its work stays attributed (see the group
        // roster), so "active member" here also means the account is still open.
        var role = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.AppUserId == appUserId && m.IsActive)
            .Join(db.AppUsers.AsNoTracking().Where(u => u.DateClosed == null),
                m => m.AppUserId, u => u.Id, (m, u) => (OrganizationMemberRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        if (role is null)
            return new(RefusalKind.NotEligible,
                "That person isn't an active member of this group, so they can't be put on its team.");

        if (role == OrganizationMemberRole.Viewer)
            return new(RefusalKind.NotEligible,
                "That person is a viewer in this group. Viewers can look but can't take part, "
              + "so they can't be put on a team. Change their role first if they're coming.");

        if (await db.InvestigationAttendees.AsNoTracking()
                .AnyAsync(a => a.InvestigationId == investigationId && a.AppUserId == appUserId, ct))
        {
            var name = await NameAsync(db, appUserId, ct);
            return new(RefusalKind.Conflict, $"{name} is already on this team.");
        }

        return null;
    }

    /// <summary>
    /// Why this person must stay on the team, or null when they may be taken off.
    /// </summary>
    /// <remarks>
    /// Who attended is evidence, and so is what they said they saw. Deleting the row would quietly
    /// rewrite both — the roster would no longer show them, and their account would sit on a visit
    /// whose team does not include them — so the refusal says what to do instead.
    /// </remarks>
    public static async Task<Refusal?> WhyCannotLeaveAsync(
        BenDataContext db, InvestigationAttendee attendee, CancellationToken ct)
    {
        if (attendee.DidAttend == true)
            return new(RefusalKind.Conflict,
                $"{await NameAsync(db, attendee.AppUserId, ct)} is recorded as having been there, so they "
              + "stay on the team. Untick Attended first if that was a mistake.");

        if (await db.InvestigationFindings.AsNoTracking()
                .AnyAsync(f => f.InvestigationId == attendee.InvestigationId && f.AppUserId == attendee.AppUserId, ct))
            return new(RefusalKind.Conflict,
                $"{await NameAsync(db, attendee.AppUserId, ct)} has written up what they saw, so they stay on the team.");

        return null;
    }

    /// <summary>
    /// Takes the row off the team, with the duties it holds on the visit. Does not save.
    /// </summary>
    /// <remarks>
    /// The duties first: the assignment table's key to the team row is NoAction, so SQL Server
    /// refuses to delete a team row that still holds one.
    /// </remarks>
    public static async Task RemoveAsync(BenDataContext db, InvestigationAttendee attendee, CancellationToken ct)
    {
        var duties = await db.InvestigationDutyAssignments
            .Where(x => x.InvestigationAttendeeId == attendee.Id)
            .ToListAsync(ct);
        db.InvestigationDutyAssignments.RemoveRange(duties);
        db.InvestigationAttendees.Remove(attendee);
    }

    private static async Task<string> NameAsync(BenDataContext db, Guid appUserId, CancellationToken ct)
        => await db.AppUsers.AsNoTracking()
               .Where(u => u.Id == appUserId)
               .Select(u => u.DisplayName)
               .FirstOrDefaultAsync(ct)
           ?? "That person";
}
