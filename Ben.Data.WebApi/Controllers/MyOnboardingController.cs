using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The caller's first-run onboarding state (item 166 W2).
/// </summary>
/// <remarks>
/// One flag with one honest rule: <c>DateOnboarded</c> null means the wizard has never been
/// answered; completing OR skipping stamps it, because skipping is an answer and the wizard
/// must never nag twice. Existing accounts were stamped by the column's own migration.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/onboarding")]
public sealed class MyOnboardingController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public MyOnboardingController(IDbContextFactory<BenDataContext> dbFactory)
        => _dbFactory = dbFactory;

    [HttpGet]
    public async Task<ActionResult<OnboardingStateResponse>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var onboarded = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DateOnboarded != null)
            .FirstOrDefaultAsync(ct);

        return Ok(new OnboardingStateResponse(onboarded));
    }

    /// <summary>Stamps the caller as onboarded. Idempotent; finishing and skipping both land here.</summary>
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (user.DateOnboarded is null)
        {
            user.DateOnboarded = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
}

public sealed record OnboardingStateResponse(bool Onboarded);
