using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The picture of a pass (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>Anonymous, on purpose.</b> This is what an <c>&lt;img&gt;</c> in a letter points at and
/// what a printed page fetches, and neither carries a session. Requiring one would mean a pass
/// that only worked while the guest was signed in on the device they happened to be holding, which
/// is not a pass.</para>
///
/// <para><b>The token is the secret, and the picture only re-draws what the holder already has.</b>
/// Anybody who can reach this URL already knows the token, so serving the image adds nothing they
/// did not have. What the token is worth is bounded by design: it admits one party to one event,
/// it can be revoked in a keystroke, and the scan endpoint behind it takes a signed-in member of
/// the venue.</para>
///
/// <para><b>It is in the path rather than a body</b> because an image tag cannot POST. That does
/// put the token in access logs, which is the same trade the confirmation links on this site
/// already make, and is why the token carries nothing and can be withdrawn.</para>
/// </remarks>
[ApiController]
[Route("api/public/event-passes")]
public sealed class PublicEventPassController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicEventPassController(IDbContextFactory<BenDataContext> db) { _db = db; }

    /// <summary>The pass as a PNG.</summary>
    /// <remarks>
    /// <para>A revoked pass still draws. A guest holding one has to be able to show it to somebody
    /// who can tell them it was replaced — a picture that vanished would look like a broken site
    /// rather than a withdrawn ticket, and the door's own answer is the place that says so.</para>
    ///
    /// <para>Never cached by a shared cache. A pass belongs to one party, and a proxy holding one
    /// person's ticket to hand to the next request is the one caching mistake that matters here.</para>
    /// </remarks>
    [HttpGet("{token}.png")]
    [AllowAnonymous]
    public async Task<IActionResult> Image(string token, [FromQuery] int? size, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.HostedEventPasses.AsNoTracking().AnyAsync(p => p.Token == token, ct))
            return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        return File(EventPasses.Png(token, size ?? 8), "image/png");
    }
}
