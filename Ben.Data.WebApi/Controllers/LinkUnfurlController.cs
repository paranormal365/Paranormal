using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.LinkUnfurl;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SkiaSharp;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Preview cards for links pasted onto a case canvas, and the proxy that draws their pictures.
/// </summary>
/// <remarks>
/// <para><b>A deliberate reversal.</b> <c>PublicLinkPreviewController</c> never fetches a stranger's
/// page, because a server that fetches an address somebody chose is how server-side request forgery
/// works. Ben reversed that for the canvas on 2026-09-14: a pasted https link should become a
/// Twitter/X-style card, and only a server can read another site's page. The reversal is made safe by
/// these safeguards, each with its own test:</para>
/// <list type="number">
/// <item><description><b>The address.</b> <see cref="SafeUrlPolicy"/>: https, port 443, a real DNS
/// name, nothing private, no IPv6 spelling of anything private.</description></item>
/// <item><description><b>The connection.</b> <see cref="SafeUrlFetcher"/> resolves the name itself,
/// refuses if any answer is private, and dials only the address it vetted — no second lookup to rebind,
/// no proxy, no cookies, three re-vetted redirects, five seconds, a byte cap counted as it arrives.</description></item>
/// <item><description><b>The bytes.</b> The proxy never passes a third party's bytes on. It checks the
/// file is a displayable raster, reads the header and refuses anything over 40 million pixels before
/// decoding (a small file can claim a picture that takes gigabytes to decode), and answers only with
/// our own JPEG re-encoded at 800 px through <see cref="IMediaSanitizationService"/>.</description></item>
/// <item><description><b>Who, and how often.</b> Signed in, holding Cases Create in at least one group
/// (so a throwaway account cannot make this server fetch anything), per-person rate limits of 30 and
/// 120 a minute, a server-wide ceiling on picture fetches, a week-long cache, and the whole controller
/// dark until <b>Feature — Canvas editor</b> is switched on (it defaults off).</description></item>
/// </list>
///
/// <para><b>The record names the page's own picture</b> (<see cref="LinkUnfurlRecord.ImageSourceUrl"/>),
/// never a proxy address: the client builds <c>{ApiBaseUrl}/api/link-unfurl/image?url=</c> when it
/// draws the card, so a board saved on one deployment still draws on another.</para>
/// </remarks>
[ApiController]
[Route("api/link-unfurl")]
[Authorize]
[FeatureGated(SiteSettingKeys.FeatureCanvasEditor)]
public sealed class LinkUnfurlController : BenControllerBase
{
    /// <summary>The largest picture fetched, in bytes.</summary>
    public const long MaxImageBytes = 2_097_152;

    /// <summary>The long edge of the JPEG the proxy answers with.</summary>
    public const int ImageLongEdge = 800;

    /// <summary>The most pixels a picture's header may claim before it is refused undecoded.</summary>
    public const long MaxImagePixels = 40_000_000;

    private readonly LinkUnfurlService _unfurl;
    private readonly ISafeUrlFetcher _fetcher;
    private readonly IMediaSanitizationService _sanitizer;
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;
    private readonly LinkUnfurlImageCeiling _ceiling;

    public LinkUnfurlController(
        LinkUnfurlService unfurl, ISafeUrlFetcher fetcher, IMediaSanitizationService sanitizer,
        IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security,
        LinkUnfurlImageCeiling ceiling)
    {
        _unfurl    = unfurl;
        _fetcher   = fetcher;
        _sanitizer = sanitizer;
        _db        = db;
        _security  = security;
        _ceiling   = ceiling;
    }

