using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/audit-logs")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminAuditLogController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly Services.PlatformMessageService _messages;

    public AdminAuditLogController(
        IDbContextFactory<BenDataContext> db, Services.PlatformMessageService messages)
    {
        _db       = db;
        _messages = messages;
    }

    // ── GET /api/admin/audit-logs ─────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<AuditLogPagedResponse>> GetAll(
        [FromQuery] int      page       = 1,
        [FromQuery] int      pageSize   = 50,
        [FromQuery] string?  entityType = null,
        [FromQuery] int?     action     = null,
        [FromQuery] Guid?    userId     = null,
        [FromQuery] DateTime? dateFrom  = null,
        [FromQuery] DateTime? dateTo    = null,
        CancellationToken ct = default)
    {
        var validPageSize = Math.Clamp(pageSize, 1, 200);
        var validPage     = Math.Max(page, 1);

        await using var db = await _db.CreateDbContextAsync(ct);
        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking()
            .OrderByDescending(l => l.OccurredAt);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(l => l.EntityType == entityType);
        if (action.HasValue && Enum.IsDefined(typeof(AuditAction), action.Value))
            query = query.Where(l => l.Action == (AuditAction)action.Value);
        if (userId.HasValue)
            query = query.Where(l => l.UserId == userId.Value);
        if (dateFrom.HasValue)
            query = query.Where(l => l.OccurredAt >= dateFrom.Value);
        if (dateTo.HasValue)
            query = query.Where(l => l.OccurredAt <= dateTo.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .ToListAsync(ct);

        var userIds = items.Select(i => i.UserId).Distinct().ToList();
        var displayNames = await db.AppUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, ct);

        var records = items.Select(l => ToRecord(l, displayNames.GetValueOrDefault(l.UserId))).ToList();
        return Ok(new AuditLogPagedResponse(records, total));
    }

    // ── GET /api/admin/audit-logs/entity-types ────────────────────────────────

    /// <summary>Returns distinct entity type names for use in the filter dropdown.</summary>
    [HttpGet("entity-types")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetEntityTypes(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var types = await db.AuditLogs.AsNoTracking()
            .Select(l => l.EntityType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);
        return Ok(types);
    }

    // ── POST /api/admin/audit-logs/send-message ───────────────────────────────

    [HttpPost("send-message")]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendAuditLogMessageRequest request, CancellationToken ct)
    {
        if (request.RecipientUserIds is null || request.RecipientUserIds.Count == 0)
            return BadRequest("At least one recipient is required.");

        // The mechanism lives in PlatformMessageService now — the tier-change notices send the
        // same kind of message, and two private copies of find-or-create-the-type is how the
        // type gets duplicated the first time they race.
        await _messages.SendAsync(
            request.Subject, request.Body, [.. request.RecipientUserIds], GetCurrentUserId(), ct);

        return Ok();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static AuditLogRecord ToRecord(AuditLog l, string? userDisplayName) => new()
    {
        Id              = l.Id,
        UserId          = l.UserId,
        UserDisplayName = userDisplayName,
        Action          = l.Action,
        EntityType      = l.EntityType,
        EntityId        = l.EntityId,
        Source          = l.Source,
        OccurredAt      = l.OccurredAt,
        ChangesJson     = l.ChangesJson
    };
}
