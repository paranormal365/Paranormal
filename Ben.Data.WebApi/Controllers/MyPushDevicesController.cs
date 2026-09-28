using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The phones the caller may be sent pushes on (item 252): the app registers one when somebody
/// signs in, and removes it when they sign out.
/// </summary>
/// <remarks>
/// Scoped to the bearer token's own account, like every <c>api/me</c> surface. A token already
/// registered to somebody else moves to the caller: it is the same phone in new hands, and the
/// previous person must stop receiving pushes on it.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/push-devices")]
public sealed partial class MyPushDevicesController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MyPushDevicesController(IDbContextFactory<BenDataContext> db)
    {
        _db = db;
    }

    /// <summary>A phone registering for pushes.</summary>
    /// <param name="Token">Apple's device token, as hex.</param>
    /// <param name="Environment"><c>sandbox</c> for a build run from Xcode, <c>production</c> otherwise.</param>
    /// <param name="AppVersion">The app's version, e.g. <c>1.1.0</c>.</param>
    public sealed record RegisterPushDeviceRequest(string Token, string Environment, string? AppVersion);

    /// <summary>Registers (or refreshes) this phone for the caller.</summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterPushDeviceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var token = request.Token?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!DeviceToken().IsMatch(token))
            return BadRequest(new { message = "That is not a device token this app would send." });
        PushEnvironment environment;
        switch (request.Environment?.Trim().ToLowerInvariant())
        {
            case "sandbox": environment = PushEnvironment.Sandbox; break;
            case "production": environment = PushEnvironment.Production; break;
            default: return BadRequest(new { message = "Environment must be sandbox or production." });
        }
        var version = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim()[..Math.Min(32, request.AppVersion.Trim().Length)];

        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var device = await db.PushDevices.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (device is null)
        {
            db.PushDevices.Add(new PushDevice
            {
                Id = Guid.NewGuid(), AppUserId = userId, Token = token, Environment = environment,
                AppVersion = version, DateCreated = now, LastSeenUtc = now,
            });
        }
        else
        {
            device.AppUserId = userId;
            device.Environment = environment;
            device.AppVersion = version;
            device.LastSeenUtc = now;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two registrations of one new token raced; the other one won and is equally right.
        }
        return NoContent();
    }

    /// <summary>Stops pushes to this phone for the caller — signing out.</summary>
    /// <remarks>Idempotent, and quiet about a token that is not the caller's: it says nothing about whose it is.</remarks>
    [HttpDelete("{token}")]
    public async Task<IActionResult> Remove(string token, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var normalized = token.Trim().ToLowerInvariant();
        await using var db = await _db.CreateDbContextAsync(ct);
        var device = await db.PushDevices.FirstOrDefaultAsync(d => d.Token == normalized && d.AppUserId == userId, ct);
        if (device is not null)
        {
            db.PushDevices.Remove(device);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>Apple's tokens are 32 bytes today (64 hex digits); room is left for them to grow.</summary>
    [GeneratedRegex("^[0-9a-f]{64,200}$")]
    private static partial Regex DeviceToken();
}
