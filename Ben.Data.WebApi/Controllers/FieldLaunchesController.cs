using Ben.Data.WebApi.Services.FieldLaunches;
using Ben.Service.Models.FieldLaunches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// A lead starting everybody's Field Kit at once (item 252). The rules live in
/// <see cref="FieldLaunchService"/>; this is the door to them.
/// </summary>
[ApiController]
[Route("api/field-launches")]
public sealed class FieldLaunchesController : BenControllerBase
{
    private readonly FieldLaunchService _launches;

    public FieldLaunchesController(FieldLaunchService launches)
    {
        _launches = launches;
    }

    /// <summary>What the caller may launch right now — the lead's list.</summary>
    [HttpGet("launchable")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<LaunchableRecord>>> Launchable(CancellationToken ct)
        => Ok(await _launches.LaunchableAsync(GetCurrentUserId(), ct));

    /// <summary>Launch: push everybody registered, and put the card in the feed.</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<LaunchOutcomeRecord>> Launch([FromBody] LaunchRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (FieldLaunchService.ParseTarget(request.Target) is not { } target)
            return BadRequest(new { message = "Target must be investigation, event or hosted-event." });

        return await _launches.LaunchAsync(userId, await CallerIsSuperAdminAsync(), target, request.Id, ct) switch
        {
            FieldLaunchService.LaunchResult.Launched launched => Ok(launched.Outcome),
            FieldLaunchService.LaunchResult.NotFound => NotFound(),
            FieldLaunchService.LaunchResult.Forbidden => Forbid(),
            FieldLaunchService.LaunchResult.Refused refused => Conflict(new { message = refused.Reason }),
            _ => StatusCode(500),
        };
    }

    /// <summary>The launches still open for the caller — "happening now".</summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<FieldLaunchRecord>>> Mine(CancellationToken ct)
        => Ok(await _launches.MineAsync(GetCurrentUserId(), ct));

    /// <summary>
    /// One launch, for opening Field Kit on it. Anybody may open a public one — Field Kit works
    /// without an account; a private one only its people. Gone, expired and not-yours are all 404.
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<FieldLaunchRecord>> Get(Guid id, CancellationToken ct)
    {
        var reader = await GetCurrentUserIdOrNullAcrossSchemesAsync() ?? Guid.Empty;
        return await _launches.ReadAsync(id, reader, await CallerIsSuperAdminAsync(), ct) is { } launch
            ? Ok(launch)
            : NotFound();
    }
}
