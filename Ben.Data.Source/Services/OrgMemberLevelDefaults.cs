using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;

namespace Ben.Data.Source.Services;

/// <summary>
/// The member-title ladder every new organization starts with (item 157).
/// </summary>
/// <remarks>
/// <para>Same pattern and reasoning as <see cref="OrgCalendarDefaults"/>: titles are per-org, so
/// no global seeder can reach them, and a group whose ladder starts empty has a feature it
/// cannot discover. Every organization-creation door stamps these on the same SaveChanges as
/// the organization itself; the owner renames, reorders, or retires rungs from group settings.</para>
///
/// <para>The ladder deliberately ends at Lead Investigator (Ben, 2026-08-23). "Case Manager" is
/// not a rung — it is a permission role (item 156). Titles are seniority and grant nothing;
/// "Probationary" is a label, never a restriction (a genuinely restricted newcomer is the
/// Viewer membership kind).</para>
/// </remarks>
public static class OrgMemberLevelDefaults
{
    /// <summary>
    /// Lowest first: SortOrder is what eligibility comparisons (item 158) read, and what the
    /// title-by-duty matrix (item 160) is ordered by.
    /// </summary>
    /// <remarks>
    /// "Associate" rather than "Probationary" (Ben, 2026-09-04). The bottom rung is where somebody
    /// new stands, and naming it after a probation period reads as a warning rather than a
    /// welcome. Groups created before this keep the name they have; renaming a rung is theirs to do.
    /// </remarks>
    private static readonly string[] _ladder =
    [
        "Associate",
        "Junior Investigator",
        "Investigator",
        "Senior Investigator",
        "Lead Investigator",
    ];

    /// <summary>
    /// Stages the default ladder for <paramref name="organizationId"/> and hands the rungs back,
    /// so the duty matrix (item 160) can be seeded against them. Does not save — the caller's
    /// SaveChangesAsync commits it with the organization itself.
    /// </summary>
    public static List<OrganizationMemberLevel> AddDefaultLevels(
        BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        var now = DateTime.UtcNow;
        var added = new List<OrganizationMemberLevel>(_ladder.Length);

        for (var i = 0; i < _ladder.Length; i++)
        {
            var level = new OrganizationMemberLevel
            {
                OrganizationId = organizationId,
                Name = _ladder[i],
                SortOrder = i + 1,
                IsActive = true,
                DateCreated = now,
                CreatedByAppUserId = createdByAppUserId,
            };
            db.OrganizationMemberLevels.Add(level);
            added.Add(level);
        }

        return added;
    }
}
