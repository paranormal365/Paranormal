using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// What a request must have before it can be sent to a group.
/// </summary>
/// <remarks>
/// One set of rules for the two doors: the signed-in wizard's <c>POST api/client-requests/{id}/submit</c>
/// and the signed-out wizard's <c>POST api/public/client-requests/submit</c>. They were written
/// once in the first controller and are shared rather than copied so the second door cannot
/// drift into accepting something the first refuses — or the other way round.
/// </remarks>
public static class ClientRequestRules
{
    public const int MaxOrganizations = 2;

    /// <summary>The address, the story and the chosen groups. Null means "fine".</summary>
    public static string? CheckSubmission(
        decimal? latitude, decimal? longitude, string? description, IReadOnlyCollection<Guid> organizationIds)
    {
        if (organizationIds.Count == 0)
            return "At least one organization is required.";
        if (organizationIds.Count > MaxOrganizations)
            return $"You may apply to a maximum of {MaxOrganizations} organizations.";
        if (organizationIds.Distinct().Count() != organizationIds.Count)
            return "Duplicate organizations are not allowed.";
        if (!latitude.HasValue || !longitude.HasValue)
            return "The address must be geocoded before submitting.";
        if (string.IsNullOrWhiteSpace(description))
            return "A description of your experiences is required before submitting.";
        return null;
    }

    /// <summary>Every chosen group must exist. Null means "fine".</summary>
    public static async Task<string?> CheckOrganizationsExistAsync(
        BenDataContext db, IReadOnlyCollection<Guid> organizationIds, CancellationToken ct)
    {
        foreach (var orgId in organizationIds)
        {
            var orgExists = await db.Organizations.AnyAsync(o => o.Id == orgId, ct);
            if (!orgExists) return $"Organization {orgId} not found.";
        }
        return null;
    }
}
