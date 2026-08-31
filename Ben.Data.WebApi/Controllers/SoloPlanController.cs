using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The door a single investigator walks through to start paying for themselves.
/// </summary>
/// <remarks>
/// <para><b>Why this creates an organization.</b> Everything the solo tier sells is org-scoped:
/// cases, subscriptions, privacy, private-residence work. A person with no group has nothing to
/// hang any of it on, so this mints a personal organization for them and every existing feature
/// works unchanged. The alternative — an account-level parallel of each — is the same rules
/// written twice, and two copies of a rule is how the copies come to disagree.</para>
///
/// <para><b>It does not take payment.</b> It produces the organization that checkout needs, and
/// the ordinary billing flow does the rest. Minting an organization and charging a card in one
/// endpoint would mean a failed payment leaves a half-made group behind, and the recovery for
/// that is worse than the extra round trip.</para>
///
/// <para><b>Idempotent by design.</b> Somebody who taps twice, or who comes back after abandoning
/// checkout, gets the organization they already have rather than a second one. A person cannot
/// hold two personal organizations: the second would be an orphan carrying its own cases that no
/// screen would ever show them.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/solo-plan")]
public sealed class SoloPlanController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public SoloPlanController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>The personal organization behind this account's solo plan, or null if none.</summary>
    [HttpGet]
    public async Task<ActionResult<SoloPlanRecord?>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var existing = await FindAsync(db, userId, ct);

        return Ok(existing is null ? null : new SoloPlanRecord(existing.Id, existing.Name));
    }

    /// <summary>
    /// Creates this account's personal organization, or returns the one it already has.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SoloPlanRecord>> Start(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        if (await FindAsync(db, userId, ct) is { } already)
            return Ok(new SoloPlanRecord(already.Id, already.Name));

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            // Named for the person, because the only places this name is ever shown are their own
            // billing page and their own case list. It is not a brand and nobody else will read it.
            Name = string.IsNullOrWhiteSpace(user.DisplayName) ? "My investigations" : user.DisplayName!,
            UrlName = await UniqueUrlNameAsync(db, user, ct),
            IsPersonal = true,
            // Not accepting clients and not running tours: a personal organization is nobody's
            // service provider. The public listings already exclude it, and these make the record
            // itself say so rather than relying on the filter to be the only truth.
            IsAcceptingClients = false,
            IsAcceptingApplications = false,
            RunsPublicTours = false,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        db.Organizations.Add(org);

        // A personal organization gets the full skeleton — roles, levels, duties, event types.
        // It is a real organization that happens to have one member, and a reduced version would
        // mean a second code path in every feature it touches.
        NewOrganizationDefaults.AddAll(db, org.Id, userId);

        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            AppUserId = userId,
            Role = OrganizationMemberRole.Owner,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });

        await db.SaveChangesAsync(ct);

        return Ok(new SoloPlanRecord(org.Id, org.Name));
    }

    /// <summary>This account's personal organization, if it has one.</summary>
    /// <remarks>
    /// Found through the membership rather than <c>CreatedByAppUserId</c>: a SuperAdmin creating
    /// one on somebody's behalf must produce the same answer as the person creating their own.
    /// </remarks>
    private static Task<Organization?> FindAsync(BenDataContext db, Guid userId, CancellationToken ct)
        => db.Organizations.AsNoTracking()
            .Where(o => o.IsPersonal)
            .FirstOrDefaultAsync(
                o => db.OrganizationUserMemberships
                    .Any(m => m.OrganizationId == o.Id && m.AppUserId == userId && m.IsActive), ct);

    /// <summary>
    /// A URL name nobody is using, derived from the handle and then disambiguated.
    /// </summary>
    /// <remarks>
    /// A personal organization has no public page worth visiting, but <c>UrlName</c> is unique and
    /// non-null for every organization, so it still needs one that cannot collide — and cannot
    /// capture the traffic of a real group's name (item 89).
    /// </remarks>
    private static async Task<string> UniqueUrlNameAsync(
        BenDataContext db, AppUser user, CancellationToken ct)
    {
        var seed = Ben.Data.Common.SlugText.NormalizeOrEmpty(
            string.IsNullOrWhiteSpace(user.Handle) ? "solo" : user.Handle!);
        if (seed.Length == 0) seed = "solo";

        var candidate = $"{seed}-solo";
        // Suffixed until free rather than trusting the first try: two people may share a handle
        // shape, and a unique-index violation here would surface as a failed sign-up.
        for (var attempt = 2; await db.Organizations.AnyAsync(o => o.UrlName == candidate, ct); attempt++)
            candidate = $"{seed}-solo-{attempt}";

        return candidate;
    }

    /// <param name="OrganizationId">What checkout, billing and case creation all key off.</param>
    public sealed record SoloPlanRecord(Guid OrganizationId, string Name);
}
