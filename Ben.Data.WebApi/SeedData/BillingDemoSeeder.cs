using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Development-only data that makes items 84, 85 and 111 visible on a fresh database.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The billing tables shipped with a tier seeder and nothing else,
/// so on a fresh dev database the Coupons screen was empty, no band had a single cap, every group
/// read "Free", and the evidence review queue never appeared. Every one of those surfaces would
/// have looked broken-or-unbuilt to anybody opening it — the same "dead on arrival for every
/// existing deployment" reasoning the contact-type seeder was written for.</para>
///
/// <para><b>Development only</b>, unlike <see cref="SubscriptionTierSeeder"/>: the bands are real
/// product configuration and belong on any deployment, while a coupon called LAUNCH25 and a group
/// three weeks from renewal are demo furniture. Guarded by the same flag as the other dev seeders
/// and never run in production.</para>
///
/// <para><b>Each block is independently idempotent.</b> They are gated on their own marker rather
/// than on one another, so a database that already has some of this gains only what it is
/// missing — the trap that hid the past-event seed behind an unrelated early return.</para>
/// </remarks>
internal static class BillingDemoSeeder
{
    /// <summary>The caps Ben named as the launch shape: open cases, scaling with the band.</summary>
    private static readonly (string Tier, int? OpenCases, int? Equipment)[] Caps =
    [
        ("Free",        2,    5),
        ("Small group", 10,   40),
        ("Large group", null, null),   // written-down unlimited, which is a decision worth showing
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
        var now = DateTime.UtcNow;

        await SeedCapsAsync(db, owner.Id, now);
        await SeedCouponsAsync(db, owner.Id, now);
        await SeedSubscriptionsAsync(db, owner.Id, now);
        await SeedPendingEvidenceAsync(db, owner.Id, now);
    }

    // ── caps on the bands ─────────────────────────────────────────────────────

    private static async Task SeedCapsAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        if (await db.SubscriptionTierLimits.AnyAsync()) return;

        var tiers = await db.SubscriptionTiers.ToListAsync();
        if (tiers.Count == 0) return;

        foreach (var (tierName, openCases, equipment) in Caps)
        {
            var tier = tiers.FirstOrDefault(t => t.Name == tierName);
            if (tier is null) continue;

            foreach (var (limit, max) in new[]
                     { (SubscriptionLimit.OpenCases, openCases), (SubscriptionLimit.EquipmentItems, equipment) })
            {
                db.SubscriptionTierLimits.Add(new SubscriptionTierLimit
                {
                    Id = Guid.NewGuid(), SubscriptionTierId = tier.Id,
                    Limit = limit, MaxValue = max,
                    DateCreated = now, CreatedByAppUserId = ownerId,
                });
            }
        }

