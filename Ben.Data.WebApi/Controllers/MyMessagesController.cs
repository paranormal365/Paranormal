using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The recipient's view of the platform message system.
/// </summary>
/// <remarks>
/// <c>UserMessageController</c>/<c>UserMessageToController</c> already exist, but both are
/// SuperAdmin-only <c>EntityReadControllerBase</c> subclasses that return every row unfiltered —
/// deliberately unreachable by ordinary users. So until now a message could be *sent* (by the audit
/// log's "send as message" action) with no way for its recipient to ever read it. These are the
/// endpoints that close that loop: everything here is scoped to the caller's own
/// <c>UserMessageTo</c> rows.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/messages")]
public sealed class MyMessagesController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public MyMessagesController(IDbContextFactory<BenDataContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>Messages addressed to the caller, newest first.</summary>
    /// <param name="unreadOnly">Restrict to messages the caller has never opened.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MyMessageRecord>>> GetMine(
        [FromQuery] bool unreadOnly, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var query = db.UserMessageTos.AsNoTracking().Where(t => t.ToAppUserId == userId);
        if (unreadOnly) query = query.Where(t => t.DateLastRead == null);

        var items = await query
            .OrderByDescending(t => t.UserMessage.DateCreated)
            .Select(t => new MyMessageRecord(
                t.Id,
                t.MessageId,
                t.UserMessage.MessageSubject,
                t.UserMessage.MessageBody,
                t.UserMessage.UserMessageType.Name,
                t.UserMessage.UserMessageType.IconClass,
                t.UserMessage.UserMessageType.ColorClass,
                t.UserMessage.DateCreated,
                t.DateLastRead,
                // Both the id and the name go, not just the name: an anonymous channel that still
                // hands over the author's user id is not anonymous. Note the fallback below — when
                // a display name is missing this used to send the author's EMAIL address.
                t.UserMessage.HideSenderIdentity ? null : (Guid?)t.UserMessage.CreatedByAppUserId,
                t.UserMessage.HideSenderIdentity
                    ? null
                    : (t.UserMessage.CreatedByAppUser.DisplayName ?? t.UserMessage.CreatedByAppUser.Email),
                t.UserMessage.HideSenderIdentity))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Marks one of the caller's messages read. Idempotent — re-reading a message refreshes the
    /// timestamp and bumps the open count rather than failing.
    /// </summary>
    /// <param name="id">The <c>UserMessageTo</c> row id from <see cref="GetMine"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var row = await db.UserMessageTos.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return NotFound();
        // Not Forbid: whether a given id exists is itself information the caller has no claim to.
        if (row.ToAppUserId != userId) return NotFound();

        row.DateLastRead   = DateTime.UtcNow;
        row.LastReadCount += 1;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Marks every unread message of the caller's read in one call.</summary>
    [HttpPut("read-all")]
    public async Task<ActionResult<int>> MarkAllRead(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Loaded and saved rather than ExecuteUpdate: this is bounded by one user's unread
        // messages, and every other write in this API goes through the change tracker.
        var rows = await db.UserMessageTos
            .Where(t => t.ToAppUserId == userId && t.DateLastRead == null)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.DateLastRead   = now;
            row.LastReadCount += 1;
        }
        await db.SaveChangesAsync(ct);

        return Ok(rows.Count);
    }
}
