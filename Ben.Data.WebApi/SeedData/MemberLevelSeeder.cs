using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Backfills the default member-title ladder (item 157) for organizations that predate it.
/// </summary>
/// <remarks>
/// Production-safe and idempotent per organization: a group with ANY levels — even one, even
/// renamed — is left entirely alone, so a group that edited its ladder never has rungs pushed
/// back. New organizations get their ladder from the creation doors
/// (<see cref="OrgMemberLevelDefaults"/>); this exists only for groups created before that.
/// </remarks>
internal static class MemberLevelSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var orgsWithLevels = await db.OrganizationMemberLevels
            .Select(l => l.OrganizationId).Distinct().ToListAsync();

        var bare = await db.Organizations
            .Where(o => !orgsWithLevels.Contains(o.Id))
            .Select(o => new { o.Id, o.CreatedByAppUserId })
            .ToListAsync();

        if (bare.Count == 0) return;

        foreach (var org in bare)
            OrgMemberLevelDefaults.AddDefaultLevels(db, org.Id, org.CreatedByAppUserId);

        await db.SaveChangesAsync();
        Console.WriteLine($"[MemberLevelSeeder] Backfilled the default ladder for {bare.Count} organization(s).");
    }
}
