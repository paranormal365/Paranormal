using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// One person, several organizations, a different role and a different kind in each — and
/// nothing that belongs to one reaching another (item 199).
/// </summary>
/// <remarks>
/// <para><b>Why this suite exists.</b> Ben, working through who his users actually are:
/// somebody may guide for a ghost walking tour, own a ghost-hunting event provider, and belong
/// to an investigation group, all at once — "please account for any type of these
/// combinations." The schema already supports it, because authority is carried by the
/// membership row and never by the person: <c>OrganizationUserMembership</c> is unique on
/// <c>(OrganizationId, AppUserId)</c> and carries its own <c>Role</c> and title rung, while
/// <c>AppUser</c> carries no role, admin flag, tier or subscription at all.</para>
///
/// <para>"Already supports it" is a claim about behaviour, so it is tested rather than
/// asserted. These run against the real <see cref="OrganizationSecurityService"/> — the thing
/// every controller actually asks — and not against a mock of it, because a mock would agree
/// with whatever the test believed.</para>
///
/// <para><b>The shape of the leak being hunted.</b> Every failure here would look the same
/// from the outside: a person is granted something in the organization they own or work for,
/// and it silently follows them into a different organization where they are an ordinary
/// member. That is the bug that makes a multi-organization life unsafe, and it is worth a
/// standing test even while the code is clean, because the person who later writes a
/// "get the user's organization" helper will be caught by it.</para>
/// </remarks>
public sealed class MultiOrgCombinationTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

    /// <summary>Alex: guide at a tour company, owner of an event provider, member of a group.</summary>
    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid Alex, Guid TourCo, Guid EventCo, Guid HuntGroup);

    private static async Task<World> BuildAsync()
    {
        var factory = CreateFactory();
        var alex = Guid.NewGuid();
        var tourCo = Guid.NewGuid();
        var eventCo = Guid.NewGuid();
        var huntGroup = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        void Org(Guid id, string name, OrganizationKind kind) =>
            db.Organizations.Add(new Organization
            {
                Id = id,
                Name = name,
                UrlName = name.ToLowerInvariant().Replace(' ', '-'),
                Kind = kind,
                RunsPublicTours = OrganizationKindDefaults.RunsPublicTours(kind),
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = alex,
            });

        void Member(Guid orgId, OrganizationMemberRole role) =>
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                AppUserId = alex,
                Role = role,
                IsActive = true,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = alex,
            });

        // A tour company Alex is hired by — an ordinary member who was given the calendar,
        // which is how a guide signs up a walk-up (the late-arrival work).
        Org(tourCo, "Franklin Ghost Walks", OrganizationKind.GhostWalkingTour);
        Member(tourCo, OrganizationMemberRole.Member);

        // An events business Alex owns outright.
        Org(eventCo, "Midnight Prison Events", OrganizationKind.GhostWalkingTour);
        Member(eventCo, OrganizationMemberRole.Owner);

        // And a group Alex simply belongs to, with nothing granted at all.
        Org(huntGroup, "Harpeth Paranormal", OrganizationKind.InvestigationGroup);
        Member(huntGroup, OrganizationMemberRole.Member);

        db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
        {
            Id = Guid.NewGuid(),
            OrganizationId = tourCo,
            AppUserId = alex,
            TableName = OrganizationSecurityTable.OrgCalendar,
            Actions = OrganizationSecurityAction.Read | OrganizationSecurityAction.Update,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = alex,
        });

        await db.SaveChangesAsync();
        return new World(factory, alex, tourCo, eventCo, huntGroup);
    }

    private static OrganizationSecurityService Security(World w) => new(w.Factory);

    /// <summary>
    /// Owning one organization confers nothing in another. The owner bypass in
    /// <c>HasAccessAsync</c> is reached only through the membership row for the organization
    /// being asked about, which is exactly the property that makes it safe.
    /// </summary>
    [Fact]
    public async Task Owning_one_organization_grants_nothing_in_another()
    {
        var w = await BuildAsync();
        var security = Security(w);

        // Owner of the events business: everything, as it should be.
        Assert.True(await security.HasAccessAsync(
            w.Alex, w.EventCo, OrganizationSecurityTable.Case, OrganizationSecurityAction.Delete));

        // The same person, an ordinary member elsewhere with nothing granted: nothing.
        Assert.False(await security.HasAccessAsync(
            w.Alex, w.HuntGroup, OrganizationSecurityTable.Case, OrganizationSecurityAction.Delete));
        Assert.False(await security.HasAccessAsync(
            w.Alex, w.HuntGroup, OrganizationSecurityTable.Case, OrganizationSecurityAction.Read));
    }

    /// <summary>
    /// A grant is scoped to the organization that made it. The tour company gave Alex its
    /// calendar; the investigation group did not, and the tour company cannot give it away on
    /// the group's behalf.
    /// </summary>
    [Fact]
    public async Task A_grant_is_scoped_to_the_organization_that_made_it()
    {
        var w = await BuildAsync();
        var security = Security(w);

        Assert.True(await security.HasAccessAsync(
            w.Alex, w.TourCo, OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update));

        Assert.False(await security.HasAccessAsync(
            w.Alex, w.HuntGroup, OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update));
    }

    /// <summary>
    /// A grant does not widen within its own organization either: the tour gave Alex the
    /// calendar, not the case file.
    /// </summary>
    [Fact]
    public async Task A_grant_does_not_widen_beyond_its_own_table()
    {
        var w = await BuildAsync();

        Assert.False(await Security(w).HasAccessAsync(
            w.Alex, w.TourCo, OrganizationSecurityTable.Case, OrganizationSecurityAction.Update));
    }

    /// <summary>
    /// Every membership is listed, whatever the role or the kind — the switcher must show all
    /// three or a person cannot reach the work they were hired to do.
    /// </summary>
    [Fact]
    public async Task Every_membership_is_listed_regardless_of_role_or_kind()
    {
        var w = await BuildAsync();

        var orgs = await Security(w).GetOrganizationsForUserAsync(w.Alex);

        Assert.Equal(3, orgs.Count);
        Assert.Contains(orgs, o => o.Id == w.TourCo && o.Kind == OrganizationKind.GhostWalkingTour);
        Assert.Contains(orgs, o => o.Id == w.EventCo && o.Kind == OrganizationKind.GhostWalkingTour);
        Assert.Contains(orgs, o => o.Id == w.HuntGroup && o.Kind == OrganizationKind.InvestigationGroup);
    }

    /// <summary>
    /// The kind is a property of the organization, so a person in two kinds at once leaves both
    /// intact — including the tour defaults, which must not be applied to the group.
    /// </summary>
    [Fact]
    public async Task The_kind_of_one_organization_does_not_change_another()
    {
        var w = await BuildAsync();
        await using var db = await w.Factory.CreateDbContextAsync();

        var tour = await db.Organizations.FindAsync(w.TourCo);
        var group = await db.Organizations.FindAsync(w.HuntGroup);

        Assert.True(tour!.RunsPublicTours);
        Assert.False(group!.RunsPublicTours);
    }

    /// <summary>
    /// A plan limit belongs to an organization, not to the people in it. The tour company on a
    /// restricted plan must not restrict the group Alex also belongs to.
    /// </summary>
    /// <remarks>
    /// Asked through the member-with-a-grant path deliberately: owners and administrators
    /// bypass the tier gate by design ("a plan can narrow what ROLES may do, never what the
    /// owner may do"), so asking as an owner would pass whether the scoping worked or not.
    /// </remarks>
    [Fact]
    public async Task A_plan_limit_in_one_organization_does_not_reach_another()
    {
        var w = await BuildAsync();
        var restricted = Guid.NewGuid();

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            // A plan that includes Cases and nothing else — so the calendar is excluded.
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = restricted, Name = "Restricted", MinMembers = 1, SortOrder = 1,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.Alex,
            });
            db.SubscriptionTierPermissionAreas.Add(new SubscriptionTierPermissionArea
            {
                SubscriptionTierId = restricted, Area = OrganizationPermissionArea.Cases,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.Alex,
            });
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = w.TourCo, SubscriptionTierId = restricted,
                Status = SubscriptionStatus.Active,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.Alex,
            });
            await db.SaveChangesAsync();
        }

        var security = Security(w);

        // The tour company's own plan takes its calendar away, grant and all.
        Assert.False(await security.HasAccessAsync(
            w.Alex, w.TourCo, OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update));

        // The group Alex also belongs to never subscribed to it and is untouched: with no
        // resolvable tier the area gate fails open, so a grant there would still work.
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = w.HuntGroup, AppUserId = w.Alex,
                TableName = OrganizationSecurityTable.OrgCalendar,
                Actions = OrganizationSecurityAction.Update,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.Alex,
            });
            await db.SaveChangesAsync();
        }

        Assert.True(await security.HasAccessAsync(
            w.Alex, w.HuntGroup, OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update));
    }

    /// <summary>
    /// A seat charge belongs to one organization. Two organizations may each bill the same
    /// person for a seat, and neither row is the other's.
    /// </summary>
    /// <remarks>
    /// This is the money half of the same property, and the one with a decision still in it:
    /// consistent that every group pays for its own headcount, potentially surprising to a
    /// person who guides for one business and belongs to another and sees two charges. Recorded
    /// as behaviour so that if Ben decides it should feel like one relationship, the change is
    /// a deliberate one and this test is what has to be rewritten.
    /// </remarks>
    [Fact]
    public async Task A_seat_charge_belongs_to_one_organization()
    {
        var w = await BuildAsync();

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            foreach (var (orgId, price) in new[] { (w.TourCo, 3m), (w.HuntGroup, 5m) })
                db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = w.Alex,
                    Status = SubscriptionStatus.Active, PriceAtStart = price,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.Alex,
                });
            await db.SaveChangesAsync();
        }

        await using var read = await w.Factory.CreateDbContextAsync();

        var seats = await read.MemberSeatSubscriptions
            .Where(s => s.AppUserId == w.Alex).ToListAsync();
        Assert.Equal(2, seats.Count);

        // Each organization's charge is its own, and cancelling one leaves the other standing.
        Assert.Equal(3m, seats.Single(s => s.OrganizationId == w.TourCo).PriceAtStart);
        Assert.Equal(5m, seats.Single(s => s.OrganizationId == w.HuntGroup).PriceAtStart);
        Assert.DoesNotContain(seats, s => s.OrganizationId == w.EventCo);
    }

    /// <summary>
    /// Leaving one organization does not disturb the others — the deactivated membership stops
    /// answering, and both remaining ones carry on with the roles they had.
    /// </summary>
    [Fact]
    public async Task Leaving_one_organization_leaves_the_others_untouched()
    {
        var w = await BuildAsync();

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var membership = await db.OrganizationUserMemberships
                .FirstAsync(m => m.OrganizationId == w.TourCo && m.AppUserId == w.Alex);
            membership.IsActive = false;
            await db.SaveChangesAsync();
        }

        var security = Security(w);

        Assert.False(await security.HasAccessAsync(
            w.Alex, w.TourCo, OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update));
        Assert.True(await security.HasAccessAsync(
            w.Alex, w.EventCo, OrganizationSecurityTable.Case, OrganizationSecurityAction.Delete));

        var orgs = await security.GetOrganizationsForUserAsync(w.Alex);
        Assert.DoesNotContain(orgs, o => o.Id == w.TourCo);
        Assert.Equal(2, orgs.Count);
    }
}
