using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>
/// Whether a member's title lets them hold a duty (item 160), and the sentence to show when it
/// does not.
/// </summary>
/// <remarks>
/// <para><b>Two rules, and which applies is decided by the data.</b> A duty whose matrix has rows
/// is answered by the matrix: eligible if the member's title is one of them. A duty with no rows
/// falls back to <c>MinimumMemberLevelId</c>, the single-threshold rule item 158 shipped. So a
/// group that has never opened the matrix behaves exactly as before, nothing had to be backfilled,
/// and the moment a group ticks one box that duty starts answering the new way.</para>
///
/// <para><b>The verdict is advice, not a wall.</b> The assignment door offers an override that is
/// recorded on the assignment. The senior calls in sick and the capable junior steps up; a hard
/// refusal would push the group back to organising by text message. That was true of the minimum
/// and stays true of the matrix.</para>
/// </remarks>
public static class DutyEligibility
{
    /// <param name="Eligible">Whether the title clears the duty's rule.</param>
    /// <param name="Refusal">What to tell the person, naming the gap. Null when eligible.</param>
    public sealed record Verdict(bool Eligible, string? Refusal);

    /// <summary>Whether <paramref name="memberAppUserId"/> may hold <paramref name="duty"/>.</summary>
    public static async Task<Verdict> CheckAsync(
        BenDataContext db, InvestigationDuty duty, Guid organizationId,
        Guid memberAppUserId, CancellationToken ct)
    {
        var title = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId
                     && m.AppUserId == memberAppUserId && m.IsActive)
            .Select(m => m.MemberLevel)
            .FirstOrDefaultAsync(ct);

        var openTo = await db.InvestigationDutyEligibilities.AsNoTracking()
            .Where(e => e.InvestigationDutyId == duty.Id)
            .Select(e => new { e.OrganizationMemberLevelId, e.OrganizationMemberLevel.Name, e.OrganizationMemberLevel.SortOrder })
            .OrderBy(e => e.SortOrder)
            .ToListAsync(ct);

        if (openTo.Count > 0)
        {
            if (title is not null && openTo.Any(o => o.OrganizationMemberLevelId == title.Id))
                return new Verdict(true, null);

            var names = string.Join(", ", openTo.Select(o => o.Name));
            return new Verdict(false,
                $"“{duty.Name}” is open to {names}; "
              + $"{(title is null ? "this member has no title yet" : $"this member is {title.Name}")}. "
              + "Assign anyway to confirm the exception.");
        }

        // No matrix for this duty: the single threshold, unchanged from item 158.
        var minimum = duty.MinimumMemberLevel
            ?? (duty.MinimumMemberLevelId is { } id
                ? await db.OrganizationMemberLevels.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct)
                : null);
        if (minimum is null) return new Verdict(true, null);

        if (title is not null && title.SortOrder >= minimum.SortOrder)
            return new Verdict(true, null);

        return new Verdict(false,
            $"This duty asks for {minimum.Name} or above; "
          + $"{(title is null ? "this member has no title yet" : $"this member is {title.Name}")}. "
          + "Assign anyway to confirm the exception.");
    }
}
