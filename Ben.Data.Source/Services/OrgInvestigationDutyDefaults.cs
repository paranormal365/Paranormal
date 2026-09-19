using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;

namespace Ben.Data.Source.Services;

/// <summary>
/// The investigation duties every new organization starts with (item 158).
/// </summary>
/// <remarks>
/// Same pattern as <see cref="OrgCalendarDefaults"/> and <see cref="OrgMemberLevelDefaults"/>:
/// per-org, stamped at every creation door on the same SaveChanges, fully editable afterwards.
/// Lead Investigator is the one single-holder duty — a visit has one lead — and none of the
/// seeds carries a minimum title, deliberately: eligibility thresholds are a choice each group
/// makes, never a surprise the platform ships.
/// </remarks>
public static class OrgInvestigationDutyDefaults
{
    /// <summary>
    /// The duties every new group starts with, and what holding each one confers (item 160).
    /// </summary>
    /// <remarks>
    /// <b>Equipment is two duties on purpose.</b> Ben's worked example distinguishes assisting
    /// with the equipment from running it — a junior may do the first long before the second — and
    /// a single "Equipment" duty cannot say that. The matrix below opens them to different rungs.
    /// </remarks>
    private static readonly (string Name, bool SingleHolder, InvestigationDutyCapabilities Capabilities)[] _defaults =
    [
        // The visit's point of contact, and the one duty that may hand the others out. Not
        // scheduling: Ben's example is explicit that the lead of a visit does not move it.
        ("Lead Investigator",   true,  InvestigationDutyCapabilities.PointOfContact
                                     | InvestigationDutyCapabilities.MayAssignDuties),
        ("Equipment",           false, InvestigationDutyCapabilities.None),
        ("Equipment Assist",    false, InvestigationDutyCapabilities.None),
        ("Evidence Collection", false, InvestigationDutyCapabilities.None),
        ("Documentation",       false, InvestigationDutyCapabilities.None),
    ];

    /// <summary>
    /// Which titles each duty starts open to, by name — the matrix a new group gets rather than an
    /// empty grid (item 160). Every cell is listed: the matrix is a matrix, so "and any rung above
    /// it" is never implied.
    /// </summary>
    private static readonly (string Duty, string[] Titles)[] _defaultMatrix =
    [
        ("Documentation",       ["Associate", "Junior Investigator", "Investigator", "Senior Investigator", "Lead Investigator"]),
        ("Equipment Assist",    ["Associate", "Junior Investigator", "Investigator", "Senior Investigator", "Lead Investigator"]),
        ("Evidence Collection", ["Junior Investigator", "Investigator", "Senior Investigator", "Lead Investigator"]),
        ("Equipment",           ["Investigator", "Senior Investigator", "Lead Investigator"]),
        ("Lead Investigator",   ["Senior Investigator", "Lead Investigator"]),
    ];

    /// <summary>The seeded name whose assignment writes through to <c>InvestigationAttendee.IsLead</c>.</summary>
    public const string LeadDutyName = "Lead Investigator";

    /// <summary>
    /// Stages the default duties and hands them back, so the matrix can be seeded against them.
    /// The caller's SaveChangesAsync commits them.
    /// </summary>
    public static List<InvestigationDuty> AddDefaultDuties(
        BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        var now = DateTime.UtcNow;
        var added = new List<InvestigationDuty>(_defaults.Length);

        for (var i = 0; i < _defaults.Length; i++)
        {
            var duty = new InvestigationDuty
            {
                OrganizationId = organizationId,
                Name = _defaults[i].Name,
                SortOrder = i + 1,
                IsActive = true,
                IsSingleHolder = _defaults[i].SingleHolder,
                Capabilities = _defaults[i].Capabilities,
                DateCreated = now,
                CreatedByAppUserId = createdByAppUserId,
            };
            db.InvestigationDuties.Add(duty);
            added.Add(duty);
        }

        return added;
    }

    /// <summary>
    /// Opens each default duty to the rungs it starts open to (item 160).
    /// </summary>
    /// <remarks>
    /// <para>Linked through the navigation properties rather than ids: both sides were added in
    /// this same change and EF fixes the keys up on save, so nothing here depends on when a Guid
    /// gets its value.</para>
    ///
    /// <para>A duty or title that is not in the lists is skipped rather than invented — this also
    /// runs for groups whose ladder somebody may already have edited, and seeding a rung the group
    /// never chose would put a name in their matrix out of nowhere.</para>
    /// </remarks>
    public static void AddDefaultEligibility(
        BenDataContext db,
        IReadOnlyCollection<InvestigationDuty> duties,
        IReadOnlyCollection<OrganizationMemberLevel> levels,
        Guid createdByAppUserId)
    {
        var now = DateTime.UtcNow;

        foreach (var (dutyName, titles) in _defaultMatrix)
        {
            var duty = duties.FirstOrDefault(d => d.Name == dutyName);
            if (duty is null) continue;

            foreach (var title in titles)
            {
                var level = levels.FirstOrDefault(l => l.Name == title);
                if (level is null) continue;

                db.InvestigationDutyEligibilities.Add(new InvestigationDutyEligibility
                {
                    InvestigationDuty = duty,
                    OrganizationMemberLevel = level,
                    DateCreated = now,
                    CreatedByAppUserId = createdByAppUserId,
                });
            }
        }
    }
}
