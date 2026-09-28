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

    // ── Joining by the lead's code (item 252) ────────────────────────────────

    /// <summary>
    /// Where somebody who scanned the lead's QR code stands. Anonymous, because Field Kit is; a
    /// private launch then says to sign in. Unknown, ended or never-allowed codes are 404.
    /// </summary>
    [HttpGet("join/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<JoinStandingRecord>> Standing(string token, CancellationToken ct)
    {
        var reader = await GetCurrentUserIdOrNullAcrossSchemesAsync() ?? Guid.Empty;
        return await _launches.StandingAsync(token, reader, await CallerIsSuperAdminAsync(), ct) is { } standing
            ? Ok(standing)
            : NotFound();
    }

    /// <summary>Asks the lead to be let in; the lead's phone is told.</summary>
    [HttpPost("join/{token}/ask")]
    [Authorize]
    public async Task<ActionResult<JoinStandingRecord>> Ask(string token, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        return await _launches.AskAsync(token, userId, ct) is { } standing ? Ok(standing) : NotFound();
    }

    /// <summary>Who has asked to join — for whoever may manage the launch.</summary>
    [HttpGet("{id:guid}/requests")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<JoinRequestRecord>>> Requests(Guid id, CancellationToken ct)
        => await _launches.RequestsAsync(id, GetCurrentUserId(), await CallerIsSuperAdminAsync(), ct) is { } requests
            ? Ok(requests)
            : NotFound();

    /// <summary>Lets somebody in: registers them for the thing and tells their phone.</summary>
    [HttpPost("{id:guid}/requests/{requestId:guid}/approve")]
    [Authorize]
    public Task<ActionResult<JoinRequestRecord>> Approve(Guid id, Guid requestId, CancellationToken ct)
        => DecideAsync(id, requestId, approve: true, ct);

    /// <summary>Says no. Their sessions stay their own.</summary>
    [HttpPost("{id:guid}/requests/{requestId:guid}/decline")]
    [Authorize]
    public Task<ActionResult<JoinRequestRecord>> Decline(Guid id, Guid requestId, CancellationToken ct)
        => DecideAsync(id, requestId, approve: false, ct);

    private async Task<ActionResult<JoinRequestRecord>> DecideAsync(Guid id, Guid requestId, bool approve, CancellationToken ct)
        => await _launches.DecideAsync(id, requestId, GetCurrentUserId(), await CallerIsSuperAdminAsync(), approve, ct) switch
        {
            FieldLaunchService.DecideResult.Decided decided => Ok(decided.Request),
            FieldLaunchService.DecideResult.NotFound => NotFound(),
            FieldLaunchService.DecideResult.Forbidden => Forbid(),
            FieldLaunchService.DecideResult.Refused refused => Conflict(new { message = refused.Reason }),
            _ => StatusCode(500),
        };
}
