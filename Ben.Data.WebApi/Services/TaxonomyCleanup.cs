using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Clears away a proposed brand or model that nothing uses any more.
/// </summary>
/// <remarks>
/// <para>Ben's case: somebody adds a piece of equipment, types <b>Sansung</b> by mistake, then
/// deletes the item. Without this the typo outlives them — the model and the brand stay in the
/// shared catalog forever, and the member who created them cannot remove them, because rejecting
/// taxonomy is a SuperAdmin action. Everybody who adds a Samsung recorder afterwards is offered two
/// manufacturers to choose between, and the wrong one looks exactly as real as the right one.</para>
///
/// <para><b>Only unapproved entries, and only when nothing points at them.</b> An approved brand is
/// shared vocabulary — it stays whether or not anybody currently owns one, because the catalog is
/// meant to describe what exists in the world, not only what is in the database this week. An
/// unapproved one has no such claim: it is somebody's guess that has not been endorsed and is now
/// unused.</para>
///
/// <para><b>Immediately, rather than after a grace period.</b> A sweep on a timer would need a
/// scheduler this app does not have, and the grace period would only matter if somebody wanted to
/// re-add the same thing minutes later — in which case proposing it again simply recreates it. The
/// only thing lost is who first proposed a name nobody kept.</para>
/// </remarks>
public static class TaxonomyCleanup
{
    /// <summary>
    /// Removes the model, and then the brand, if each is unapproved and now unreferenced.
    /// </summary>
    /// <remarks>
    /// Cascades upward on purpose: deleting the only Sansung item should take the model with it, and
    /// then the brand that existed only to hold that model. Stopping at the model would leave the
    /// typo exactly where somebody will see it.
    ///
    /// <para>Called after the referencing row is removed and saved, so the counts it checks are the
    /// truth rather than a prediction.</para>
    /// </remarks>
    public static async Task RemoveOrphanedTaxonomyAsync(
        BenDataContext db, Guid equipmentModelId, CancellationToken ct)
    {
        var model = await db.EquipmentModels
            .FirstOrDefaultAsync(m => m.Id == equipmentModelId, ct);

        if (model is null || model.IsApproved) return;

        if (await db.EquipmentItems.AnyAsync(i => i.EquipmentModelId == model.Id, ct)) return;

        var brandId = model.EquipmentBrandId;
        db.EquipmentModels.Remove(model);
        await db.SaveChangesAsync(ct);

        var brand = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Id == brandId, ct);
        if (brand is null || brand.IsApproved) return;

        if (await db.EquipmentModels.AnyAsync(m => m.EquipmentBrandId == brand.Id, ct)) return;

        db.EquipmentBrands.Remove(brand);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Removes any of these experience types that a group proposed, no human has reviewed, and
    /// nothing is tagged with any more.
    /// </summary>
    /// <remarks>
    /// <para>The same story as the equipment brand above, one taxonomy over: somebody tags tonight's
    /// occurrence with a type they had to invent, mistypes it, then unticks it. Without this the
    /// mistyping stays in the picker forever — and unlike equipment, a group has <i>no</i> way at
    /// all to remove it, because the only delete lives behind an app-administrator screen.</para>
    ///
    /// <para><b>Unreviewed means approved with no approver.</b> An org-proposed type goes live
    /// immediately with <c>ApprovedByAppUserId</c> left null, and that null is the whole marker.
    /// Testing <c>IsApproved</c> alone would sweep away words an administrator had deliberately
    /// endorsed, which is the opposite of the intent.</para>
    ///
    /// <para><b>Only types a group proposed.</b> A seeded type that nothing happens to be tagged
    /// with today is still shared vocabulary — the taxonomy describes what people experience, not
    /// what is in this database this week.</para>
    ///
    /// <para>Called after the tagging rows are removed and saved, so the counts are the truth
    /// rather than a prediction.</para>
    /// </remarks>
    public static async Task RemoveOrphanedExperienceTypesAsync(
        BenDataContext db, IReadOnlyCollection<Guid> experienceTypeIds, CancellationToken ct)
    {
        if (experienceTypeIds.Count == 0) return;

        var orphans = await db.ExperienceTypes
            .Where(t => experienceTypeIds.Contains(t.Id)
                     && t.ProposedByOrganizationId != null
                     && t.ApprovedByAppUserId == null
                     && !db.CaseTimelineEntryExperienceTypes.Any(x => x.ExperienceTypeId == t.Id))
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        db.ExperienceTypes.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);
    }
}
