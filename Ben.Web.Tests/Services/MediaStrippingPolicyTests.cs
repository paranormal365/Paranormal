using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 181: audio/video stripping is a per-organization setting whose availability a plan can
/// withhold. Three things must agree before it happens — the host has a tool, the plan includes
/// it, the group has left it on — and each refusal has to say which, because a toggle that
/// silently does nothing is the write-only-feature shape this codebase keeps re-learning.
/// </summary>
public sealed class MediaStrippingPolicyTests
{
    private static BenDataContext NewDb() =>
        new(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IAvMetadataStripper Stripper(bool available)
    {
        var mock = new Mock<IAvMetadataStripper>();
        mock.Setup(s => s.IsAvailable).Returns(available);
        return mock.Object;
    }

    /// <summary>A group on a tier, with the setting as given and the capability included or not.</summary>
    private static async Task<(BenDataContext Db, Guid OrgId)> SeedAsync(
        bool settingOn = true, bool capabilityIncluded = true)
    {
        var db = NewDb();
        Guid orgId = Guid.NewGuid(), tierId = Guid.NewGuid(), userId = Guid.NewGuid();

        db.Users.Add(new AppUser { Id = userId, UserName = "u@t", Email = "u@t", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Night Watch", UrlName = "nw", StripMediaMetadata = settingOn,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Free", MinMembers = 1, MaxMembers = null, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Status = SubscriptionStatus.Active,
            SubscriptionTierId = tierId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        // Capabilities are stored as EXCLUSIONS, so withholding one means adding a row.
        if (!capabilityIncluded)
        {
            db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
            {
                Id = Guid.NewGuid(), SubscriptionTierId = tierId,
                Capability = TierCapability.MediaMetadataStripping,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }

        await db.SaveChangesAsync();
        return (db, orgId);
    }

    [Fact]
    public async Task All_three_agreeing_is_the_only_way_it_strips()
    {
        var (db, orgId) = await SeedAsync();
        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, Stripper(available: true), orgId, default);

        Assert.True(decision.Strips);
        Assert.Null(decision.Reason);
        Assert.False(decision.NeedsUpgrade);
    }

    [Fact]
    public async Task A_host_with_no_media_tool_says_so_and_does_not_blame_the_plan()
    {
        var (db, orgId) = await SeedAsync();
        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, Stripper(available: false), orgId, default);

        Assert.False(decision.Strips);
        Assert.Contains("no media tool", decision.Reason!, StringComparison.OrdinalIgnoreCase);
        // The group cannot fix this by paying, so the screen must not offer an upgrade.
        Assert.False(decision.NeedsUpgrade);
        // Nothing to choose when nothing can honour the choice.
        Assert.False(decision.CanChoose);
    }

    [Fact]
    public async Task A_plan_that_withholds_it_names_the_plan_and_offers_the_upgrade()
    {
        var (db, orgId) = await SeedAsync(capabilityIncluded: false);
        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, Stripper(available: true), orgId, default);

        Assert.False(decision.Strips);
        Assert.Contains("Free", decision.Reason!);
        Assert.True(decision.NeedsUpgrade);
        Assert.False(decision.CanChoose);
    }

    [Fact]
    public async Task A_group_that_turned_it_off_is_told_it_was_their_own_choice()
    {
        var (db, orgId) = await SeedAsync(settingOn: false);
        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, Stripper(available: true), orgId, default);

        Assert.False(decision.Strips);
        Assert.Contains("your group", decision.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.False(decision.NeedsUpgrade);
        // They turned it off themselves, so the switch stays theirs to turn back on.
        Assert.True(decision.CanChoose);
    }

    [Fact]
    public async Task The_plan_is_checked_before_the_setting_so_the_actionable_reason_wins()
    {
        // Both are in the way. The one worth saying is the one the group can do something about.
        var (db, orgId) = await SeedAsync(settingOn: false, capabilityIncluded: false);
        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, Stripper(available: true), orgId, default);

        Assert.True(decision.NeedsUpgrade);
    }

    /// <summary>
    /// Ben's line (2026-08-24): "every file reads and has a row entered into the table containing
    /// EXIF-like data, but removal is what you are working on". Reading is free and universal;
    /// only removal is gated. A future change that made extraction conditional on the plan would
    /// sell groups their own facts back, and this fails if that happens.
    /// </summary>
    [Fact]
    public void Extraction_is_never_gated_only_removal_is()
    {
        var ingestSource = File.ReadAllText(Path.Combine(
            RepoRoot(), "Ben.Data.WebApi", "Services", "MediaIngestService.cs"));

        var extractAt = ingestSource.IndexOf("metadataExtractor.Extract(", StringComparison.Ordinal);
        // The GATE, not the parameter's declaration — the interface names it far earlier in the
        // file, which is what the first version of this check tripped over.
        var gateAt = ingestSource.IndexOf("if (stripAudioVideo &&", StringComparison.Ordinal);

        Assert.True(extractAt > 0, "the ingest path no longer extracts metadata at all");
        Assert.True(gateAt > 0, "the stripping gate is no longer where this test can find it");
        Assert.True(gateAt > extractAt,
            "the stripping gate now sits BEFORE extraction — every file must get its metadata row "
          + "regardless of plan; only removal is a paid capability.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public async Task A_new_group_strips_without_anyone_switching_it_on()
    {
        // The default that matters: privacy nobody has to discover. A group created with no
        // explicit choice gets the protection.
        var db = NewDb();
        Guid orgId = Guid.NewGuid(), userId = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t", Email = "u@t", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Fresh", UrlName = "fresh",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, Stripper(available: true), orgId, default);
        Assert.True(decision.Strips);
    }
}
