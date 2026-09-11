using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// What a link in a message is pointing at (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// <para><b>Nothing here fetches anything.</b> A preview that went and read the target page would
/// be a request this server makes to an address a stranger chose, which is the shape of every
/// server-side request forgery there has ever been — an internal address, a cloud metadata
/// endpoint, a slow host that ties up a thread. So this answers for OUR OWN addresses, out of our
/// own records, and says nothing about anybody else's beyond the host they name.</para>
///
/// <para>That is also the better preview. A case's title, its group and its status are facts we
/// hold, correct at the moment of asking, and they stay correct when the case is renamed — which
/// is more than a snapshot of somebody's OpenGraph tags would manage.</para>
/// </remarks>
[ApiController]
[Route("api/public/link-preview")]
[AllowAnonymous]
public sealed class PublicLinkPreviewController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IConfiguration _configuration;

    public PublicLinkPreviewController(IDbContextFactory<BenDataContext> db, IConfiguration configuration)
    { _db = db; _configuration = configuration; }

    /// <summary>What we can say about <paramref name="url"/>, or 404 when it is not one of ours.</summary>
    [HttpGet]
    public async Task<ActionResult<LinkPreview>> Get(
        [FromQuery] string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return BadRequest();

        // A relative path is ours by construction; an absolute one is ours only if it names this
        // site. Anything else gets no lookup at all.
        var path = PathOfOurs(url);
        if (path is null) return NotFound();

        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        await using var db = await _db.CreateDbContextAsync(ct);

        // /o/{org}/cases/{slug}
        if (parts is ["o", var orgSlug, "cases", var caseSlug])
        {
            // The case route accepts either the readable slug or the old "2026-042" reference,
            // and both are addresses people share — so the preview has to recognise both, or a
            // link that opens perfectly well gets no card.
            var slug = Ben.Data.Common.SlugText.NormalizeOrEmpty(caseSlug);
            var reference = caseSlug.TrimStart('#').Split('-');
            var year = reference.Length == 2 && int.TryParse(reference[0], out var y) ? y : (int?)null;
            var number = reference.Length == 2 && int.TryParse(reference[1], out var n) ? n : (int?)null;

            var found = await db.Cases.AsNoTracking()
                .Where(c => c.Organization.UrlName == orgSlug
                         && (c.UrlName == slug
                             || (year != null && c.CaseYear == year && c.OrgCaseNumber == number))
                         && c.IsPublic
                         && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted))
                .Select(c => new LinkPreview(
                    "Case", c.Title,
                    $"{c.Organization.Name} · {c.City}, {c.State}",
                    path))
                .FirstOrDefaultAsync(ct);
            return found is null ? NotFound() : Ok(found);
        }

        // /o/{org}/tours/{slug}
        if (parts is ["o", var tourOrg, "tours", var tourSlug])
        {
            var found = await db.Tours.AsNoTracking()
                .Where(t => t.Organization.UrlName == tourOrg
                         && t.UrlName == tourSlug
                         && t.RetiredAtUtc == null)
                .Select(t => new LinkPreview(
                    "Tour", t.Name,
                    $"{t.Organization.Name} · {t.StartOrganizationAddress.City}, {t.StartOrganizationAddress.State}",
                    path))
                .FirstOrDefaultAsync(ct);
            return found is null ? NotFound() : Ok(found);
        }

        // /o/{org}/events/{slug}
        if (parts is ["o", var evOrg, "events", var evSlug])
        {
            var found = await db.OrgCalendarEvents.AsNoTracking()
                .Where(e => e.Organization.UrlName == evOrg && e.UrlName == evSlug && e.IsPublic)
                .Select(e => new LinkPreview(
                    "Event", e.Title, e.Organization.Name, path))
                .FirstOrDefaultAsync(ct);
            return found is null ? NotFound() : Ok(found);
        }

        // /o/{org}
        if (parts is ["o", var groupSlug])
        {
            // The kind, not the slug: repeating the address under the name says nothing a reader
            // cannot already see in the link above the card.
            var found = await db.Organizations.AsNoTracking()
                .Where(o => o.UrlName == groupSlug)
                .Select(o => new { o.Name, o.Kind })
                .FirstOrDefaultAsync(ct);

            return found is null
                ? NotFound()
                : Ok(new LinkPreview(
                    "Group", found.Name, OrganizationKindDefaults.DisplayName(found.Kind), path));
        }

        return NotFound();
    }

    /// <summary>
    /// The path part of <paramref name="url"/> when it points at this site, else null.
    /// </summary>
    /// <remarks>
    /// <para>A root-relative link is ours by construction — it can only resolve against this
    /// site. An absolute one has to name the SITE's host, which is <c>AppBaseUrl</c> and not the
    /// host this request arrived on: the API answers on its own address, and the first version of
    /// this compared against that, so every link to our own website came back as a stranger's.</para>
    ///
    /// <para>Compared whole, never by "contains": a <c>Contains("ishaunted")</c> test would accept
    /// <c>https://ishaunted.com.example.net/</c>, which is somebody else's domain wearing ours.</para>
    /// </remarks>
    private string? PathOfOurs(string url)
    {
        var trimmed = url.Trim();

        if (trimmed.StartsWith('/')) return trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var ours = new List<string>();
        if (Uri.TryCreate(_configuration["AppBaseUrl"], UriKind.Absolute, out var site))
            ours.Add(site.Host);
        ours.Add(Request.Host.Host);    // an API served from the same host as the site

        return ours.Any(h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase))
            ? uri.AbsolutePath
            : null;
    }
}
