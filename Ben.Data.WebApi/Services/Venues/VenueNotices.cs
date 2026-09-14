using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Venues;

/// <summary>
/// Who hears, inside the site, when one group asks another for its venue (item 235 phase 9).
/// </summary>
/// <remarks>
/// A group's owners and administrators — the people who can answer. The bell carries the same
/// question to them, so a message that goes unread is still a count on a badge somebody sees.
/// </remarks>
public static class VenueNotices
{
    public static Task<List<Guid>> PeopleWhoAnswerForAsync(BenDataContext db, Guid orgId, CancellationToken ct)
        => db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.IsActive
                     && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator))
            .Select(m => m.AppUserId)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>HTML-safe, for a message body.</summary>
    public static string Safe(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
}
