using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The pass a door scans, and what the door is told (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"Generate a QR code for the confirmation the event organizer can scan
/// to check them in when they arrive so check in is smoother."</i></para>
///
/// <para><b>The claim under test is that a pass never says something that has stopped being
/// true.</b> A booking that changed, was turned down or was released takes its pass with it, and
/// the door is told which of those happened — because a person on a door has to say something to
/// the person standing in front of them, and "invalid" is not a sentence anybody can act on.</para>
/// </remarks>
public sealed class EventPassTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid OtherEventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();

    // ── issuing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirming_gives_a_booking_a_pass_and_asking_again_gives_the_same_one()
    {
        // Two live codes for one party is two codes at a door, one of which is the wrong one.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await AddBookingAsync(sqlite, HostedEventBookingStatus.Confirmed);

        Guid firstId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            var first = await EventPasses.EnsureAsync(db, booking, HostId, default);
            await db.SaveChangesAsync();
            firstId = first.Id;
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            var again = await EventPasses.EnsureAsync(db, booking, HostId, default);
            await db.SaveChangesAsync();

            Assert.Equal(firstId, again.Id);
            Assert.Single(await db.HostedEventPasses.ToListAsync());
        }
    }

    [Fact]
    public async Task A_reissue_withdraws_the_old_one_and_remembers_which_it_replaced()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await AddBookingAsync(sqlite, HostedEventBookingStatus.Confirmed);

        Guid oldId, newId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            oldId = (await EventPasses.EnsureAsync(db, booking, HostId, default)).Id;
            await db.SaveChangesAsync();

            newId = (await EventPasses.ReissueAsync(
                db, booking, HostId, "The booking changed.", default)).Id;
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var old = await db.HostedEventPasses.FirstAsync(p => p.Id == oldId);
            var fresh = await db.HostedEventPasses.FirstAsync(p => p.Id == newId);

            Assert.NotNull(old.RevokedUtc);
            Assert.Equal("The booking changed.", old.RevokedReason);
            Assert.Null(fresh.RevokedUtc);
            // The old one is KEPT, so "what happened to the code I was sent" has an answer.
            Assert.Equal(oldId, fresh.ReissuedFromHostedEventPassId);
            Assert.NotEqual(old.Token, fresh.Token);

            var live = await EventPasses.LiveAsync(db, bookingId, default);
            Assert.Equal(newId, live!.Id);
        }
    }

    [Fact]
    public async Task Turning_a_booking_down_leaves_no_live_pass()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await AddBookingAsync(sqlite, HostedEventBookingStatus.Confirmed);

        await using (var db = await sqlite.NewContextAsync())
        {
            var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);
            await EventPasses.EnsureAsync(db, booking, HostId, default);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            await EventPasses.RevokeAllAsync(
                db, bookingId, HostId, "The venue could not take this booking.", default);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
            Assert.Null(await EventPasses.LiveAsync(db, bookingId, default));
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_reason()
    {
        // The second is a double click, and it would overwrite the true reason with a later one.
        var pass = new HostedEventPass
        {
            Id = Guid.NewGuid(), Token = "T", IssuedUtc = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };

        EventPasses.Revoke(pass, HostId, "The booking changed.");
        var first = pass.RevokedUtc;
        EventPasses.Revoke(pass, HostId, "Something else entirely.");

        Assert.Equal("The booking changed.", pass.RevokedReason);
        Assert.Equal(first, pass.RevokedUtc);
    }

    [Fact]
    public void A_pass_belongs_only_to_a_confirmed_booking()
    {
        Assert.True(EventPasses.MayHaveAPass(HostedEventBookingStatus.Confirmed));
        foreach (var status in new[]
                 {
                     HostedEventBookingStatus.Requested,
                     HostedEventBookingStatus.TurnedDown,
                     HostedEventBookingStatus.Cancelled,
                 })
            Assert.False(EventPasses.MayHaveAPass(status));
    }

    // ── what the door is told ────────────────────────────────────────────────

    [Fact]
    public void A_live_pass_at_its_own_door_is_admitted()
        => Assert.Null(EventPasses.WhyThisScanIsRefused(Pass(), EventId, null));

    [Fact]
    public void A_code_nobody_recognises_says_what_to_do_instead()
    {
        var refusal = EventPasses.WhyThisScanIsRefused(null, EventId, null);

        Assert.Contains("don't recognise", refusal);
        Assert.Contains("look them up by name", refusal);
    }

    [Fact]
    public void Last_months_pass_is_named_rather_than_called_invalid()
    {
        // The commonest real case at a door. Naming the other event ends the conversation where
        // "invalid" starts an argument.
        var pass = Pass();
        pass.HostedEventBooking.HostedEventId = OtherEventId;

        var refusal = EventPasses.WhyThisScanIsRefused(pass, EventId, "The October Weekend");

        Assert.Equal("That pass is for The October Weekend, not this event.", refusal);
    }

    [Fact]
    public void A_withdrawn_pass_reads_the_venues_own_reason_out()
    {
        var pass = Pass();
        EventPasses.Revoke(pass, HostId, "The booking changed. Ask them for the newer pass.");

        var refusal = EventPasses.WhyThisScanIsRefused(pass, EventId, null);

        Assert.Contains("withdrawn", refusal);
        Assert.Contains("Ask them for the newer pass", refusal);
    }

    [Fact]
    public void A_pass_whose_booking_is_no_longer_confirmed_is_refused_by_the_state_it_is_in()
    {
        var stillAsking = Pass();
        stillAsking.HostedEventBooking.Status = HostedEventBookingStatus.Requested;
        Assert.Equal("That booking hasn't been confirmed yet.",
            EventPasses.WhyThisScanIsRefused(stillAsking, EventId, null));

        var gone = Pass();
        gone.HostedEventBooking.Status = HostedEventBookingStatus.Cancelled;
        Assert.Equal("That booking is no longer live.",
            EventPasses.WhyThisScanIsRefused(gone, EventId, null));
    }

    // ── the picture ──────────────────────────────────────────────────────────

    [Fact]
    public void The_code_draws_as_a_real_png()
    {
        var png = EventPasses.Png("0123456789ABCDEF0123456789ABCDEF");

        // The eight-byte PNG signature. A library that quietly returned something else would
        // otherwise be found by a guest holding a broken image at a door.
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A],
            png.Take(8).ToArray());
        Assert.True(png.Length > 100);
    }

    [Fact]
    public void An_inlined_code_is_a_data_uri_a_mail_client_can_draw_without_asking()
    {
        // Most mail clients block remote pictures until somebody clicks, and a guest at a door
        // whose pass never loaded has no pass.
        var uri = EventPasses.DataUri("0123456789ABCDEF0123456789ABCDEF");

        Assert.StartsWith("data:image/png;base64,", uri);
        Assert.True(Convert.FromBase64String(uri["data:image/png;base64,".Length..]).Length > 100);
    }

    [Fact]
    public async Task Two_passes_never_share_a_token()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var bookingId = await AddBookingAsync(sqlite, HostedEventBookingStatus.Confirmed);

        await using var db = await sqlite.NewContextAsync();
        var booking = await db.HostedEventBookings.FirstAsync(b => b.Id == bookingId);

        var tokens = new HashSet<string>();
        for (var i = 0; i < 25; i++)
        {
            var pass = await EventPasses.ReissueAsync(db, booking, HostId, "again", default);
            await db.SaveChangesAsync();
            Assert.True(tokens.Add(pass.Token));
        }
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static HostedEventPass Pass() => new()
    {
        Id = Guid.NewGuid(),
        Token = "0123456789ABCDEF",
        IssuedUtc = DateTime.UtcNow,
        HostedEventBooking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = EventId,
            Status = HostedEventBookingStatus.Confirmed,
            PartySize = 2,
        },
    };

    private static async Task<Guid> AddBookingAsync(
        SqliteTestDb sqlite, HostedEventBookingStatus status)
    {
        await using var db = await sqlite.NewContextAsync();
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = GuestId,
            PartySize = 2, Kind = HostedEventBookingKind.DayPass, Status = status,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = GuestId,
        };
        db.HostedEventBookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        foreach (var (id, name) in new[] { (HostId, "The Host"), (GuestId, "A Guest") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, Email = $"{id:N}@example.com", UserName = $"{id:N}@example.com",
                DisplayName = name, DateCreated = DateTime.UtcNow,
            });
        }

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
            IsPublished = true, DayPassCapacity = 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        await db.SaveChangesAsync();
    }
}
