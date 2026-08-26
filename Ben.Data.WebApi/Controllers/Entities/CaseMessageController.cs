using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/messages")]
[Authorize]
public sealed class CaseMessageController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    private readonly Services.Billing.SubscriptionLimitGuard _limits;
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    public CaseMessageController(
        IDbContextFactory<BenDataContext> db,
        Services.Billing.SubscriptionLimitGuard limits,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _limits = limits; _security = security; }

    /// <summary>Returns all messages and marks client messages as read by the org.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseMessageRecord>>> GetMessages(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayUseThreadAsync(db, orgId, caseId, OrganizationSecurityAction.Read, ct)) return NotFound();
        // Item 84: the ORG stops writing when lapsed. The client's half of this conversation is
        // MyCaseController and stays open — their records, their voice.
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);

        var messages = await db.CaseMessages.AsNoTracking()
            .Include(m => m.AuthorAppUser)
            .Where(m => m.CaseId == caseId)
            .OrderBy(m => m.DateCreated)
            .ToListAsync(ct);

        // Mark unread client messages as read now that org is viewing
        var unread = await db.CaseMessages
            .Where(m => m.CaseId == caseId && m.SenderSide == CaseMessageSide.Client && !m.IsReadByOrg)
            .ToListAsync(ct);
        if (unread.Count > 0)
        {
            unread.ForEach(m => m.IsReadByOrg = true);
            await db.SaveChangesAsync(ct);
        }

        return Ok(messages.Select(ToRecord));
    }

    /// <summary>Posts a new message from the org to the client.</summary>
    [HttpPost]
    public async Task<ActionResult<CaseMessageRecord>> PostMessage(
        Guid orgId, Guid caseId, [FromBody] PostCaseMessageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Body)) return BadRequest("Message body is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayUseThreadAsync(db, orgId, caseId, OrganizationSecurityAction.Update, ct)) return NotFound();
        // Item 84: the ORG stops writing when lapsed. The client's half of this conversation is
        // MyCaseController and stays open — their records, their voice.
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);

        var msg = new CaseMessage
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            Body               = request.Body.Trim(),
            SenderSide         = CaseMessageSide.Organization,
            IsReadByClient     = false,
            IsReadByOrg        = true,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        await db.Entry(msg).Reference(m => m.AuthorAppUser).LoadAsync(ct);
        return Ok(ToRecord(msg));
    }

    /// <summary>
    /// Whether the case is this organization's, and the caller may take <paramref name="action"/>
    /// on the group's cases.
    /// </summary>
    /// <remarks>
    /// <para><b>Two questions, both load-bearing.</b> The case must belong to the org in the route
    /// — otherwise a member of group A reads group B's conversation by pairing their own org id
    /// with someone else's case id, the broken-ID-chain shape the Phase-B audit found nine times.
    /// And the caller must hold the grant.</para>
    ///
    /// <para><b>Found on Ben's prompt, 2026-08-26:</b> "Be sure to check permissions for clients of
    /// organizations with their case." This asked for bare active membership, so every member of
    /// the group could read the private conversation between the client and their investigator,
    /// and post into it under the group's name — no case grant needed, none consulted. That was
    /// invisible while the seeder handed case read to everyone; ending the grandfathering is what
    /// made it matter.</para>
    ///
    /// <para><b>Reading is Read, speaking to the client is Update.</b> Answering a client in the
    /// group's name is acting on their case, not observing it, so a read-only member sees the
    /// thread and cannot write to it. Owners and administrators pass through
    /// <see cref="Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService.MayAsync"/>
    /// as they do everywhere.</para>
    ///
    /// <para>The client's own half of this conversation is <c>MyCaseController</c>, gated on
    /// being the client of the case rather than on any grant — a client holds no membership and
    /// no grants, and must never be asked for one.</para>
    /// </remarks>
    private async Task<bool> MayUseThreadAsync(
        BenDataContext db, Guid orgId, Guid caseId, OrganizationSecurityAction action, CancellationToken ct)
    {
        if (!await db.Cases.AsNoTracking().AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct))
            return false;

        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)) return true;

        return await _security.MayAsync(
            GetCurrentUserId(), orgId, OrganizationPermissionArea.Cases, action, ct);
    }

    private static CaseMessageRecord ToRecord(CaseMessage m) => new(
        m.Id, m.CaseId, m.AuthorAppUserId,
        m.AuthorAppUser?.DisplayName ?? "Unknown",
        m.Body, m.SenderSide, m.IsReadByClient, m.IsReadByOrg, m.DateCreated);

    /// <summary>Returns the count of unread client messages (org has not yet seen them).</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayUseThreadAsync(db, orgId, caseId, OrganizationSecurityAction.Read, ct)) return NotFound();
        // Item 84: the ORG stops writing when lapsed. The client's half of this conversation is
        // MyCaseController and stays open — their records, their voice.
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);

        var count = await db.CaseMessages
            .CountAsync(m => m.CaseId == caseId && m.SenderSide == CaseMessageSide.Client && !m.IsReadByOrg, ct);
        return Ok(count);
    }
}

public sealed record PostCaseMessageRequest(string Body);

public sealed record CaseMessageRecord(
    Guid   Id,
    Guid   CaseId,
    Guid   AuthorAppUserId,
    string AuthorDisplayName,
    string Body,
    Ben.Data.Common.Enums.CaseMessageSide SenderSide,
    bool   IsReadByClient,
    bool   IsReadByOrg,
    DateTime DateCreated);
