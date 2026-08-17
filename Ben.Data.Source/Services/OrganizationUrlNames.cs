using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.Source.Services;

/// <summary>
/// Claiming and changing the address an organization lives at.
/// </summary>
/// <remarks>
/// <para>Three things had to be true at once and none of them were. The address had to be
/// <b>shaped like an address</b> — anything at all was accepted, spaces and slashes included. It had
/// to be <b>unique</b> — the create path checked, the rename path did not, and there was no index
/// behind either, so two groups could hold one address and <c>/o/ghost-squad</c> resolved to
/// whichever row came back first. And a change had to <b>keep the old address working</b>, because
/// it is the one part of this product that ends up on a business card.</para>
///
/// <para>Both write paths go through here so they cannot disagree again. That disagreement is
/// exactly how the original collation bug happened: two endpoints writing the same column, one
/// lowercasing and one not.</para>
/// </remarks>
public static class OrganizationUrlNames
{
    /// <summary>
    /// Whether this address can be taken, ignoring one organization's own current name.
    /// </summary>
    /// <param name="exceptOrganizationId">
    /// The organization doing the claiming, so re-saving a form without changing the address is not
    /// a collision with itself.
    /// </param>
    /// <remarks>
    /// <b>Aliases count as taken.</b> An address a group used to have belongs to that group for
    /// good: pointing somebody's saved link at a different group would be worse than the link being
    /// dead, because a broken link says "gone" while a captured one says something false.
    /// </remarks>
    public static async Task<string?> RefusalForAsync(
        BenDataContext db, string? urlName, Guid? exceptOrganizationId, CancellationToken ct)
    {
        var shapeRefusal = UrlNameRules.RefusalFor(urlName);
        if (shapeRefusal is not null) return shapeRefusal;

        var slug = SlugText.NormalizeOrEmpty(urlName);

        var takenByAnother = await db.Organizations.AsNoTracking().AnyAsync(
            o => o.UrlName == slug && (exceptOrganizationId == null || o.Id != exceptOrganizationId), ct);

        if (takenByAnother)
            return $"\"{slug}\" is already another group's web address.";

        var heldAsAlias = await db.OrganizationUrlNameAliases.AsNoTracking().AnyAsync(
            a => a.UrlName == slug
              && (exceptOrganizationId == null || a.OrganizationId != exceptOrganizationId), ct);

        if (heldAsAlias)
            return $"\"{slug}\" was another group's web address before, and old addresses stay with "
                 + "the group that used them so their existing links keep working.";

        return null;
    }

    /// <summary>
    /// Applies a new address, keeping the old one alive as an alias.
    /// </summary>
    /// <remarks>
    /// <para>Call after <see cref="RefusalForAsync"/> has passed. Adds to the change set rather than
    /// saving, so the alias and the rename commit together — a rename that saved without its alias
    /// would break exactly the links this exists to protect.</para>
    ///
    /// <para>An alias the group already holds is not written twice: changing a-to-b-to-a leaves the
    /// group holding both, which the unique index would otherwise refuse.</para>
    /// </remarks>
    public static async Task ApplyAsync(
        BenDataContext db, Organization org, string? newUrlName, Guid? userId, CancellationToken ct)
    {
        var slug = SlugText.NormalizeOrEmpty(newUrlName);
        var previous = SlugText.Normalize(org.UrlName);

        if (previous == slug) return;

        org.UrlName = slug;

        if (previous is null) return;

        var alreadyRecorded = await db.OrganizationUrlNameAliases
            .AnyAsync(a => a.UrlName == previous, ct);

        if (alreadyRecorded) return;

        db.OrganizationUrlNameAliases.Add(new OrganizationUrlNameAlias
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            UrlName            = previous,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
    }

    /// <summary>
    /// Finds an organization by its current address, or by one it used to have.
    /// </summary>
    /// <remarks>
    /// Returns the organization and <c>true</c> when the caller arrived on an old address, so the
    /// endpoint can answer with a permanent redirect rather than serving the page at both — two
    /// addresses for one page splits its search ranking and leaves people copying whichever they
    /// happened to land on.
    /// </remarks>
    public static async Task<(Organization? Organization, bool ViaAlias)> ResolveAsync(
        BenDataContext db, string? urlName, CancellationToken ct)
    {
        var slug = SlugText.NormalizeOrEmpty(urlName);

        var current = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UrlName == slug, ct);

        if (current is not null) return (current, false);

        var alias = await db.OrganizationUrlNameAliases.AsNoTracking()
            .Include(a => a.Organization)
            .FirstOrDefaultAsync(a => a.UrlName == slug, ct);

        return alias is null ? (null, false) : (alias.Organization, true);
    }
}
