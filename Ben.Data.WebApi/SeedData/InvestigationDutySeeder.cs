using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Backfills default investigation duties for organizations that predate item 158, then
/// structures the legacy per-attendee lead/role data into duty assignments.
/// </summary>
/// <remarks>
/// <para><b>Backfill:</b> a group with ANY duties is left alone (an edited list is never
/// touched); everyone else gets the four defaults.</para>
///
/// <para><b>Legacy migration, idempotent:</b> <c>InvestigationAttendee.IsLead</c> becomes a
/// Lead Investigator assignment, and a free-text <c>AssignedRole</c> that matches one of the
/// org's duty names (case-insensitive) becomes an assignment for that duty; non-matching text
/// survives untouched as the note it always was. Attendees who already hold the assignment are
/// skipped, so restarting the host never duplicates a row. IsLead itself is kept — the Lead
/// duty writes through to it, and InvestigationAccess keeps reading it unchanged.</para>
/// </remarks>
internal static class InvestigationDutySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // ── Backfill the four defaults ────────────────────────────────────────
        var orgsWithDuties = await db.InvestigationDuties
            .Select(d => d.OrganizationId).Distinct().ToListAsync();
        var bare = await db.Organizations
            .Where(o => !orgsWithDuties.Contains(o.Id))
            .Select(o => new { o.Id, o.CreatedByAppUserId })
            .ToListAsync();
        foreach (var org in bare)
            OrgInvestigationDutyDefaults.AddDefaultDuties(db, org.Id, org.CreatedByAppUserId);
        if (bare.Count > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[InvestigationDutySeeder] Backfilled duties for {bare.Count} organization(s).");
        }

        // ── Structure legacy IsLead + AssignedRole into assignments ───────────
        var duties = await db.InvestigationDuties.AsNoTracking().ToListAsync();
        var dutiesByOrg = duties.GroupBy(d => d.OrganizationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var attendees = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.IsLead || a.AssignedRole != null)
            .Select(a => new { a.Id, a.IsLead, a.AssignedRole, a.CreatedByAppUserId, a.Investigation.OrganizationId })
            .ToListAsync();

        var existing = await db.InvestigationDutyAssignments.AsNoTracking()
            .Select(x => new { x.InvestigationAttendeeId, x.InvestigationDutyId })
            .ToListAsync();
        var have = existing.Select(x => (x.InvestigationAttendeeId, x.InvestigationDutyId)).ToHashSet();

        var added = 0;
        foreach (var a in attendees)
        {
            if (!dutiesByOrg.TryGetValue(a.OrganizationId, out var orgDuties)) continue;

            var wanted = new List<InvestigationDuty>();
            if (a.IsLead)
            {
                var lead = orgDuties.FirstOrDefault(d =>
                    d.Name.Equals(OrgInvestigationDutyDefaults.LeadDutyName, StringComparison.OrdinalIgnoreCase));
                if (lead is not null) wanted.Add(lead);
            }
            if (!string.IsNullOrWhiteSpace(a.AssignedRole))
            {
                var match = orgDuties.FirstOrDefault(d =>
                    d.Name.Equals(a.AssignedRole.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is not null && !wanted.Contains(match)) wanted.Add(match);
            }

            foreach (var duty in wanted)
            {
                if (!have.Add((a.Id, duty.Id))) continue;
                db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
                {
                    InvestigationAttendeeId = a.Id,
                    InvestigationDutyId = duty.Id,
                    DateCreated = DateTime.UtcNow,
                    CreatedByAppUserId = a.CreatedByAppUserId,
                });
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[InvestigationDutySeeder] Structured {added} legacy lead/role value(s) into duty assignments.");
        }
    }
}
