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
    private static readonly (string Name, bool SingleHolder)[] _defaults =
    [
        ("Lead Investigator",   true),
        ("Equipment",           false),
        ("Evidence Collection", false),
        ("Documentation",       false),
    ];

    /// <summary>The seeded name whose assignment writes through to <c>InvestigationAttendee.IsLead</c>.</summary>
    public const string LeadDutyName = "Lead Investigator";

    /// <summary>Stages the default duties; the caller's SaveChangesAsync commits them.</summary>
    public static void AddDefaultDuties(BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < _defaults.Length; i++)
        {
            db.InvestigationDuties.Add(new InvestigationDuty
            {
                OrganizationId = organizationId,
                Name = _defaults[i].Name,
                SortOrder = i + 1,
                IsActive = true,
                IsSingleHolder = _defaults[i].SingleHolder,
                DateCreated = now,
                CreatedByAppUserId = createdByAppUserId,
            });
        }
    }
}