    // GET /api/link-unfurl?url=
    /// <summary>What a pasted https link says about itself: title, description, picture address, site name.</summary>
    /// <param name="url">The link, absolute https.</param>
    /// <param name="ct">Cancellation.</param>
    /// <response code="200">The card. Cached privately by the browser for a day.</response>
    /// <response code="400">An address this server will not fetch (not https, not port 443, an IP number, a private name); the body says why.</response>
    /// <response code="403">The caller holds Cases Create in no group.</response>
    /// <response code="404">The page could not be read, or the canvas editor is switched off.</response>
    /// <response code="429">More than 30 a minute.</response>
    [HttpGet]
    [EnableRateLimiting(RateLimiting.LinkUnfurlPolicy)]
    [ProducesResponseType(typeof(LinkUnfurlRecord), 200)]
    public async Task<ActionResult<LinkUnfurlRecord>> Get([FromQuery] string url, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (!await MayUnfurlAsync(userId, ct)) return Forbid();

        var outcome = await _unfurl.GetAsync(url, ct);
        switch (outcome.Status)
        {
            case LinkUnfurlStatus.Refused:
                return BadRequest(outcome.Refusal);
            case LinkUnfurlStatus.NotFound:
                return NotFound();
            default:
                Response.Headers.CacheControl = "private, max-age=86400";
                return Ok(outcome.Record);
        }
    }

    // GET /api/link-unfurl/image?url=
    /// <summary>
    /// A link card's picture, fetched under the same guard and answered as our own 800 px JPEG.
    /// </summary>
    /// <param name="url">The picture address from <see cref="LinkUnfurlRecord.ImageSourceUrl"/>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <response code="200">image/jpeg, cached privately by the browser for a week.</response>
    /// <response code="400">An address this server will not fetch.</response>
    /// <response code="403">The caller holds Cases Create in no group.</response>
    /// <response code="404">No displayable picture there, over 2 MB, over 40 million pixels, or unreadable.</response>
    /// <response code="429">More than 120 a minute for this person, or the server-wide ceiling.</response>
    [HttpGet("image")]
    [EnableRateLimiting(RateLimiting.LinkUnfurlImagePolicy)]
    [Produces("image/jpeg")]
    public async Task<IActionResult> Image([FromQuery] string url, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address))
            return BadRequest("Only a full https address can be previewed.");
        if (SafeUrlPolicy.Refuse(address) is { } refusal) return BadRequest(refusal);
        if (!await MayUnfurlAsync(userId, ct)) return Forbid();

        if (!_ceiling.TryTake()) return StatusCode(StatusCodes.Status429TooManyRequests);

        var fetched = await _fetcher.FetchAsync(address, "image/", MaxImageBytes, ct);
        if (fetched.Refusal is not null || fetched.StatusCode != 200 || fetched.Body is not { } body) return NotFound();
        if (!Ben.Data.Common.Helpers.ImageSignature.IsBrowserDisplayable(body)) return NotFound();
        if (!HeaderIsDecodable(body)) return NotFound();

        byte[] jpeg;
        try
        {
            jpeg = _sanitizer.Sanitize(body, ImageLongEdge);
        }
        catch (UnreadableImageException)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=604800";
        return File(jpeg, "image/jpeg");
    }

    /// <summary>
    /// Reads only the picture's header and refuses one that claims more than
    /// <see cref="MaxImagePixels"/> (canvas plan review R21).
    /// </summary>
    /// <remarks>
    /// A PNG of a few hundred bytes can declare 50,000 × 50,000 pixels; decoding it allocates about
    /// ten gigabytes before the sanitiser could notice anything. <see cref="SKCodec.Create(SKStream)"/>
    /// parses the header without decoding a single row, so this costs microseconds.
    /// </remarks>
    private static bool HeaderIsDecodable(byte[] body)
    {
        using var stream = new SKMemoryStream(body);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return false;
        var info = codec.Info;
        return info.Width > 0 && info.Height > 0 && (long)info.Width * info.Height <= MaxImagePixels;
    }

    /// <summary>
    /// Whether the caller may make this server fetch anything: a SuperAdmin, or a member holding Cases
    /// Create in at least one group (canvas plan review R21).
    /// </summary>
    /// <remarks>
    /// The canvas is an investigator's tool, and "can open a case somewhere" is the plainest statement
    /// of being one. Anybody can register an account; an account with no group, or one that only
    /// reads, has no board to paste onto and no business pointing this server at the internet.
    /// </remarks>
    private async Task<bool> MayUnfurlAsync(Guid userId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;

        await using var db = await _db.CreateDbContextAsync(ct);
        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var orgId in orgIds)
        {
            if (await _security.MayAsync(userId, orgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create, ct))
                return true;
        }
        return false;
    }
}
