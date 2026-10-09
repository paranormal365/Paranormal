using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Whether each of my groups may name me on its public pages (backlog 256, 10/09/2026).
/// </summary>
/// <remarks>
/// A group's "Our members" section shows only the people who said yes here. It is the member's choice, not
/// the group's: the group decides which of the willing to show, and nobody else can turn this on for them.
/// Personal spaces are left out — they have no public members page to be named on.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/public-listing")]
public sealed class MyPublicListingController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MyPublicListingController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>My active groups, and whether each may name me publicly.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MyPublicListing>>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        return Ok(await (
                from m in db.OrganizationUserMemberships.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
                where m.AppUserId == userId && m.IsActive && !o.IsPersonal
                orderby o.Name
                select new MyPublicListing(m.OrganizationId, o.Name, m.ShowOnPublicPages))
            .ToListAsync(ct));
    }

    /// <summary>Says yes or no for one group.</summary>
    [HttpPut("{organizationId:guid}")]
    public async Task<IActionResult> Set(Guid organizationId, [FromBody] SetMyPublicListing request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var membership = await db.OrganizationUserMemberships
            .FirstOrDefaultAsync(m => m.AppUserId == userId && m.OrganizationId == organizationId && m.IsActive, ct);
        if (membership is null) return NotFound();

        membership.ShowOnPublicPages = request.Show;
        membership.DateUpdated = DateTime.UtcNow;
        membership.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>One of my groups, and whether it may name me on its public pages.</summary>
public sealed record MyPublicListing(Guid OrganizationId, string OrganizationName, bool ShowOnPublicPages);

/// <summary>Yes or no.</summary>
public sealed record SetMyPublicListing(bool Show);
