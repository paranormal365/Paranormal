using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.LinkPreviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The card for a link a signed-in person is about to post (beta feedback, 2026-09-14).
/// </summary>
/// <remarks>
/// The one door through which somebody can ask this server to read another site's page, which is why it is signed-in
/// only and budgeted per person (<see cref="LinkPreviewService.FetchesPerMinute"/>). The website asks here when a link
/// is pasted into a composer or a research page, so the card is usually ready by the time the post is sent.
/// </remarks>
[ApiController]
[Route("api/link-previews")]
[Authorize]
public sealed class LinkPreviewsController(ILinkPreviewService previews) : BenControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Ben.Service.Models.Entities.LinkPreview>> Create([FromBody] CreateLinkPreviewRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (LinkPreviewService.Normalise(request.Url) is null) return BadRequest("That isn't a web address.");

        var kept = await previews.GetOrFetchAsync(request.Url, userId, request.Refresh, ct);
        return kept is { Fetched: true } ? Ok(ToCard(kept)) : NotFound();
    }

    /// <summary>A kept preview as the card the website and the app draw.</summary>
    public static Ben.Service.Models.Entities.LinkPreview ToCard(StoredLinkPreview p) => new(
        Kind: p.SiteName ?? p.Domain,
        Title: p.Title ?? p.Domain,
        Subtitle: null,
        Path: p.Url,
        Description: p.Description,
        ImageUrl: p.ThumbnailStoragePath is null ? null : LinkPreviewService.ThumbnailRelativeUrl(p.Id),
        SiteName: p.SiteName,
        Domain: p.Domain);
}

public sealed record CreateLinkPreviewRequest(string Url, bool Refresh = false);

/// <summary>
/// The picture of a kept link preview. Anonymous: it is our own small copy of a picture the other site published for
/// exactly this purpose, and a card is drawn for readers who are not signed in.
/// </summary>
[ApiController]
[Route("api/public/link-previews")]
[AllowAnonymous]
public sealed class PublicLinkPreviewThumbnailController(IDbContextFactory<BenDataContext> db, IFileStorageService storage) : ControllerBase
{
    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> Thumbnail(Guid id, CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var preview = await context.LinkPreviews.AsNoTracking()
            .Where(p => p.Id == id && p.ThumbnailStoragePath != null)
            .Select(p => new { p.ThumbnailStoragePath, p.ThumbnailContentType })
            .FirstOrDefaultAsync(ct);
        if (preview is null) return NotFound();

        try
        {
            var stream = await storage.OpenReadAsync(preview.ThumbnailStoragePath!, ct);
            Response.Headers.CacheControl = "public, max-age=86400";
            return File(stream, preview.ThumbnailContentType ?? "image/jpeg");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }
}
