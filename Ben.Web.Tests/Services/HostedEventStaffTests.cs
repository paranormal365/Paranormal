using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A helper at one event, and what that lets them do (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>The claim under test is that a staff row adds and never subtracts, and that it never
/// reaches past its own event.</b> A hotel's weekend steward is nobody in the group and somebody
/// at that door; giving them the door must not give them the board, must not give them the door of
/// the group's other events, and must not demote an owner who is handed a scanner.</para>
///
/// <para><b>And that an unaccepted invitation grants nothing.</b> It needs no rule of its own —
/// the row has no account attached — but "needs no rule" is exactly the kind of claim that is true
/// until somebody writes a query that forgets it.</para>
/// </remarks>
public sealed class HostedEventStaffTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid StewardId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    private static readonly Guid SaturdayEventId = Guid.NewGuid();
    private static readonly Guid SundayEventId = Guid.NewGuid();

    /// <summary>
    /// An access service whose group answers are all no.
    /// </summary>
    /// <remarks>
    /// The interesting person here is somebody the GROUP would refuse: if the group already said
    /// yes, the staff row is not what let them through and the test would prove nothing.
    /// </remarks>
    private static HostedEventAccess RefusedByTheGroup()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new HostedEventAccess(security.Object);
    }

    private static HostedEventAccess AllowedByTheGroup()
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new HostedEventAccess(security.Object);
    }

    /// <summary>Two events at one venue, so "this event only" has something to fail against.</summary>
    private static async Task<SqliteTestDb> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var (id, name) in new[]
                 { (HostId, "The Host"), (StewardId, "A Steward"), (StrangerId, "A Stranger") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}@example.test",
                DisplayName = name, DateCreated = now,
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = now, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = now, CreatedByAppUserId = HostId,
        });

        foreach (var (id, name) in new[]
                 { (SaturdayEventId, "Saturday"), (SundayEventId, "Sunday") })
        {
            db.HostedEvents.Add(new HostedEvent
            {
                Id = id, OrganizationId = OrgId, PlaceId = PlaceId,
                Name = name, UrlName = name.ToLowerInvariant(),
                StartsOn = now.Date.AddDays(20), EndsOn = now.Date.AddDays(20),
                LifecycleState = HostedEventLifecycleState.Published,
                DateCreated = now, CreatedByAppUserId = HostId,
            });
        }

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task HelpsAsync(
        SqliteTestDb sqlite, Guid eventId, Guid? appUserId, string? email = null,
        bool runsTheDoor = false, bool seesBookings = false, bool decides = false,
        bool accepted = true)
    {
        await using var db = await sqlite.NewContextAsync();

        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            AppUserId = appUserId,
            Email = email,
            RunsTheDoor = runsTheDoor,
            SeesBookings = seesBookings,
            Decides = decides,
            DateConfirmed = accepted ? DateTime.UtcNow : null,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = HostId,
        });

        await db.SaveChangesAsync();
    }

    // ── what a staff row grants ──────────────────────────────────────────────

    [Fact]
    public async Task Somebody_handed_the_door_may_run_it_and_may_not_read_the_board()
    {
        // The whole reason the table exists, and the line it must not cross: a steward with a
        // phone is trusted with letting people in, not with their addresses and allergies.
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, StewardId, runsTheDoor: true);

        var access = RefusedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await access.CanRunTheDoorAsync(StewardId, OrgId, SaturdayEventId, db, default));
        Assert.False(await access.CanReadBookingsAsync(StewardId, OrgId, SaturdayEventId, db, default));
        Assert.False(await access.CanDecideBookingsAsync(StewardId, OrgId, SaturdayEventId, db, default));
    }

    [Fact]
    public async Task Helping_at_one_event_is_helping_at_one_event()
    {
        // A steward for Saturday is nobody on Sunday. Without the event in the query this would be
        // a permission over the whole group, granted by whoever ran one weekend.
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, StewardId, runsTheDoor: true);

        var access = RefusedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await access.CanRunTheDoorAsync(StewardId, OrgId, SaturdayEventId, db, default));
        Assert.False(await access.CanRunTheDoorAsync(StewardId, OrgId, SundayEventId, db, default));
    }

    [Fact]
    public async Task Deciding_carries_seeing_because_nobody_can_answer_what_they_cannot_read()
    {
        // Two flags with one sensible combination: a helper who may say yes to a booking has to be
        // able to see the booking, and a screen that let them decide blind would be a trap.
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, StewardId, decides: true);

        var access = RefusedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await access.CanDecideBookingsAsync(StewardId, OrgId, SaturdayEventId, db, default));
        Assert.True(await access.CanReadBookingsAsync(StewardId, OrgId, SaturdayEventId, db, default));
    }

    [Fact]
    public async Task An_invitation_nobody_has_accepted_grants_nothing()
    {
        // It has no account attached, and nobody's id is null.
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, appUserId: null,
                         email: "steward@example.test", runsTheDoor: true, accepted: false);

        var access = RefusedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await access.CanRunTheDoorAsync(StewardId, OrgId, SaturdayEventId, db, default));
        Assert.False(await access.CanRunTheDoorAsync(StrangerId, OrgId, SaturdayEventId, db, default));
    }

    [Fact]
    public async Task A_row_attached_to_an_account_but_not_accepted_still_grants_nothing()
    {
        // The state the column exists for, and one the database allows even though today's
        // endpoints do not produce it: a row naming a person who has not said yes. Access reads
        // the acceptance, not the attachment — otherwise the day somebody adds a "pre-fill their
        // account" convenience, it silently becomes a grant nobody agreed to.
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, StewardId, runsTheDoor: true, accepted: false);

        var access = RefusedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await access.CanRunTheDoorAsync(StewardId, OrgId, SaturdayEventId, db, default));
    }

    [Fact]
    public async Task Somebody_who_never_helped_is_refused_like_anybody_else()
    {
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, StewardId, runsTheDoor: true);

        var access = RefusedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await access.CanRunTheDoorAsync(StrangerId, OrgId, SaturdayEventId, db, default));
    }

    // ── what it must never take away ─────────────────────────────────────────

    [Fact]
    public async Task Being_handed_a_scanner_does_not_demote_an_owner()
    {
        // Staff rows only ever ADD. A group's owner given the door for one weekend keeps
        // everything they had, and the flags they were given are beside the point.
        await using var sqlite = await SeedAsync();
        await HelpsAsync(sqlite, SaturdayEventId, HostId, runsTheDoor: true);

        var access = AllowedByTheGroup();
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await access.CanReadBookingsAsync(HostId, OrgId, SaturdayEventId, db, default));
        Assert.True(await access.CanDecideBookingsAsync(HostId, OrgId, SaturdayEventId, db, default));
        Assert.True(await access.CanRunTheDoorAsync(HostId, OrgId, SundayEventId, db, default));
    }
}
