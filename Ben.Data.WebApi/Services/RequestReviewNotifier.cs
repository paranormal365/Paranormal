using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The messages around a client request's review: "come vote", "another group took it",
/// "you have a group".
/// </summary>
/// <remarks>
/// <para>One class so the three messages that tell one story are written next to each other,
/// and so the recipient rule is defined once. Bodies are HTML — the notifications page renders
/// them with <c>Html="true"</c> — and links are host-relative, which survives every
/// environment.</para>
/// </remarks>
public sealed class RequestReviewNotifier
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly PlatformMessageService _messages;

    public RequestReviewNotifier(IDbContextFactory<BenDataContext> db, PlatformMessageService messages)
    { _db = db; _messages = messages; }

    /// <summary>
    /// The members of <paramref name="orgId"/> who can actually open the review page.
    /// </summary>
    /// <remarks>
    /// <para>The review page gates on <c>Case.Read</c>, and the dead-end-clicks policy (item 149)
    /// says a link in a message must open to something — so the message goes only to people the
    /// link will work for: owners and administrators (who bypass grants), plus members holding
    /// <c>Case.Read</c> directly or through an active role.</para>
    ///
    /// <para>This mirrors <c>OrganizationSecurityService.HasAccessAsync</c> inverted — that
    /// answers "may this one person", this answers "which people may" — and simplification is
    /// accepted: the tier-area gate is not consulted, because the Cases area is core to every
    /// plan. If an excluded-Cases tier ever exists, an ineligible member gets a message whose
    /// link shows a refusal, which is annoying and safe.</para>
    /// </remarks>
    public static async Task<List<Guid>> EligibleReviewerIdsAsync(
        BenDataContext db, Guid orgId, CancellationToken ct)
    {
        var admins = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.IsActive
                     && m.Role <= OrganizationMemberRole.Administrator)
            .Select(m => m.AppUserId)
            .ToListAsync(ct);

        var granted = await db.OrganizationAccessGrants.AsNoTracking()
            .Where(g => g.OrganizationId == orgId
                     && g.TableName == OrganizationSecurityTable.Case
                     && (g.Actions & OrganizationSecurityAction.Read) != OrganizationSecurityAction.None)
            .Select(g => g.AppUserId)
            .ToListAsync(ct);

        var viaRoles = await (
            from rm in db.OrganizationRoleMemberships.AsNoTracking()
            join role in db.OrganizationRoles on rm.OrganizationRoleId equals role.Id
            join perm in db.OrganizationRolePermissions on role.Id equals perm.OrganizationRoleId
            join membership in db.OrganizationUserMemberships on rm.OrganizationUserMembershipId equals membership.Id
            where membership.OrganizationId == orgId && membership.IsActive && role.IsActive
               && perm.TableName == OrganizationSecurityTable.Case
               && (perm.Actions & OrganizationSecurityAction.Read) != OrganizationSecurityAction.None
            select membership.AppUserId
        ).ToListAsync(ct);

        return admins.Concat(granted).Concat(viaRoles).Distinct().ToList();
    }

    /// <summary>Marking Under Review: ask the group to come and vote.</summary>
    public async Task SendReviewOpenedAsync(
        Guid orgId, Guid clientRequestId, Guid byUserId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var request = await db.ClientRequests.AsNoTracking()
            .Where(r => r.Id == clientRequestId)
            .Select(r => new { r.City, r.State })
            .FirstOrDefaultAsync(ct);
        if (request is null) return;

        var recipients = await EligibleReviewerIdsAsync(db, orgId, ct);
        var link = $"/organizations/{orgId}/request-review/{clientRequestId}";

        await _messages.SendAsync(
            $"Vote: take on the investigation request in {request.City}, {request.State}?",
            $"<p>Your group has an investigation request under review — a location in "
          + $"{request.City}, {request.State}.</p>"
          + $"<p><a href=\"{link}\">Review everything the client submitted and cast your vote</a>. "
          + "The submission may include photos and other files; all of it is on the review page.</p>"
          + "<p>Other groups may be reviewing this request too — the first group to accept it "
          + "takes the case.</p>",
            recipients, byUserId, ct);
    }

    /// <summary>The race is over: tell every OTHER reviewing group it is no longer available.</summary>
    /// <remarks><c>cancelledOrgIds</c> are the groups whose applications were just cancelled.</remarks>
    public async Task SendNoLongerAvailableAsync(
        IReadOnlyCollection<Guid> cancelledOrgIds, Guid clientRequestId, Guid byUserId, CancellationToken ct)
    {
        if (cancelledOrgIds.Count == 0) return;
        await using var db = await _db.CreateDbContextAsync(ct);

        var request = await db.ClientRequests.AsNoTracking()
            .Where(r => r.Id == clientRequestId)
            .Select(r => new { r.City, r.State })
            .FirstOrDefaultAsync(ct);
        if (request is null) return;

        foreach (var orgId in cancelledOrgIds)
        {
            var recipients = await EligibleReviewerIdsAsync(db, orgId, ct);
            await _messages.SendAsync(
                $"No longer available: the investigation request in {request.City}, {request.State}",
                $"<p>The investigation request in {request.City}, {request.State} that your group "
              + "was reviewing has been taken on by another group, so it is no longer available. "
              + "No further action is needed, and any votes cast are simply closed.</p>",
                recipients, byUserId, ct);
        }
    }

    /// <summary>Tell the client their case has a group, and where to talk to it.</summary>
    public async Task SendClientAcceptedAsync(
        Guid clientUserId, Guid caseId, string organizationName, string? contactDisplayName,
        Guid byUserId, CancellationToken ct)
    {
        var contactLine = contactDisplayName is null
            ? "Your group will introduce your contact person on your case page."
            : $"Your contact person is <strong>{System.Net.WebUtility.HtmlEncode(contactDisplayName)}</strong>.";

        await _messages.SendAsync(
            $"{organizationName} has taken on your investigation",
            $"<p><strong>{System.Net.WebUtility.HtmlEncode(organizationName)}</strong> has accepted "
          + "your investigation request — you have a group.</p>"
          + $"<p>{contactLine}</p>"
          + $"<p><a href=\"/my-cases/{caseId}\">Open your case</a> to see what happens next and to "
          + "send messages to your group from the Messages tab.</p>",
            [clientUserId], byUserId, ct);
    }
}
