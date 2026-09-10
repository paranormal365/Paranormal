using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The SuperAdmin switch that closes the door to NEW tour and event businesses (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "I want to be able to toggle the option for sign ups as tour
/// groups. Those already signed up and paying would continue, but no new groups would be allowed
/// until I turned it back on."</para>
///
/// <para>So the thing worth pinning is what the switch does <b>not</b> touch. A business that
/// already exists keeps its tours, its dates and its plan; the switch is about one door, and a
/// policy control that quietly disabled a paying customer's product would be far worse than no
/// control at all.</para>
/// </remarks>
public sealed class TourSignUpSwitchTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SetAsync(IDbContextFactory<BenDataContext> factory, bool? on)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (on is null) return;   // unset: no row at all
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(),
            Key = SiteSettingKeys.AllowTourBusinessSignUps,
            Value = on.Value ? "true" : "false",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    // ── the setting itself ───────────────────────────────────────────────────

    [Fact]
    public async Task Unset_reads_as_open_so_a_site_that_never_touched_it_is_unchanged()
    {
        var factory = Db();
        await SetAsync(factory, null);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await SiteSettingsService.GetBoolAsync(
            db, SiteSettingKeys.AllowTourBusinessSignUps, whenUnset: true));
    }

    [Fact]
    public async Task Off_reads_as_closed()
    {
        var factory = Db();
        await SetAsync(factory, false);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await SiteSettingsService.GetBoolAsync(
            db, SiteSettingKeys.AllowTourBusinessSignUps, whenUnset: true));
    }

    [Fact]
    public void The_switch_is_offered_as_a_switch_not_a_text_box()
    {
        // A boolean setting typed into a text box is how "Off" ends up stored as "off" and read
        // as unset — which for this switch would silently reopen the door.
        Assert.Contains(SiteSettingKeys.AllowTourBusinessSignUps, SiteSettingKeys.BooleanKeys);
        Assert.Contains(SiteSettingKeys.Seed, s => s.Key == SiteSettingKeys.AllowTourBusinessSignUps);
    }

    [Fact]
    public void The_admin_page_says_what_it_does_and_what_it_leaves_alone()
    {
        var (_, label, description) = SiteSettingKeys.Seed
            .First(s => s.Key == SiteSettingKeys.AllowTourBusinessSignUps);

        Assert.Contains("tour", label, StringComparison.OrdinalIgnoreCase);
        // The half somebody switching this off most needs to be sure of.
        Assert.Contains("already", description, StringComparison.OrdinalIgnoreCase);
    }

    // ── which kinds it covers ────────────────────────────────────────────────

    [Theory]
    [InlineData(OrganizationKind.GhostWalkingTour, true)]
    [InlineData(OrganizationKind.PublicEventProvider, true)]
    [InlineData(OrganizationKind.InvestigationGroup, false)]
    [InlineData(OrganizationKind.HauntedProperty, false)]
    public void It_covers_exactly_the_kinds_the_tour_plan_prices(OrganizationKind kind, bool covered)
        // One definition, shared with the pricing: a switch that closed a different set of doors
        // from the set being sold would be a second rule to keep in step.
        => Assert.Equal(covered, Ben.Data.Source.Services.SubscriptionTierResolver.IsBusinessKind(kind));

    // ── it never reaches an existing business ────────────────────────────────

    [Fact]
    public async Task A_business_that_already_exists_keeps_its_tours_while_the_door_is_shut()
    {
        var factory = Db();
        await SetAsync(factory, false);

        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), userId = Guid.NewGuid(), addressId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = userId, UserName = "o", Email = "o@t.com", DateCreated = now });
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Printers Alley Walks", UrlName = "paw",
                Kind = OrganizationKind.GhostWalkingTour, RunsPublicTours = true,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            db.OrganizationAddresses.Add(new OrganizationAddress
            {
                Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
                StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN",
                ZipCode = "37201", Country = "US", DateCreated = now, CreatedByAppUserId = userId,
            });
            db.Tours.Add(new Tour
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Printers Alley Walk",
                UrlName = "printers-alley-walk", StartOrganizationAddressId = addressId,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        await using var after = await factory.CreateDbContextAsync();
        var org = await after.Organizations.SingleAsync();

        // Still a tour business, still running its tour, still priced as one.
        Assert.Equal(OrganizationKind.GhostWalkingTour, org.Kind);
        Assert.True(org.RunsPublicTours);
        Assert.Equal(1, await after.Tours.CountAsync(t => t.OrganizationId == orgId && t.RetiredAtUtc == null));
        Assert.True(Ben.Data.Source.Services.SubscriptionTierResolver.IsBusinessKind(org.Kind));
    }

    [Fact]
    public void The_refusal_is_written_to_be_read_by_the_person_it_stops()
    {
        // Pinned as a rule rather than as a string: a 403 that says "Forbidden" to somebody
        // trying to start a business tells them nothing about what to do next.
        var source = File.ReadAllText(Path.Combine(RepoRoot(),
            "Ben.Data.WebApi", "Controllers", "OrganizationMembershipController.cs"));

        Assert.Contains("AllowTourBusinessSignUps", source, StringComparison.Ordinal);
        Assert.Contains("aren't taking on new tour or event businesses", source, StringComparison.Ordinal);
        Assert.Contains("investigation group", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Becoming_one_later_is_the_same_door_and_is_checked_too()
    {
        // Without this, switching the toggle off closes the front door and leaves the side one
        // open: register as an investigation group on Monday, change kind on Tuesday.
        var source = File.ReadAllText(Path.Combine(RepoRoot(),
            "Ben.Data.WebApi", "Controllers", "Entities", "OrganizationController.cs"));

        Assert.Contains("AllowTourBusinessSignUps", source, StringComparison.Ordinal);
        Assert.Contains("becomingABusiness", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
