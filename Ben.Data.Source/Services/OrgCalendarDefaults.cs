using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;

namespace Ben.Data.Source.Services;

/// <summary>
/// The calendar event types every new organization starts with.
/// </summary>
/// <remarks>
/// <para>Event types are per-organization, so no global seeder can reach them — a group created
/// with none gets a calendar whose "type" dropdown is empty and a founder with no idea the list
/// is theirs to fill. Every door that creates an organization (self-service registration, the two
/// SuperAdmin create endpoints) calls this so the calendar is usable from the first moment; the
/// owner can rename, recolour, or retire any of them from the group's own settings.</para>
///
/// <para>Colour values are the Bootstrap text classes the event-types manager offers
/// (<c>OrgCalendarEventTypesManager.razor</c>); anything else would render, but could not be
/// round-tripped through the edit form's dropdown.</para>
/// </remarks>
public static class OrgCalendarDefaults
{
    private static readonly (string Name, string ColorClass)[] _defaults =
    [
        ("Investigation", "text-danger"),
        ("Public Event",  "text-primary"),
        ("Meeting",       "text-secondary"),
        ("Training",      "text-info"),
        ("Fundraiser",    "text-success"),
    ];

    /// <summary>
    /// Stages the default event types for <paramref name="organizationId"/> on the given context.
    /// Does not save — the caller's <c>SaveChangesAsync</c> commits them with the organization
    /// itself, so a failed create never leaves orphaned types behind.
    /// </summary>
    public static void AddDefaultEventTypes(BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < _defaults.Length; i++)
        {
            db.OrgCalendarEventTypes.Add(new OrgCalendarEventType
            {
                OrganizationId = organizationId,
                Name = _defaults[i].Name,
                ColorClass = _defaults[i].ColorClass,
                SortOrder = i + 1,
                IsActive = true,
                DateCreated = now,
                CreatedByAppUserId = createdByAppUserId,
            });
        }
    }
}
