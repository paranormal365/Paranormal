using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Backfills the default roles for organizations that have none.
/// </summary>
/// <remarks>
/// <para><b>Backfill:</b> a group with ANY roles is left entirely alone (its role list is its
/// own); a group with none gets the defaults. Roles are CREATED here; nobody is put in
/// them.</para>
///
/// <para><b>The grandfathering is gone (Ben, 2026-08-26).</b> This used to also create an
/// <b>Investigator Role</b> and hand it to every active non-admin member, so that Phase D's
/// enforcement flip took nothing from anyone. Ben's decision, now that he is the only person
/// using the site: <i>change the security settings instead of grandfathering anyone</i>.</para>
///
/// <para>That makes roles <b>authoritative</b>. A member holds exactly what somebody gave them,
/// and a read grant can now be restrictive rather than only additive — which was the whole point
/// of IH-03 and is impossible while a seeder is quietly granting case access to everyone. The
/// runtime never had a bypass: <c>HasAccessAsync</c> has always answered from grants alone, with
/// owners and administrators passing above it (decision D2). This seeder was the bridge, and the
/// bridge is what has been removed.</para>
///
/// <para>Existing assignments are left alone — a grandfathered one is indistinguishable from a
/// deliberate one, and revoking both would take roles away that somebody meant to give. What
/// stops here is the automatic granting: new groups and new members start with nothing until a
/// person says otherwise.</para>
/// </remarks>
internal static class OrgRoleSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // ── Backfill the defaults ───────────────────────────────────────
        var orgsWithRoles = await db.OrganizationRoles
            .Select(r => r.OrganizationId).Distinct().ToListAsync();
        var bare = await db.Organizations
            .Where(o => !orgsWithRoles.Contains(o.Id))
            .Select(o => new { o.Id, o.CreatedByAppUserId })
            .ToListAsync();
        foreach (var org in bare)
            OrgRoleDefaults.AddDefaultRoles(db, org.Id, org.CreatedByAppUserId);
        if (bare.Count > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[OrgRoleSeeder] Backfilled the default roles for {bare.Count} organization(s).");
        }
    }
}
