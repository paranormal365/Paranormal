using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Somebody walking through a group's invitation link.
/// </summary>
/// <remarks>
/// <para><b>Reading the invitation and accepting it are two calls</b>, and only the second changes
/// anything — the same rule the event-staff invitation keeps, and for the same reason: a link
/// prefetched by a mail client, a chat app's preview fetcher or a scanner must not enrol anybody.
/// It takes a person pressing a button on a page that first told them whose group this is.</para>
///
/// <para><b>Reading is anonymous; joining needs an account.</b> Unlike the event door, this one
/// does not make a passwordless account from an address: a member of a group holds case material
/// about real people at real addresses, and the click on a shared link proves nothing about who
/// is holding the phone. So the page says which group is asking, and then asks them to sign in or
/// sign up — and the account they arrive with is the one that joins.</para>
///
/// <para><b>The plan is checked here, at the moment of joining</b>, through the one method that
/// answers it everywhere — <c>PaidPlan.WhyCannotAddMemberAsync</c>. A link issued on a free plan
/// and used after the group subscribes works; a link used while the group is still free comes back
/// 402 carrying the plan's own sentence, which the page shows to the person who clicked and, more
/// usefully, is the same sentence the group was shown before they shared it.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/public/join")]
public sealed class PublicOrganizationJoinController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicOrganizationJoinController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Whose group this is, before anybody joins anything.</summary>
    /// <remarks>
    /// The one anonymous door, granted per action rather than per controller. Granting it on the
    /// controller silently overrides the <c>[Authorize]</c> on <see cref="Accept"/> — the compiler
    /// says so (ASP0026) and it is easy to read past, which is how an endpoint ends up with a
    /// credential check that is not there.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("{token}")]
    public async Task<ActionResult<OrganizationJoinInviteRecord>> Get(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var org = await LoadAsync(db, token, ct);
        if (org is null) return NotFound();

        var me = GetCurrentUserIdOrNull();
        var already = me is { } id
            && await db.OrganizationUserMemberships
                .AnyAsync(m => m.OrganizationId == org.Id && m.AppUserId == id && m.IsActive, ct);

        return Ok(new OrganizationJoinInviteRecord
        {
            OrganizationId   = org.Id,
            OrganizationName = org.Name,
            OrganizationUrlName = org.UrlName,
            ExpiresUtc       = org.JoinTokenExpiresUtc,
            AlreadyAMember   = already,
        });
    }

    /// <summary>Joins the group.</summary>
    /// <remarks>
    /// <para><b>The link is not spent by being used.</b> It is one link for a whole group of
    /// people — the organiser pastes it into the chat they already have — so it keeps working
    /// until it expires or is replaced. That is the difference between this and the event-staff
    /// invitation, which names one person and is cleared on acceptance.</para>
    ///
    /// <para>Everything a newly accepted application gets, a join through this link gets, because
    /// they are the same event with the consent given at different ends: the starting role the
    /// group chose (without it a new member lands on a desk of links that answer 403), the
    /// personal-group flag dropped now that there are two of them, and item 144's overflow seat
    /// when the join goes past the group's frozen band.</para>
    /// </remarks>
    [HttpPost("{token}")]
    [Authorize]
    public async Task<ActionResult<OrganizationJoinInviteRecord>> Accept(string token, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var org = await LoadAsync(db, token, ct);
        if (org is null) return NotFound();

        var already = await db.OrganizationUserMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == org.Id && m.AppUserId == userId.Value, ct);

        if (already is { IsActive: true })
            return Ok(Describe(org, alreadyAMember: true));

        // One person is free; working with other people is the paid part. The same gate the
        // acceptance path runs, called the same way, so there is one answer on the site.
        if (await Services.Billing.PaidPlan.WhyCannotAddMemberAsync(db, org.Id, ct) is { } needsPlan)
            return StatusCode(StatusCodes.Status402PaymentRequired, needsPlan);

        // A personal organization that gains a second person has become a group, and should stop
        // being hidden from the places groups are found.
        if (org.IsPersonal)
        {
            org.IsPersonal = false;
            org.DateUpdated = DateTime.UtcNow;
            org.UpdatedByAppUserId = userId.Value;
        }

        if (already is not null)
        {
            // A lapsed membership is revived rather than duplicated: the row carries history.
            already.IsActive = true;
            already.DateUpdated = DateTime.UtcNow;
            already.UpdatedByAppUserId = userId.Value;
        }
        else
        {
            var membership = new OrganizationUserMembership
            {
                Id                 = Guid.NewGuid(),
                OrganizationId     = org.Id,
                AppUserId          = userId.Value,
                Role               = OrganizationMemberRole.Member,
                IsActive           = true,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId.Value,
            };
            db.OrganizationUserMemberships.Add(membership);

            await Ben.Data.Source.Services.MemberDefaultRole.ApplyAsync(
                db, org.Id, membership, userId.Value, ct);
        }

        await db.SaveChangesAsync(ct);

        await Ben.Data.WebApi.Services.Billing.OverflowSeats.MaybeOfferSeatAsync(
            db, org.Id, userId.Value, userId.Value, ct);

        return Ok(Describe(org, alreadyAMember: true));
    }

    /// <summary>The group a live, unexpired token belongs to, or null.</summary>
    private static async Task<Organization?> LoadAsync(
        BenDataContext db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.JoinToken == token, ct);
        if (org is null) return null;

        // An expired link is indistinguishable from a wrong one, deliberately: neither tells a
        // stranger holding a stale link whether the group exists.
        return org.JoinTokenExpiresUtc is { } until && until > DateTime.UtcNow ? org : null;
    }

    private static OrganizationJoinInviteRecord Describe(Organization org, bool alreadyAMember)
        => new()
        {
            OrganizationId      = org.Id,
            OrganizationName    = org.Name,
            OrganizationUrlName = org.UrlName,
            ExpiresUtc          = org.JoinTokenExpiresUtc,
            AlreadyAMember      = alreadyAMember,
        };
}
