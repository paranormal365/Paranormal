using Ben.Data.Source.Services;
using Ben.Data.Common.Enums;
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
///
/// <para>Each paid band gets a monthly and a yearly price, the yearly one set to ten months —
/// "two months free", which is the discount people recognise. The free band gets a monthly row
/// only: a yearly price of zero is a cadence choice with no consequence, and offering it is a
/// question asked for no reason.</para>
/// </remarks>
internal static class SubscriptionTierSeeder
{
    /// <summary>Yearly costs this many months. Ten is "two months free".</summary>
    private const int YearlyMonthsCharged = 10;

    private static readonly (string Name, int Min, int? Max, decimal Monthly, int Sort)[] Bands =
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

        // ── Permission areas (item 156 Phase A): every tier starts ALL-INCLUSIVE ──
        // BEFORE the tiers-exist early return, because it backfills databases whose tiers
        // predate the areas table. Zero behavior change is the phase's contract: all-checked
        // gates nothing, and differentiation is a choice Ben makes by UNchecking. Per-tier
        // gate: a tier with ANY area rows has been edited (or seeded) and is left entirely
        // alone, so an unchecked box never grows back.
        {
            var existingTiers = await db.SubscriptionTiers.AsNoTracking().ToListAsync();
            var tiersWithAreas = await db.SubscriptionTierPermissionAreas.AsNoTracking()
                .Select(a => a.SubscriptionTierId).Distinct().ToListAsync();
            var bareTiers = existingTiers.Where(t => !tiersWithAreas.Contains(t.Id)).ToList();
            if (bareTiers.Count > 0)
            {
                var seedNow = DateTime.UtcNow;
                foreach (var tier in bareTiers)
                foreach (var area in Enum.GetValues<Ben.Data.Common.Enums.OrganizationPermissionArea>())
                {
                    db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
                    {
                        SubscriptionTierId = tier.Id,
                        Area = area,
                        DateCreated = seedNow,
                        CreatedByAppUserId = tier.CreatedByAppUserId,
                    });
                }
                await db.SaveChangesAsync();
                Console.WriteLine($"[SubscriptionTierSeeder] Seeded all permission areas for {bareTiers.Count} tier(s).");
            }
        }


        // Populated already — including deliberately emptied — is left as it is. See remarks.
        if (await db.SubscriptionTiers.AnyAsync()) return;

        var now = DateTime.UtcNow;
        foreach (var (name, min, max, monthly, sort) in Bands)
        {
            var tier = new SubscriptionTier
            {
                Id                 = Guid.NewGuid(),
                Name               = name,
                MinMembers         = min,
                MaxMembers         = max,
                SortOrder          = sort,
                IsActive           = true,
                DateCreated        = now,
                CreatedByAppUserId = owner.Id,
            };

            tier.Prices.Add(NewPrice(tier, BillingInterval.Monthly, monthly, now, owner.Id));

            if (monthly > 0)
                tier.Prices.Add(NewPrice(
                    tier, BillingInterval.Yearly, monthly * YearlyMonthsCharged, now, owner.Id));

            db.SubscriptionTiers.Add(tier);
        }

        await db.SaveChangesAsync();

        // The resolver's own rules, checked against what was just written rather than assumed —
        // a seeder that plants an unusable price list is worse than one that plants nothing.
        // The tiers this run just created get their all-inclusive checklist too.
        {
            var justCreated = await db.SubscriptionTiers.AsNoTracking().ToListAsync();
            var seedNow2 = DateTime.UtcNow;
            foreach (var tier in justCreated)
            foreach (var area in Enum.GetValues<Ben.Data.Common.Enums.OrganizationPermissionArea>())
            {
                db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
                {
                    SubscriptionTierId = tier.Id, Area = area,
                    DateCreated = seedNow2, CreatedByAppUserId = tier.CreatedByAppUserId,
                });
            }
            await db.SaveChangesAsync();
        }

        var seeded = await db.SubscriptionTiers.AsNoTracking().ToListAsync();
        if (SubscriptionTierResolver.Validate(seeded) is { } problem)
            throw new InvalidOperationException($"Seeded subscription tiers are not usable: {problem}");

    }

    private static SubscriptionTierPrice NewPrice(
        SubscriptionTier tier, BillingInterval interval, decimal price, DateTime now, Guid ownerId) =>
        new()
        {
            Id                 = Guid.NewGuid(),
            SubscriptionTierId = tier.Id,
            Interval           = interval,
            Price              = price,
            IsActive           = true,
            DateCreated        = now,
            CreatedByAppUserId = ownerId,
        };
}
