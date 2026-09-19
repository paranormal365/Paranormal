using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 144, Ben's overflow-seat model: a group's band covers its member count, and people who
/// join PAST it are billed individually at the tier's per-extra-member price. The rules worth
/// pinning are the ones that cost money when wrong — who gets a seat, at what price, and that
/// joining is never blocked by one.
/// </summary>
public sealed class OverflowSeatTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static BenDataContext NewDb() =>
        new(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>A group on a 1–3 band that sells extra seats at $4/month, with `members` members.</summary>
    private static async Task<(BenDataContext Db, Guid OrgId, Guid AdminId)> SeedAsync(
        int members, decimal? pricePerExtra = 4m, int? bandMax = 3,
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var db = NewDb();
        Guid orgId = Guid.NewGuid(), adminId = Guid.NewGuid(), tierId = Guid.NewGuid();

        db.Users.Add(new AppUser { Id = adminId, UserName = "a@t", Email = "a@t", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Night Watch", UrlName = "nw", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Small group", MinMembers = 1, MaxMembers = bandMax, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId, Interval = BillingInterval.Monthly,
            Price = 15m, PricePerExtraMember = pricePerExtra, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Status = status, SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, PriceAtPeriodStart = 15m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.NewGuid();
            db.Users.Add(new AppUser { Id = userId, UserName = $"m{i}@t", Email = $"m{i}@t", DateCreated = DateTime.UtcNow });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
            });
        }
        await db.SaveChangesAsync();
        return (db, orgId, adminId);
    }

    /// <summary>Adds the joining member the way the accept endpoint does — tracked, unsaved.</summary>
    private static Guid AddJoiner(BenDataContext db, Guid orgId, Guid adminId)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = userId, UserName = "new@t", Email = "new@t", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        return userId;
    }

    [Fact]
    public async Task The_member_who_takes_the_group_past_its_band_gets_a_seat_at_the_frozen_price()
    {
        // Band is 1–3 and three people are in; the fourth is the overflow.
        var (db, orgId, adminId) = await SeedAsync(members: 3);
        var joinerId = AddJoiner(db, orgId, adminId);

        var seat = await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default);

        Assert.NotNull(seat);
        Assert.Equal(SubscriptionStatus.PendingPayment, seat!.Status);
        Assert.Equal(4m, seat.PriceAtStart);
        Assert.Equal(BillingInterval.Monthly, seat.Interval);
    }

    [Fact]
    public async Task A_join_that_stays_inside_the_band_creates_no_seat()
    {
        var (db, orgId, adminId) = await SeedAsync(members: 2);   // 2 + joiner = 3, the band's cap
        var joinerId = AddJoiner(db, orgId, adminId);
        Assert.Null(await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default));
    }

    [Fact]
    public async Task A_band_that_does_not_sell_extra_seats_never_charges_anybody()
    {
        var (db, orgId, adminId) = await SeedAsync(members: 5, pricePerExtra: null);
        var joinerId = AddJoiner(db, orgId, adminId);
        Assert.Null(await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default));
    }

    [Fact]
    public async Task An_unbounded_band_cannot_be_outgrown()
    {
        var (db, orgId, adminId) = await SeedAsync(members: 50, bandMax: null);
        var joinerId = AddJoiner(db, orgId, adminId);
        Assert.Null(await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default));
    }

    [Fact]
    public async Task Free_and_lapsed_groups_have_no_band_to_outgrow()
    {
        foreach (var status in new[] { SubscriptionStatus.Free, SubscriptionStatus.Lapsed })
        {
            var (db, orgId, adminId) = await SeedAsync(members: 9, status: status);
            var joinerId = AddJoiner(db, orgId, adminId);
            Assert.Null(await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default));
        }
    }

    [Fact]
    public async Task A_rejoining_member_who_already_holds_a_seat_does_not_get_a_second_one()
    {
        var (db, orgId, adminId) = await SeedAsync(members: 3);
        var joinerId = AddJoiner(db, orgId, adminId);
        db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = joinerId,
            Status = SubscriptionStatus.Active, Interval = BillingInterval.Monthly, PriceAtStart = 4m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });
        await db.SaveChangesAsync();

        Assert.Null(await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default));
    }

    [Fact]
    public async Task The_price_is_frozen_a_later_tier_edit_does_not_reprice_the_seat()
    {
        var (db, orgId, adminId) = await SeedAsync(members: 3);
        var joinerId = AddJoiner(db, orgId, adminId);
        var seat = await OverflowSeats.MaybeOfferSeatAsync(db, orgId, joinerId, adminId, default);
        await db.SaveChangesAsync();

        (await db.SubscriptionTierPrices.SingleAsync()).PricePerExtraMember = 99m;
        await db.SaveChangesAsync();

        Assert.Equal(4m, (await db.MemberSeatSubscriptions.SingleAsync(s => s.Id == seat!.Id)).PriceAtStart);
    }

    // ── The resolver amendment ───────────────────────────────────────────────

    private static SubscriptionTier Band(string name, int min, int? max, decimal? perExtra = null)
    {
        var tier = new SubscriptionTier { Id = Guid.NewGuid(), Name = name, MinMembers = min, MaxMembers = max, IsActive = true };
        tier.Prices.Add(new SubscriptionTierPrice
        {
            Interval = BillingInterval.Monthly, Price = 10m, PricePerExtraMember = perExtra, IsActive = true,
        });
        return tier;
    }

    [Fact]
    public void A_bounded_top_band_is_legal_exactly_when_it_prices_extra_members()
    {
        // Without a per-extra price the old rule stands: the list is unusable.
        Assert.NotNull(SubscriptionTierResolver.Validate([Band("Small", 1, 3)]));
        // With one, growth past the band is priced per seat, so bounding it is sound.
        Assert.Null(SubscriptionTierResolver.Validate([Band("Small", 1, 3, perExtra: 4m)]));
    }

    [Fact]
    public void A_group_past_an_overflowing_top_band_still_resolves_to_that_band()
    {
        var bands = new[] { Band("Small", 1, 3, perExtra: 4m) };
        // Not an exception, and not a bigger band that does not exist: the group stays on its
        // band and the extra people hold seats.
        Assert.Equal("Small", SubscriptionTierResolver.Resolve(bands, memberCount: 9).Name);
    }
}
