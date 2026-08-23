using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 156 Phase E: the included-areas fan-out nets a removal against a re-add.
/// </summary>
/// <remarks>
/// The checklist saves on every toggle, so this path's whole reason to exist is that an
/// uncheck-then-recheck must reach the groups as SILENCE — a removal is queued behind a grace
/// window, and a re-add cancels the pending sentence instead of sending a cheerful correction
/// to a warning nobody saw. These tests run the real <see cref="PlatformMessageService"/> over
/// an in-memory database and assert on the message rows themselves.
/// </remarks>
public sealed class TierChangeNotifierAreaTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static readonly Guid Editor = Guid.NewGuid();

    /// <summary>One org on one tier, with the org's creator as its only billing recipient.</summary>
    private static async Task<(Guid tierId, Guid orgId, Guid ownerId)> SeedAsync(
        IDbContextFactory<BenDataContext> factory, SubscriptionStatus status,
        DateTime? periodEnd = null)
    {
        var tierId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = ownerId, UserName = ownerId.ToString(), Email = $"{ownerId}@test.com" });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Org", UrlName = $"org-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Band", MinMembers = 1, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, SubscriptionTierId = tierId,
            Status = status, CurrentPeriodEnd = periodEnd,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (tierId, orgId, ownerId);
    }

    private static TierChangeNotifier Notifier(IDbContextFactory<BenDataContext> factory)
        => new(factory, new PlatformMessageService(factory));

    private static HashSet<OrganizationPermissionArea> Areas(params OrganizationPermissionArea[] areas)
        => [.. areas];

    [Fact]
    public async Task A_removal_queues_a_notice_behind_the_grace_window_and_sends_nothing_now()
    {
        var factory = CreateFactory();
        var (tierId, orgId, _) = await SeedAsync(factory, SubscriptionStatus.Free);

        await Notifier(factory).ApplyAreaChangesAsync(
            tierId, "Band",
            Areas(OrganizationPermissionArea.Cases, OrganizationPermissionArea.Calendar),
            Areas(OrganizationPermissionArea.Calendar),
            Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var notice = Assert.Single(await db.TierChangeNotices.ToListAsync());
        Assert.Equal(orgId, notice.OrganizationId);
        Assert.Contains("cases", notice.Sentences);
        Assert.True(notice.DeliverAtUtc > DateTime.UtcNow.AddMinutes(20),
            "the grace window must hold the notice long enough to absorb a mis-click");
        Assert.Empty(await db.UserMessages.ToListAsync());
    }

    [Fact]
    public async Task A_readd_before_delivery_cancels_the_pending_notice_and_sends_nothing()
    {
        var factory = CreateFactory();
        var (tierId, _, _) = await SeedAsync(factory, SubscriptionStatus.Free);
        var notifier = Notifier(factory);

        var with    = Areas(OrganizationPermissionArea.Cases, OrganizationPermissionArea.Calendar);
        var without = Areas(OrganizationPermissionArea.Calendar);

        await notifier.ApplyAreaChangesAsync(tierId, "Band", with, without, Editor, CancellationToken.None);
        await notifier.ApplyAreaChangesAsync(tierId, "Band", without, with, Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.TierChangeNotices.ToListAsync());
        Assert.Empty(await db.UserMessages.ToListAsync());
    }

    [Fact]
    public async Task A_readd_of_one_area_leaves_the_other_areas_notice_intact()
    {
        var factory = CreateFactory();
        var (tierId, _, _) = await SeedAsync(factory, SubscriptionStatus.Free);
        var notifier = Notifier(factory);

        var all  = Areas(OrganizationPermissionArea.Cases, OrganizationPermissionArea.Files,
                         OrganizationPermissionArea.Calendar);
        var none = Areas(OrganizationPermissionArea.Calendar);
        var some = Areas(OrganizationPermissionArea.Files, OrganizationPermissionArea.Calendar);

        await notifier.ApplyAreaChangesAsync(tierId, "Band", all, none, Editor, CancellationToken.None);
        await notifier.ApplyAreaChangesAsync(tierId, "Band", none, some, Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var notice = Assert.Single(await db.TierChangeNotices.ToListAsync());
        Assert.Contains("cases", notice.Sentences);
        Assert.DoesNotContain("files", notice.Sentences);
        Assert.Empty(await db.UserMessages.ToListAsync());
    }

    [Fact]
    public async Task An_addition_with_no_pending_removal_is_announced_immediately()
    {
        var factory = CreateFactory();
        var (tierId, _, ownerId) = await SeedAsync(factory, SubscriptionStatus.Free);

        await Notifier(factory).ApplyAreaChangesAsync(
            tierId, "Band",
            Areas(OrganizationPermissionArea.Calendar),
            Areas(OrganizationPermissionArea.Calendar, OrganizationPermissionArea.PublicPages),
            Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var msg = Assert.Single(await db.UserMessages.ToListAsync());
        Assert.Contains("public pages", msg.MessageBody);
        var to = Assert.Single(await db.UserMessageTos.ToListAsync());
        Assert.Equal(ownerId, to.ToAppUserId);
        Assert.Empty(await db.TierChangeNotices.ToListAsync());
    }

    [Fact]
    public async Task A_paid_groups_removal_is_queued_on_the_renewal_window_not_the_grace()
    {
        var factory = CreateFactory();
        var periodEnd = DateTime.UtcNow.AddDays(60);
        var (tierId, orgId, _) = await SeedAsync(factory, SubscriptionStatus.Active, periodEnd);

        await Notifier(factory).ApplyAreaChangesAsync(
            tierId, "Band",
            Areas(OrganizationPermissionArea.Cases),
            Areas(),
            Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var notice = Assert.Single(await db.TierChangeNotices.ToListAsync());
        Assert.Equal(orgId, notice.OrganizationId);
        Assert.Equal(periodEnd, notice.EffectiveAtUtc);
        Assert.Equal(periodEnd - TierChangeNotifier.NoticeWindow, notice.DeliverAtUtc);
        Assert.Empty(await db.UserMessages.ToListAsync());
    }

    // ── Capabilities ride the same netting (item 167) ─────────────────────────

    [Fact]
    public async Task A_capability_flip_flop_reaches_the_groups_as_silence()
    {
        var factory = CreateFactory();
        var (tierId, _, _) = await SeedAsync(factory, SubscriptionStatus.Free);
        var notifier = Notifier(factory);

        var with    = new HashSet<TierCapability> { TierCapability.CaseTransfers };
        var without = new HashSet<TierCapability>();

        await notifier.ApplyCapabilityChangesAsync(tierId, "Band", with, without, Editor, CancellationToken.None);
        await notifier.ApplyCapabilityChangesAsync(tierId, "Band", without, with, Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.TierChangeNotices.ToListAsync());
        Assert.Empty(await db.UserMessages.ToListAsync());
    }

    [Fact]
    public async Task A_capability_removal_queues_a_notice_naming_the_consequence()
    {
        var factory = CreateFactory();
        var (tierId, orgId, _) = await SeedAsync(factory, SubscriptionStatus.Free);

        await Notifier(factory).ApplyCapabilityChangesAsync(
            tierId, "Band",
            new HashSet<TierCapability> { TierCapability.CaseTransfers },
            new HashSet<TierCapability>(),
            Editor, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var notice = Assert.Single(await db.TierChangeNotices.ToListAsync());
        Assert.Equal(orgId, notice.OrganizationId);
        Assert.Contains("Case transfers", notice.Sentences);
        Assert.Contains("neither be sent", notice.Sentences);
        Assert.Empty(await db.UserMessages.ToListAsync());
    }
}
