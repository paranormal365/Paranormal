using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Readable addresses for the shared equipment catalog — <c>/equipment/zoom/h1n</c>.
/// </summary>
/// <remarks>
/// <para>Ben's original complaint, and the last thing in the readable-URL work still wearing a
/// GUID: <i>"we use the GUID for many of the IDs. That is not human readable."</i> A make and model
/// page addressed as <c>/equipment-models/3f2a9c81-…</c> is a link nobody clicks and nobody
/// recognises in their own history.</para>
///
/// <para><b>Regenerated on rename, unlike every other slug here.</b> A case, an event and an
/// organization freeze their address, because somebody chose it and shared it and a URL is a promise
/// to whoever wrote it down. This catalog is different on both counts: the names are the site's own
/// shared vocabulary rather than one group's, and the rename path exists specifically to correct
/// mistakes. A page for a make corrected from "Sansung" to "Samsung" that still answered only to
/// <c>/equipment/sansung</c> would preserve the error in the most visible place there is.</para>
///
/// <para>The cost is that a catalog link shared before a correction stops resolving. Accepted
/// deliberately: these addresses are brand new, so nothing has been shared yet, and the correction
/// is the point. Organizations get the alias treatment instead, because their address is the one
/// that ends up on a business card.</para>
/// </remarks>
public static class EquipmentCatalogSlugs
{
    /// <summary>
    /// Gives a make an address free of any other make's.
    /// </summary>
    /// <remarks>
    /// Falls back to the id when the name yields nothing sluggable — a make named only in
    /// non-Latin script, say. An unreadable address still beats an unreachable page, and the
    /// alternative is refusing a name that is perfectly valid.
    /// </remarks>
    public static async Task AssignAsync(
        BenDataContext db, EquipmentBrand brand, CancellationToken ct)
    {
        var candidate = UrlSlug.From(brand.Name) ?? brand.Id.ToString("N")[..8];

        brand.UrlName = await UrlSlug.MakeUniqueAsync(candidate, async slug =>
            await db.EquipmentBrands.AsNoTracking()
                .AnyAsync(b => b.Id != brand.Id && b.UrlName == slug, ct));
    }

    /// <summary>
    /// Gives a model an address free of any other model's <i>under the same make</i>.
    /// </summary>
    /// <remarks>
    /// Scoped to the make, matching how the names themselves are unique: two manufacturers may both
    /// make an "X1", and <c>/equipment/zoom/x1</c> and <c>/equipment/tascam/x1</c> are different
    /// pages that should not be forced to fight over a suffix.
    /// </remarks>
    public static async Task AssignAsync(
        BenDataContext db, EquipmentModel model, CancellationToken ct)
    {
        var candidate = UrlSlug.From(model.Name) ?? model.Id.ToString("N")[..8];

        model.UrlName = await UrlSlug.MakeUniqueAsync(candidate, async slug =>
            await db.EquipmentModels.AsNoTracking()
                .AnyAsync(m => m.Id != model.Id
                            && m.EquipmentBrandId == model.EquipmentBrandId
                            && m.UrlName == slug, ct));
    }
}
