using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The caller's own walkthrough-tour state (item 166 W0).
/// </summary>
/// <remarks>
/// Rows, not localStorage, so impersonation shows the real person's state and a cleared
/// browser replays nothing. A tour name in the list means "never auto-launch this again";
/// tours remain relaunchable by hand regardless. Dismissing records whether the person saw
/// the tour through or skipped — both silence the auto-launch.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/tours")]
public sealed class MyToursController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public MyToursController(IDbContextFactory<BenDataContext> dbFactory)
        => _dbFactory = dbFactory;

    /// <summary>Every tour the caller has dismissed. The page asks once and launches nothing listed.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<string>>> GetDismissed(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return Ok(await db.UserTourStates.AsNoTracking()
            .Where(t => t.AppUserId == userId)
            .Select(t => t.TourName)
            .ToListAsync(ct));
    }

    /// <summary>Dismisses one tour. Idempotent upsert — dismissing twice updates, never duplicates.</summary>
    [HttpPut("{tourName}")]
    public async Task<IActionResult> Dismiss(
        string tourName, [FromBody] DismissTourRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tourName) || tourName.Length > 64)
            return BadRequest("Tour name must be 1-64 characters.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var row = await db.UserTourStates
            .FirstOrDefaultAsync(t => t.AppUserId == userId && t.TourName == tourName, ct);
        if (row is null)
        {
            db.UserTourStates.Add(new UserTourState
            {
                Id = Guid.NewGuid(), AppUserId = userId, TourName = tourName,
                Completed = request.Completed,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }
        else
        {
            row.Completed          = request.Completed;
            row.DateUpdated        = DateTime.UtcNow;
            row.UpdatedByAppUserId = userId;
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>Whether the tour was seen through (true) or skipped out of (false).</summary>
public sealed record DismissTourRequest(bool Completed);
