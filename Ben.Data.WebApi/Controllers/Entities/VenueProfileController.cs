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

        // No limit on how much a venue writes about its own building (2026-10-05); it was 8,000 and 4,000.
        if (request.MaxOvernightGuests is < 1 or > 10000)
            return BadRequest("How many may stay overnight has to be a sensible number, or left empty.");

        List<(string Title, string Body)>? sections = null;
        if (request.Sections is not null)
        {
            sections = Sections(request.Sections, out var why);
            if (sections is null) return BadRequest(why);
        }

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

        if (sections is not null)
        {
            // The list is the page: what was sent replaces what was there, in the order it was sent.
            db.VenueSections.RemoveRange(await db.VenueSections.Where(x => x.OrganizationVenueProfileId == profile.Id).ToListAsync(ct));
            var order = 0;
            foreach (var (title, body) in sections)
                db.VenueSections.Add(new VenueSection
                {
                    Id = Guid.NewGuid(), OrganizationVenueProfileId = profile.Id, Title = title, Body = body,
                    SortOrder = order++, DateCreated = now, CreatedByAppUserId = userId.Value,
                });
        }

        await db.SaveChangesAsync(ct);
        return Ok(await ListAsync(db, orgId, ct));
    }

    /// <summary>
    /// The sections worth keeping, cleaned: a section with neither a title nor any words is a blank row
    /// somebody added and left, and is dropped; one with words needs a heading to go under.
    /// </summary>
    internal static List<(string Title, string Body)>? Sections(IReadOnlyList<VenueSectionInput> input, out string why)
    {
        why = "";
        var kept = new List<(string, string)>();
        foreach (var section in input)
        {
            var title = Trimmed(section.Title);
            var body = Trimmed(section.Body);
            if (title is null && body is null) continue;
            if (title is null)
            {
                why = "Give each section a heading, so visitors can see what it is about.";
                return null;
            }
            if (title.Length > VenueSection.MaxTitleLength)
            {
                why = $"Keep a section's heading under {VenueSection.MaxTitleLength} characters — the words go underneath it.";
                return null;
            }
            kept.Add((title, body ?? ""));
        }
        return kept;
    }

    private Task<bool> MayEditAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsCmsAuthorizedAsync(userId, orgId,
            OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private static string? Trimmed(string? value) => value?.Trim() is { Length: > 0 } v ? v : null;

    private static async Task<IReadOnlyList<VenueProfileRecord>> ListAsync(BenDataContext db, Guid orgId, CancellationToken ct)
    {
        var profiles = await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(v => v.OrganizationId == orgId)
            .OrderBy(v => v.Place.Name)
            .Select(v => new VenueProfileRecord(
                v.Id, v.PlaceId, v.Place.Name ?? "", v.History, v.HouseRules,
                v.MaxOvernightGuests, v.IsPublished, v.VerifiedUtc, null))
            .ToListAsync(ct);

        var ids = profiles.Select(p => p.Id).ToList();
        var sections = (await db.VenueSections.AsNoTracking()
                .Where(x => ids.Contains(x.OrganizationVenueProfileId))
                .Select(x => new { x.OrganizationVenueProfileId, Record = new VenueSectionRecord(x.Id, x.Title, x.Body, x.SortOrder) })
                .ToListAsync(ct))
            .GroupBy(x => x.OrganizationVenueProfileId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<VenueSectionRecord>)g.Select(x => x.Record).OrderBy(r => r.SortOrder).ToList());

        return profiles.Select(p => p with { Sections = sections.GetValueOrDefault(p.Id, []) }).ToList();
    }
}
