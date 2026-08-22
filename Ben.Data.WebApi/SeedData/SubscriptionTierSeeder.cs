using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds the platform's default price bands so a fresh deployment can price an organization.
/// </summary>
/// <remarks>
/// <para>Without rows here, <c>SubscriptionTierResolver</c> refuses to price anybody and every
/// organization is unbilled — the silent failure the resolver exists to prevent. The same
/// "dead on arrival for every existing deployment" reasoning as the taxonomy seeders.</para>
///
/// <para><b>Seeds once, then leaves the rows alone.</b> These are prices, and prices get edited by
/// a SuperAdmin. A seeder that reasserted them on every startup would silently undo a real price
/// change on the next restart — so it fills an empty table and never touches a populated one.</para>
///
/// <para>The bands are item 85's worked example — 1–3 free, 4–10 at $15 — plus an unbounded top
/// band, which the resolver requires so a group cannot outgrow the list.</para>
/// </remarks>
internal static class SubscriptionTierSeeder
{
    private static readonly (string Name, int Min, int? Max, decimal Price, int Sort)[] Bands =
    [
        ("Free",         1,    3,   0m, 1),
        ("Small group",  4,   10,  15m, 2),
        ("Large group", 11, null,  40m, 3),
    ];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        var owner = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        // Populated already — including deliberately emptied — is left as it is. See remarks.
        if (await db.SubscriptionTiers.AnyAsync()) return;

        var now = DateTime.UtcNow;
        foreach (var (name, min, max, price, sort) in Bands)
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id                 = Guid.NewGuid(),
                Name               = name,
                MinMembers         = min,
                MaxMembers         = max,
                MonthlyPrice       = price,
                SortOrder          = sort,
                IsActive           = true,
                DateCreated        = now,
                CreatedByAppUserId = owner.Id,
            });
        }

        await db.SaveChangesAsync();

        // The resolver's own rules, checked against what was just written rather than assumed —
        // a seeder that plants an unusable price list is worse than one that plants nothing.
        var seeded = await db.SubscriptionTiers.AsNoTracking().ToListAsync();
        if (SubscriptionTierResolver.Validate(seeded) is { } problem)
            throw new InvalidOperationException($"Seeded subscription tiers are not usable: {problem}");
    }
}
