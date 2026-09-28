using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Fills in the clock a case or investigation actually reads on — its own, else its case's, else
/// its group's (2026-09-28).
/// </summary>
/// <remarks>
/// <para><b>Why not in the mapping.</b> The records map straight from the entity, and the case
/// profile already warns that a mapped navigation is null "unless the caller included" it — the
/// case page drew a blank where a place name belonged that way. An effective zone that silently
/// fell back to Chicago whenever a query forgot <c>.Include(c =&gt; c.Organization)</c> would be the
/// same bug with worse consequences, so this looks the zones up itself, one query for however many
/// records there are.</para>
/// </remarks>
public static class ZoneChain
{
    public static async Task<IReadOnlyList<CaseRecord>> FillAsync(
        BenDataContext db, IEnumerable<CaseRecord> cases, CancellationToken ct)
    {
        var list = (cases ?? []).Where(c => c is not null).ToList();
        var groups = await GroupZonesAsync(db, list.Select(c => c.OrganizationId), ct);
        return list.Select(c => c with
        {
            EffectiveTimeZoneId = Zones.Effective(c.TimeZoneId, groups.GetValueOrDefault(c.OrganizationId)),
        }).ToList();
    }

    // A null passes straight through: a mapper double in a test hands one back, and a lookup is
    // never worth a crash on the way out of a save that has already succeeded.
    public static async Task<CaseRecord> FillAsync(BenDataContext db, CaseRecord record, CancellationToken ct) =>
        record is null ? record! : (await FillAsync(db, [record], ct))[0];

    public static async Task<IReadOnlyList<InvestigationRecord>> FillAsync(
        BenDataContext db, IEnumerable<InvestigationRecord> investigations, CancellationToken ct)
    {
        var list = (investigations ?? []).Where(i => i is not null).ToList();
        var groups = await GroupZonesAsync(db, list.Select(i => i.OrganizationId), ct);
        var caseIds = list.Where(i => i.CaseId.HasValue).Select(i => i.CaseId!.Value).Distinct().ToList();
        var cases = caseIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.Cases.AsNoTracking().Where(c => caseIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.TimeZoneId, ct);
        return list.Select(i => i with
        {
            CaseTimeZoneId = i.CaseId is { } cid ? cases.GetValueOrDefault(cid) : null,
            EffectiveTimeZoneId = Zones.Effective(
                i.TimeZoneId,
                i.CaseId is { } caseId ? cases.GetValueOrDefault(caseId) : null,
                groups.GetValueOrDefault(i.OrganizationId)),
        }).ToList();
    }

    public static async Task<InvestigationRecord> FillAsync(
        BenDataContext db, InvestigationRecord record, CancellationToken ct) =>
        record is null ? record! : (await FillAsync(db, [record], ct))[0];

    /// <summary>The zone a new case starts on, or a new investigation of <paramref name="caseId"/>.</summary>
    public static async Task<string> ForNewAsync(BenDataContext db, Guid orgId, Guid? caseId, CancellationToken ct)
    {
        var group = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId)
            .Select(o => o.TimeZoneId).FirstOrDefaultAsync(ct);
        var ofCase = caseId is { } id
            ? await db.Cases.AsNoTracking().Where(c => c.Id == id).Select(c => c.TimeZoneId).FirstOrDefaultAsync(ct)
            : null;
        return Zones.Effective(ofCase, group);
    }

    private static async Task<Dictionary<Guid, string>> GroupZonesAsync(
        BenDataContext db, IEnumerable<Guid> orgIds, CancellationToken ct)
    {
        var ids = orgIds.Distinct().ToList();
        return await db.Organizations.AsNoTracking().Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.TimeZoneId, ct);
    }
}
