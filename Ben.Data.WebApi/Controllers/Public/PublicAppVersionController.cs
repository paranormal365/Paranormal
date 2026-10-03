using Ben.Data.WebApi.Services.Apple;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Which version of the app is out, so the app can say whether a newer one is waiting.
/// </summary>
/// <remarks>
/// <para>Ben, 10/03/2026: "On your profile page, have a check for updated version which checks the
/// webapi to see if a newer version has been released." The app asks here rather than asking Apple
/// itself so the question has one answer, cached once for every phone, and so the source can change
/// (a staged rollout, a version the site wants to hold back) without shipping a new app.</para>
///
/// <para><b>Anonymous</b>, because the check is on Profile signed out as well — an out-of-date app is
/// most likely on a phone nobody has opened in a while — and the answer is what the public App Store
/// page already says.</para>
/// </remarks>
[ApiController]
[Route("api/public/app-version")]
[AllowAnonymous]
public sealed class PublicAppVersionController : ControllerBase
{
    private readonly IAppStoreVersionLookup _lookup;

    public PublicAppVersionController(IAppStoreVersionLookup lookup) => _lookup = lookup;

    /// <summary>The live iPhone and iPad release.</summary>
    /// <response code="200">The version the App Store is offering.</response>
    /// <response code="503">The App Store could not be asked just now; says so in a sentence.</response>
    [HttpGet("ios")]
    public async Task<ActionResult<AppVersionRecord>> Ios(CancellationToken ct)
    {
        if (await _lookup.LatestAsync(ct) is not { } release)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Couldn't reach the App Store to check for a new version. Try again in a few minutes.");

        return Ok(new AppVersionRecord(
            Platform: "ios",
            LatestVersion: release.Version,
            ReleasedUtc: release.ReleasedUtc,
            ReleaseNotes: release.ReleaseNotes,
            StoreUrl: release.StoreUrl,
            MinimumOsVersion: release.MinimumOsVersion));
    }
}

/// <summary>What the app compares its own version against.</summary>
/// <param name="Platform">"ios".</param>
/// <param name="LatestVersion">The live marketing version, "1.1.2".</param>
/// <param name="ReleasedUtc">When it went live.</param>
/// <param name="ReleaseNotes">Its "What's New" text.</param>
/// <param name="StoreUrl">Where to get it.</param>
/// <param name="MinimumOsVersion">The oldest iOS it installs on — a phone below it cannot update, and is told so.</param>
public sealed record AppVersionRecord(
    string Platform, string LatestVersion, DateTime? ReleasedUtc, string? ReleaseNotes,
    string StoreUrl, string? MinimumOsVersion);
