using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Tickets at the door: a QR code the organizer prints or puts on a screen, which a walk-up scans to
/// ask for a place on their own phone, and the door confirms once they have paid (event-scenario-gaps).
/// </summary>
/// <remarks>
/// <para>Ben's scenario: <i>"there will be the ability for Jim to sell tickets at the door if he wants.
/// Our site will give him a QR code for him to print or display for people to use to get tickets at the
/// door."</i></para>
///
/// <para><b>The site still takes no money.</b> Scanning asks for a day pass, exactly as the event's own
/// page would, and marks it as asked at the door. The person on the door takes the money however the
/// organizer does, then confirms it here, and the guest's phone shows their pass.</para>
///
/// <para><b>The door may confirm these and only these.</b> Selling a ticket at the door is the door's
/// job, so somebody trusted with the door (and nothing else) may confirm a request made by scanning the
/// door's code — never any other booking, which stays with the people who decide bookings.</para>
///
/// <para><b>Signing in is needed to ask.</b> A place belongs to an account, the account is where the pass
/// is shown, and an address typed at the door proves nothing. The page sends them to sign in or make
/// an account and back again.</para>
/// </remarks>
public sealed class HostedEventDoorTicketsController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;
    private readonly HostedEventCalendarSync _sync;
    private readonly EventGuestMailer _mail;
    private readonly Ben.Data.Common.SiteIdentity _site;

    public HostedEventDoorTicketsController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, HostedEventCalendarSync sync, EventGuestMailer mail,
        IOptions<Ben.Data.Common.SiteIdentity> site)
        : base(dbFactory, mapper, security)
    {
        _access = access;
        _sync = sync;
        _mail = mail;
        _site = site.Value;
    }

    // ── the organizer and the door ───────────────────────────────────────────

    /// <summary>Whether the door is selling, the code's address, and who is waiting at the door.</summary>
    [HttpGet("~/api/organizations/{orgId:guid}/events/{eventId:guid}/door-tickets")]
    public async Task<ActionResult<HostedEventDoorTicketsRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await MayWorkTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        return Ok(await RecordAsync(db, ev, userId.Value, orgId, ct));
    }

    /// <summary>Turns tickets at the door on (a new code) or off (the old code stops working).</summary>
    [HttpPut("~/api/organizations/{orgId:guid}/events/{eventId:guid}/door-tickets")]
    public async Task<ActionResult<HostedEventDoorTicketsRecord>> Set(
        Guid orgId, Guid eventId, [FromBody] SetHostedEventDoorTicketsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        if (request.On && ev.DayPassCapacity is 0)
            return BadRequest("This event sells no day passes, so there is nothing to sell at the door. "
                            + "Set how many day passes it has on the event's page first.");

        ev.DoorTicketToken = request.On
            ? ev.DoorTicketToken ?? NewToken()
            : null;
        ev.DateUpdated = DateTime.UtcNow;
        ev.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(await RecordAsync(db, ev, userId.Value, orgId, ct));
    }

    /// <summary>
    /// Confirms a place somebody asked for by scanning the door's code, once they have paid.
    /// </summary>
    [HttpPost("~/api/organizations/{orgId:guid}/events/{eventId:guid}/door-tickets/{bookingId:guid}/confirm")]
    public async Task<ActionResult<HostedEventDoorTicketsRecord>> Confirm(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await MayWorkTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var booking = await db.HostedEventBookings.Include(b => b.Nights).Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.HostedEventId == eventId, ct);
        if (booking is null) return NotFound();
        if (booking.AskedAtTheDoorUtc is null)
            return BadRequest("That place wasn't asked for at the door. Whoever decides bookings confirms it on the board.");
        if (booking.Status == HostedEventBookingStatus.Confirmed)
            return BadRequest("That place is already confirmed.");
        if (booking.Status != HostedEventBookingStatus.Requested)
            return BadRequest("That request was withdrawn or answered already.");

        var all = await db.HostedEventBookings.AsNoTracking().Where(b => b.HostedEventId == eventId).ToListAsync(ct);
        if (EventCapacity.WhyTheseDayPassesCannotBeGiven(
                ev.DayPassCapacity, EventCapacity.DayPassesTaken(all, excludingBookingId: booking.Id),
                EventCapacity.ClampPartySize(booking.PartySize)) is { } full)
            return BadRequest(full);

        var now = DateTime.UtcNow;
        BookingTransitions.Confirm(booking, userId.Value, "Paid at the door.", now);
        booking.GuestAcknowledgedUtc = null;
        booking.DateUpdated = now;
        booking.UpdatedByAppUserId = userId.Value;
        await BookingTransitions.ApplyUmbrellaAsync(db, _sync, ev, booking, userId.Value, now, ct);
        await EventPasses.EnsureAsync(db, booking, userId.Value, ct);

        await SaveInOneTransactionAndAuditAsync(db, booking, booking.Id, userId.Value,
            () => _mail.SendDecisionAsync(db, booking.Id, ct), ct);

        return Ok(await RecordAsync(db, ev, userId.Value, orgId, ct));
    }

    // ── the person at the door ───────────────────────────────────────────────

    /// <summary>What the door's code leads to, before anybody asks.</summary>
    [HttpGet("~/api/public/door-tickets/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicDoorTicketRecord>> Info(string token, CancellationToken ct)
    {
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await ByTokenAsync(db, token, ct);
        if (ev is null) return NotFound();

        var userId = GetCurrentUserId();
        var mine = userId is Guid me
            ? await db.HostedEventBookings.AsNoTracking()
                .Where(b => b.HostedEventId == ev.Id && b.LeadAppUserId == me
                         && b.Status != HostedEventBookingStatus.Cancelled && b.Status != HostedEventBookingStatus.Expired)
                .Select(b => (HostedEventBookingStatus?)b.Status).FirstOrDefaultAsync(ct)
            : null;

        return Ok(ToPublic(ev, mine));
    }

    /// <summary>Asks for a place at the door. The door confirms it once they have paid.</summary>
    [HttpPost("~/api/public/door-tickets/{token}")]
    [Authorize]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    public async Task<ActionResult<PublicDoorTicketRecord>> Ask(
        string token, [FromBody] AskAtTheDoorRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await ByTokenAsync(db, token, ct);
        if (ev is null) return NotFound();

        if (WhyTheDoorIsNotSelling(ev, DateTime.UtcNow) is { } shut) return Conflict(shut);

        var (contact, missing) = await BookingContact.ForAccountAsync(
            db, userId.Value, request.FirstName, request.LastName, request.Phone, ct);
        if (contact is null) return BadRequest(missing);

        var existing = await db.HostedEventBookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.HostedEventId == ev.Id && b.LeadAppUserId == userId
                                   && b.Status != HostedEventBookingStatus.Cancelled
                                   && b.Status != HostedEventBookingStatus.Expired, ct);
        if (existing is not null)
            return Conflict(existing.Status == HostedEventBookingStatus.Confirmed
                ? "You already have a place. Show your pass at the door."
                : "You've already asked. Show this screen at the door.");

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = ev.Id,
            LeadAppUserId = userId.Value,
            PartySize = EventCapacity.ClampPartySize(request.PartySize),
            Kind = HostedEventBookingKind.DayPass,
            Status = HostedEventBookingStatus.Requested,
            ContactPhone = contact.Phone,
            Note = "Asked at the door.",
            AskedAtTheDoorUtc = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };
        db.HostedEventBookings.Add(booking);
        await BookingContact.FillEmptyNamesAsync(db, userId.Value, contact, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToPublic(ev, HostedEventBookingStatus.Requested));
    }

    // ── the rules ────────────────────────────────────────────────────────────

    /// <summary>Why nobody can ask at the door now, or null when they can.</summary>
    internal static string? WhyTheDoorIsNotSelling(HostedEvent ev, DateTime now)
    {
        if (ev.DoorTicketToken is null) return "The door isn't selling tickets.";
        if (HostedEventStates.CalledOff.Contains(ev.LifecycleState)) return "This event was called off.";
        if (!HostedEventStates.TakingBookings.Contains(ev.LifecycleState)) return "This event is over.";
        if (ev.DayPassCapacity is 0) return "This event isn't selling day passes.";
        return null;
    }

    /// <summary>Where the door's code points, relative to the site.</summary>
    public static string DoorPath(string token) => $"/door-tickets/{token}";

    private static string NewToken()
        => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static Task<HostedEvent?> ByTokenAsync(BenDataContext db, string token, CancellationToken ct)
        => string.IsNullOrWhiteSpace(token) || token.Length > 64
            ? Task.FromResult<HostedEvent?>(null)
            : db.HostedEvents.AsNoTracking().Include(e => e.Organization).Include(e => e.Nights)
                .FirstOrDefaultAsync(e => e.DoorTicketToken == token, ct);

    private async Task<bool> MayWorkTheDoorAsync(Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => await _access.CanRunTheDoorAsync(userId, orgId, eventId, db, ct)
        || await _access.CanDecideBookingsAsync(userId, orgId, eventId, db, ct);

    private PublicDoorTicketRecord ToPublic(HostedEvent ev, HostedEventBookingStatus? mine)
    {
        var nights = ev.Nights.Select(n => n.Date.Date).OrderBy(d => d).ToList();
        var dateLine = nights.Count switch
        {
            0 => ev.StartsOn.ToString("ddd MM/dd/yyyy"),
            1 => nights[0].ToString("ddd MM/dd/yyyy"),
            _ => $"{nights[0]:ddd MM/dd/yyyy} – {nights[^1]:ddd MM/dd/yyyy}",
        };
        return new PublicDoorTicketRecord(
            ev.Id, ev.Name, ev.Organization?.Name ?? "", ev.Organization?.UrlName ?? "", ev.UrlName,
            dateLine, ev.DayPassPrice, ev.ContactLine,
            WhyTheDoorIsNotSelling(ev, DateTime.UtcNow), mine);
    }

    private async Task<HostedEventDoorTicketsRecord> RecordAsync(
        BenDataContext db, HostedEvent ev, Guid userId, Guid orgId, CancellationToken ct)
    {
        var waiting = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == ev.Id && b.AskedAtTheDoorUtc != null
                     && (b.Status == HostedEventBookingStatus.Requested || b.Status == HostedEventBookingStatus.Confirmed))
            .OrderBy(b => b.AskedAtTheDoorUtc)
            .Select(b => new HostedEventDoorRequestRecord(
                b.Id,
                (b.LeadAppUser.FirstName != null && b.LeadAppUser.LastName != null
                    ? b.LeadAppUser.FirstName + " " + b.LeadAppUser.LastName
                    : b.LeadAppUser.DisplayName ?? b.LeadAppUser.UserName) ?? "A guest",
                b.PartySize, b.AskedAtTheDoorUtc!.Value, b.Status == HostedEventBookingStatus.Confirmed))
            .ToListAsync(ct);

        return new HostedEventDoorTicketsRecord(
            ev.Id, ev.DoorTicketToken is not null,
            ev.DoorTicketToken is { } t ? _site.AbsoluteUrl(DoorPath(t)) : null,
            WhyTheDoorIsNotSelling(ev, DateTime.UtcNow) is { } why && ev.DoorTicketToken is not null ? why : null,
            ev.DayPassPrice,
            await _access.CanEditEventAsync(userId, orgId, ct),
            waiting);
    }
}
