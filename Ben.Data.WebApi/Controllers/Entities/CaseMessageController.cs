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

    public CaseMessageController(IDbContextFactory<BenDataContext> db, Services.Billing.SubscriptionLimitGuard limits)
    { _db = db; _limits = limits; }

    /// <summary>Returns all messages and marks client messages as read by the org.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseMessageRecord>>> GetMessages(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgCase(db, orgId, caseId, userId, ct)) return NotFound();
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
        if (!await IsOrgCase(db, orgId, caseId, userId, ct)) return NotFound();
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

    private static async Task<bool> IsOrgCase(BenDataContext db, Guid orgId, Guid caseId, Guid userId, CancellationToken ct)
        => await db.Cases.AsNoTracking()
            .AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)
            && await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

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
        if (!await IsOrgCase(db, orgId, caseId, userId, ct)) return NotFound();
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