        await db.SaveChangesAsync();
        Console.WriteLine("[BillingDemoSeeder] Added open-case and equipment caps to the price bands.");
    }

    // ── coupons: one of each kind, so both shapes render ──────────────────────

    private static async Task SeedCouponsAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        if (await db.Coupons.AnyAsync(c => c.Name == "Launch offer")) return;

        var shared = new Coupon
        {
            Id = Guid.NewGuid(), Name = "Launch offer",
            Description = "25% off the first three periods, any cadence.",
            Kind = CouponKind.Shared, PercentOff = 25,
            Duration = CouponDuration.Repeating, DurationPeriods = 3,
            MaxRedemptions = 100, RedeemByUtc = now.AddMonths(6),
            AppliesTo = CouponApplicability.NewSubscriptionsOnly,
            IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
        };
        shared.Codes.Add(new CouponCode
        {
            Id = Guid.NewGuid(), CouponId = shared.Id, Code = "LAUNCH25",
            IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
        });

        // A generated batch, so the codes panel has something with more than one row in it and
        // the single-use-per-code shape is visible rather than only described.
        var batch = new Coupon
        {
            Id = Guid.NewGuid(), Name = "ParaCon 2026 handout",
            Description = "Single-use cards handed out at the conference stand.",
            Kind = CouponKind.Generated, AmountOff = 10m,
            Duration = CouponDuration.Once,
            RedeemByUtc = now.AddMonths(3),
            IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
        };
        foreach (var code in CouponCodeGenerator.Batch(12, "PARACON"))
        {
            batch.Codes.Add(new CouponCode
            {
                Id = Guid.NewGuid(), CouponId = batch.Id, Code = code,
                MaxRedemptions = 1, IsActive = true,
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
        }

        db.Coupons.AddRange(shared, batch);
        await db.SaveChangesAsync();
        Console.WriteLine("[BillingDemoSeeder] Added a shared coupon and a 12-code generated batch.");
    }

    // ── subscriptions: two paid groups, one of them near renewal ──────────────

    private static async Task SeedSubscriptionsAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        var tiers = await db.SubscriptionTiers.Include(t => t.Prices).Include(t => t.Limits).ToListAsync();
        if (tiers.Count == 0) return;

        // "paranormal365" is 10 days from renewal ON PURPOSE: inside the two-week window, so the lapse job's
        // first notice fires on a dev database and the feature can be seen rather than trusted.
        var plans = new (string UrlName, string TierName, BillingInterval Interval, int DaysLeft)[]
        {
            ("paranormal365", "Small group", BillingInterval.Monthly, 10),
            ("nps", "Large group", BillingInterval.Yearly,  200),
        };

        var added = 0;
        foreach (var (urlName, tierName, interval, daysLeft) in plans)
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == urlName);
            if (org is null) continue;

            // Only groups with no row at all — never overwriting a subscription somebody set by
            // hand while testing.
            if (await db.OrganizationSubscriptions.AnyAsync(s => s.OrganizationId == org.Id)) continue;

            var tier = tiers.FirstOrDefault(t => t.Name == tierName);
            if (tier is null) continue;

            var members = await db.OrganizationUserMemberships
                .CountAsync(m => m.OrganizationId == org.Id && m.IsActive);

            var start = now.AddDays(daysLeft).AddMonths(-SubscriptionPricing.MonthsIn(interval));
            var sub = new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id,
                DateCreated = now, CreatedByAppUserId = ownerId,
            };

            // Through PeriodOpener, so the seeded state is the same shape a real payment produces
            // — frozen count and price, a contract snapshot, first-paid set once.
            var snapshot = PeriodOpener.Open(
                sub, tier, SubscriptionStatus.Active, interval,
                start, now.AddDays(daysLeft), members, ownerId);

            sub.ProviderName = "Manual";
            db.OrganizationSubscriptions.Add(sub);
            if (snapshot is not null) db.SubscriptionContractTerms.Add(snapshot);
            added++;
        }

        if (added == 0) return;

        await db.SaveChangesAsync();
        Console.WriteLine($"[BillingDemoSeeder] Put {added} group(s) on paid plans, one inside the renewal-notice window.");
    }

    // ── one submission waiting on review (item 111) ───────────────────────────

    private static async Task SeedPendingEvidenceAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        const string marker = "Seeded — knocking near the cave mouth";
        if (await db.EventEvidenceSubmissions.AnyAsync(s => s.Note == marker)) return;

        var pastEvent = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.Title == "Bell Witch Cave — Last Month's Open Night");
        if (pastEvent is null) return;

        var daniel = await db.Users.FirstOrDefaultAsync(u => u.Email == "daniel.park@benco.dev");
        if (daniel is null) return;

        // Bytes in the column rather than on disk: a seeder that writes files leaves litter a
        // database reset cannot clear, and the byte path already falls back to the blob.
        var file = new UploadFile
        {
            Id = Guid.NewGuid(),
            UploadFileTypeId = await db.UploadFileTypes.Select(t => t.Id).FirstAsync(),
            AppUserId = daniel.Id,
            FileName = "cave-mouth-knocks.wav", StoredFileName = "cave-mouth-knocks.wav",
            ContentType = "audio/wav", FileSize = 8,
            FileData = [0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00],
            IsPublic = false,
            DateCreated = now, CreatedByAppUserId = daniel.Id,
        };
        db.UploadFiles.Add(file);

        db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = pastEvent.Id,
            SubmittedByAppUserId = daniel.Id, UploadFileId = file.Id,
            Note = marker, Status = EvidenceSubmissionStatus.Pending,
            DateCreated = now, CreatedByAppUserId = daniel.Id,
        });

        await db.SaveChangesAsync();
        Console.WriteLine("[BillingDemoSeeder] Added a pending evidence submission for the review queue.");
    }
}
