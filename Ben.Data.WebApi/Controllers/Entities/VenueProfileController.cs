using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The places a group describes as its venue, and what it says about each (item 235 phase 9).
/// </summary>
/// <remarks>
/// <b>Writing a profile proves nothing and gates nothing.</b> Any group may describe a hall it rents
/// every October. What makes other organizers ask first is being verified as the venue there, which
/// is a claim with proof, not a form; and until then the page cannot be published, because a public
/// page headed "the venue" is itself a claim.
/// </remarks>
[Route("api/organizations/{orgId:guid}/venue-profiles")]
public sealed class VenueProfileController : OrgCmsControllerBase
{
    public VenueProfileController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VenueProfileRecord>>> Get(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayEditAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, orgId, ct));
    }

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<VenueProfileRecord>>> Save(
        Guid orgId, [FromBody] SaveVenueProfileRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayEditAsync(userId.Value, orgId, ct)) return Forbid();

        if (request.History is { Length: > 8000 })
            return BadRequest("Keep the building's story under 8,000 characters.");
        if (request.HouseRules is { Length: > 4000 })
            return BadRequest("Keep the house rules under 4,000 characters.");
        if (request.MaxOvernightGuests is < 1 or > 10000)
            return BadRequest("How many may stay overnight has to be a sensible number, or left empty.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.Places.AnyAsync(p => p.Id == request.PlaceId, ct))
            return NotFound("We can't find that place.");

        var profile = await db.OrganizationVenueProfiles
            .FirstOrDefaultAsync(v => v.OrganizationId == orgId && v.PlaceId == request.PlaceId, ct);

        if (request.IsPublished && profile?.VerifiedUtc is null)
            return Conflict("You can publish the venue page once you've been confirmed as the venue there. "
                          + "Until then it's saved for your group only.");

        var now = DateTime.UtcNow;
        if (profile is null)
        {
            profile = new OrganizationVenueProfile
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, PlaceId = request.PlaceId,
                DateCreated = now, CreatedByAppUserId = userId.Value,
            };
            db.OrganizationVenueProfiles.Add(profile);
        }
        else
        {
            profile.DateUpdated = now;
            profile.UpdatedByAppUserId = userId.Value;
        }

        profile.History = Trimmed(request.History);
        profile.HouseRules = Trimmed(request.HouseRules);
        profile.MaxOvernightGuests = request.MaxOvernightGuests;
        profile.IsPublished = request.IsPublished;

        await db.SaveChangesAsync(ct);
        return Ok(await ListAsync(db, orgId, ct));
    }

    private Task<bool> MayEditAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsCmsAuthorizedAsync(userId, orgId,
            OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private static string? Trimmed(string? value) => value?.Trim() is { Length: > 0 } v ? v : null;

    private static async Task<IReadOnlyList<VenueProfileRecord>> ListAsync(BenDataContext db, Guid orgId, CancellationToken ct)
        => await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(v => v.OrganizationId == orgId)
            .OrderBy(v => v.Place.Name)
            .Select(v => new VenueProfileRecord(
                v.Id, v.PlaceId, v.Place.Name ?? "", v.History, v.HouseRules,
                v.MaxOvernightGuests, v.IsPublished, v.VerifiedUtc))
            .ToListAsync(ct);
}
