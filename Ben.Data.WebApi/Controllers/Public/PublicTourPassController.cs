using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A tour guest's pass, as a picture (item 247).
/// </summary>
/// <remarks>
/// <para><b>Anonymous, because a mail client is.</b> The letter draws the pass as a data URI
/// first — a linked picture a mail client blocked is a guest with no pass — and links here as the
/// fallback, for the client that strips data URIs instead. Neither can sign in.</para>
///
/// <para>It does put the token in access logs, which is the trade every confirmation link on this
/// site already makes, and is why the token carries nothing: it names no guest, no walk and no
/// group, and it can be replaced.</para>
/// </remarks>
[ApiController]
[Route("api/public/tour-passes")]
public sealed class PublicTourPassController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicTourPassController(IDbContextFactory<BenDataContext> db) { _db = db; }

    /// <summary>The pass as a PNG.</summary>
    /// <remarks>
    /// <para>A replaced pass still draws. A guest holding one has to be able to show it to
    /// somebody who can tell them it was reissued — a picture that vanished looks like a broken
    /// site rather than a replaced ticket, and the guide's own scan is where that is said.</para>
    ///
    /// <para>Never cached by a shared cache. A pass belongs to one guest, and a proxy handing one
    /// person's ticket to the next request is the caching mistake that matters here.</para>
    /// </remarks>
    [HttpGet("{token}.png")]
    [AllowAnonymous]
    public async Task<IActionResult> Image(string token, [FromQuery] int? size, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.OrgCalendarEventAttendees.AsNoTracking().AnyAsync(a => a.PassToken == token, ct))
            return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        return File(EventPasses.Png(token, size ?? 8), "image/png");
    }
}
