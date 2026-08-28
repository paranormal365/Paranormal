using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The caller's block list — who they refuse to see, managed by them alone.
/// </summary>
/// <remarks>
/// <para>App Review Guideline 1.2 names four obligations for user-generated content: filter it,
/// report it, <b>block abusive users</b>, and publish a contact. This is the third. Reporting
/// (<c>FeedController.Report</c>) asks a moderator to act eventually; blocking acts immediately,
/// for this reader, which is what somebody being harassed actually needs in the moment.</para>
///
/// <para><b>Like reporting, deliberately NOT gated on feed participation.</b> The participation
/// rule (item 186 F2) decides who may build an audience; a person who may not post can still be
/// abused, and their right to stop seeing somebody cannot depend on their standing to write.</para>
///
/// <para><b>Not gated on the feed flag either.</b> A block is a fact about two people, not about
/// the feed feature — it is enforced wherever blocked content could surface, so it must be
/// settable even while the feed is dark, and it survives the feed being toggled.</para>
///
/// <para>Scoped to the bearer token's own account throughout, like every <c>api/me</c> surface —
/// there is no "block on someone's behalf" shape to get wrong.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/blocks")]
public sealed class MyBlocksController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MyBlocksController(IDbContextFactory<BenDataContext> db)
    {
        _db = db;
    }

    /// <summary>One row of the caller's block list.</summary>
    /// <remarks>
    /// Carries the display name so the list is readable without a join on the client — and it is
    /// the name as it stands NOW, so a blocked account that later closes shows "A former member"
    /// here like everywhere else.
    /// </remarks>
    public sealed record BlockedUserRecord(Guid AppUserId, string DisplayName, DateTime DateCreated);

    /// <summary>Everyone the caller has blocked, most recent first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BlockedUserRecord>>> GetBlocks(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var blocks = await db.UserBlocks.AsNoTracking()
            .Where(b => b.BlockerAppUserId == userId)
            .OrderByDescending(b => b.DateCreated)
            .Join(db.AppUsers.AsNoTracking(),
                  b => b.BlockedAppUserId, u => u.Id,
                  (b, u) => new BlockedUserRecord(
                      u.Id, u.DisplayName ?? u.Email ?? "Unknown", b.DateCreated))
            .ToListAsync(ct);

        return Ok(blocks);
    }

    /// <summary>
    /// Blocks a person: their posts and replies stop being shown to the caller, from now.
    /// </summary>
    /// <remarks>
    /// Also severs any follow in either direction — "I never want to see them" and "I follow
    /// them" cannot both stand, and leaving the reverse row would keep the caller appearing in
    /// the blocked person's following feed. Idempotent: blocking twice is blocking once.
    /// </remarks>
    [HttpPost("{appUserId:guid}")]
    public async Task<IActionResult> Block(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (appUserId == userId) return BadRequest("You can't block yourself.");

        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.AppUsers.AsNoTracking().AnyAsync(u => u.Id == appUserId, ct)) return NotFound();

        var follows = await db.UserFollows
            .Where(f => (f.FollowerAppUserId == userId && f.FollowedAppUserId == appUserId)
                     || (f.FollowerAppUserId == appUserId && f.FollowedAppUserId == userId))
            .ToListAsync(ct);
        db.UserFollows.RemoveRange(follows);

        if (!await db.UserBlocks.AnyAsync(
                b => b.BlockerAppUserId == userId && b.BlockedAppUserId == appUserId, ct))
        {
            db.UserBlocks.Add(new UserBlock
            {
                Id = Guid.NewGuid(),
                BlockerAppUserId = userId,
                BlockedAppUserId = appUserId,
                DateCreated = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Unblocks a person. Their posts reappear; severed follows stay severed.</summary>
    /// <remarks>
    /// The follows are not restored on purpose. A block-then-unblock is a decision revisited, not
    /// one that never happened, and silently re-following somebody you blocked last week is the
    /// kind of surprise that reads as the app acting on its own.
    /// </remarks>
    [HttpDelete("{appUserId:guid}")]
    public async Task<IActionResult> Unblock(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var existing = await db.UserBlocks
            .FirstOrDefaultAsync(b => b.BlockerAppUserId == userId && b.BlockedAppUserId == appUserId, ct);

        if (existing is not null)
        {
            db.UserBlocks.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
