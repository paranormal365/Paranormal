using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// How Jim's guests get their tickets (event-scenario-gaps): his list of people, a ticket he sends
/// somebody who paid him, a booking he confirms on the spot, and the QR code at the door.
/// </summary>
/// <remarks>
/// Ben's scenario: <i>"They either can request tickets or Jim can generate them on our site … there will be
/// the ability for Jim to sell tickets at the door if he wants. Our site will give him a QR code for him to
/// print or display for people to use to get tickets at the door."</i> The site still takes no money.
/// </remarks>
public sealed class TicketsTests
{
    // ── a pasted list ────────────────────────────────────────────────────────

    [Fact]
    public void A_pasted_list_is_split_however_it_came_and_the_typos_handed_back()
    {
        var (addresses, not) = HostedEventBookingController.SplitAddresses(
            "Jim Smith <JIM@example.com>, sam@example.com; sam@example.com\nnot-an-address\n\n  pat@example.org  ");

        Assert.Equal(["jim@example.com", "sam@example.com", "pat@example.org"], addresses);
        Assert.Equal(["not-an-address"], not);
    }

    [Fact]
    public void A_ticket_letter_says_the_place_is_confirmed_on_opening_and_carries_the_organizers_words()
    {
        var ticket = HostedEventBookingController.InviteLetter("Overnight", new DateTime(2026, 10, 30), "https://x/attending/T", isTicket: true,
            message: "Paid, thanks!\n\nSee you there <b>soon</b>.");
        Assert.Contains("Get your ticket", ticket);
        Assert.Contains("confirms your place", ticket);
        Assert.Contains("Paid, thanks!", ticket);
        Assert.DoesNotContain("<b>soon</b>", ticket);   // the organizer's words are escaped

        var invitation = HostedEventBookingController.InviteLetter("Overnight", new DateTime(2026, 10, 30), "https://x/attending/T", isTicket: false, message: null);
        Assert.Contains("Accept the invitation", invitation);
        Assert.Contains("will confirm your place", invitation);
    }

    [Fact]
    public async Task Inviting_a_list_writes_to_each_once_with_the_message_and_says_what_it_skipped()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Published);
        var letters = new ScenarioWorld.Letters();

        var result = Assert.IsType<HostedEventGuestInvitesRecord>(Assert.IsType<OkObjectResult>(
            (await w.Board(letters, ScenarioWorld.Jim).InviteMany(w.OrgId, w.EventId,
                new InviteHostedEventGuestsRequest("a@example.test, b@example.test\nnope", "Come to the Factory. $45, paid to me."), default)).Result).Value);

        Assert.Equal(["a@example.test", "b@example.test"], result.Invited);
        Assert.Equal(["nope"], result.NotAddresses);
        Assert.True(result.Sent);
        Assert.Equal(["a@example.test", "b@example.test"], letters.Sent.Select(l => l.To).Order());
        Assert.All(letters.Sent, l => Assert.Contains("Come to the Factory. $45, paid to me.", l.HtmlBody));

        await using var db = await w.Sqlite.NewContextAsync();
        Assert.Equal(2, await db.EventAttendanceInvites.CountAsync(i => i.InvitedByAppUserId == ScenarioWorld.Jim && !i.ConfirmsOnAccept));
    }

    [Fact]
    public async Task Only_somebody_who_decides_bookings_may_invite_a_list()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Published);
        Assert.IsType<ForbidResult>((await w.Board(new(), ScenarioWorld.Nephew).InviteMany(
            w.OrgId, w.EventId, new InviteHostedEventGuestsRequest("a@example.test"), default)).Result);
    }

    // ── a ticket Jim sends ───────────────────────────────────────────────────

    private static UserManager<AppUser> Users(ScenarioWorld w)
        => new(new UserStore<AppUser, IdentityRole<Guid>, BenDataContext, Guid>(w.Sqlite.Factory.CreateDbContext()),
               Options.Create(new IdentityOptions()), new PasswordHasher<AppUser>(), [], [],
               new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<AppUser>>.Instance);

    private static PublicEventAttendanceController Attendance(ScenarioWorld w, ScenarioWorld.Letters letters)
        => new(w.Sqlite.Factory, letters, Users(w), ScenarioWorld.Site, NullLogger<PublicEventAttendanceController>.Instance,
               new Ben.Data.WebApi.Services.UserHandleService(w.Sqlite.Factory), Support.SilentTourMail.Instance,
               new ForwardingOutboxQueue(letters))
        { ControllerContext = ScenarioWorld.SignedInAs(null) };

    private static Ben.Data.WebApi.Services.EmailLinkAccounts Accounts(ScenarioWorld w)
        => new(Users(w), new Ben.Data.WebApi.Services.UserHandleService(w.Sqlite.Factory), NullLogger<Ben.Data.WebApi.Services.EmailLinkAccounts>.Instance);

    /// <summary>
    /// The ticket's link confirms the place and sends the pass; an invitation's link only asks.
    /// </summary>
    /// <remarks>
    /// The "Book somebody in" button this replaces never worked: it sent no account and the server refused
    /// every one ("Choose who this booking is for…").
    /// </remarks>
    [Fact]
    public async Task Opening_a_ticket_confirms_the_place_and_sends_the_pass_and_an_invitation_only_asks()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Published);
        var letters = new ScenarioWorld.Letters();
        var board = w.Board(letters, ScenarioWorld.Jim);

        Assert.IsType<OkObjectResult>((await board.InviteByEmail(w.OrgId, w.EventId,
            new InviteHostedEventGuestRequest("paid@example.test", "Paid Person", 2, ConfirmsOnAccept: true, Message: "Thanks for paying."), default)).Result);
        Assert.IsType<OkObjectResult>((await board.InviteByEmail(w.OrgId, w.EventId,
            new InviteHostedEventGuestRequest("maybe@example.test"), default)).Result);

        var ticketLetter = Assert.Single(letters.Sent, l => l.To == "paid@example.test");
        Assert.Equal("Your ticket for Overnight at The Factory", ticketLetter.Subject);
        Assert.Contains("Thanks for paying.", ticketLetter.HtmlBody);

        string ticketToken, inviteToken;
        await using (var db = await w.Sqlite.NewContextAsync())
        {
            ticketToken = (await db.EventAttendanceInvites.SingleAsync(i => i.Email == "paid@example.test")).Token!;
            inviteToken = (await db.EventAttendanceInvites.SingleAsync(i => i.Email == "maybe@example.test")).Token!;
        }

        letters.Sent.Clear();
        var opened = Assert.IsType<EventAttendanceConfirmation>(Assert.IsType<OkObjectResult>(
            (await Attendance(w, letters).Confirm(ticketToken, w.Mailer(letters), Accounts(w), default)).Result).Value);
        Assert.True(opened.IsConfirmed);
        var asked = Assert.IsType<EventAttendanceConfirmation>(Assert.IsType<OkObjectResult>(
            (await Attendance(w, letters).Confirm(inviteToken, w.Mailer(letters), Accounts(w), default)).Result).Value);
        Assert.False(asked.IsConfirmed);

        await using var read = await w.Sqlite.NewContextAsync();
        var paid = await read.HostedEventBookings.Include(b => b.LeadAppUser).SingleAsync(b => b.LeadAppUser.Email == "paid@example.test");
        Assert.Equal(HostedEventBookingStatus.Confirmed, paid.Status);
        Assert.Equal(2, paid.PartySize);
        Assert.NotNull(await EventPasses.LiveAsync(read, paid.Id, default));
        var maybe = await read.HostedEventBookings.Include(b => b.LeadAppUser).SingleAsync(b => b.LeadAppUser.Email == "maybe@example.test");
        Assert.Equal(HostedEventBookingStatus.Requested, maybe.Status);

        // The pass letter went to the ticket holder; the invitation got "asked" words.
        Assert.Contains(letters.Sent, l => l.To == "paid@example.test" && l.HtmlBody.Contains("data:image/png;base64,"));
    }

    /// <summary>A booking the host confirms on the spot carries a pass and its letter, like any confirmed one.</summary>
    [Fact]
    public async Task Booking_somebody_in_confirmed_gives_them_a_pass_and_writes_to_them()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Published);
        var letters = new ScenarioWorld.Letters();

        var made = Assert.IsType<HostedEventBookingRecord>(Assert.IsType<OkObjectResult>(
            (await w.Board(letters, ScenarioWorld.Jim).CreateOnBehalf(w.OrgId, w.EventId,
                new CreateHostedEventBookingOnBehalfRequest(ScenarioWorld.Stranger, null, null, HostedEventBookingKind.DayPass, 1,
                    ConfirmImmediately: true), default)).Result).Value);

        await using var db = await w.Sqlite.NewContextAsync();
        Assert.NotNull(await EventPasses.LiveAsync(db, made.Id, default));
        Assert.Contains(letters.Sent, l => l.To == "stranger@example.test" && l.Subject.Contains("is confirmed"));
    }

    // ── tickets at the door ──────────────────────────────────────────────────

    private static HostedEventDoorTicketsController Door(ScenarioWorld w, ScenarioWorld.Letters letters, Guid? who)
        => new(w.Sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, w.Security().Object, w.Access(),
               new HostedEventCalendarSync(), w.Mailer(letters), ScenarioWorld.Site)
        { ControllerContext = ScenarioWorld.SignedInAs(who) };

    [Fact]
    public async Task The_door_sells_by_qr_code_and_the_door_confirms_what_was_asked_there_and_nothing_else()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Live);
        var letters = new ScenarioWorld.Letters();

        // Only the organizer turns it on; the nephew on the door may not.
        Assert.IsType<ForbidResult>((await Door(w, letters, ScenarioWorld.Nephew).Set(w.OrgId, w.EventId, new(true), default)).Result);
        var on = Assert.IsType<HostedEventDoorTicketsRecord>(Assert.IsType<OkObjectResult>(
            (await Door(w, letters, ScenarioWorld.Jim).Set(w.OrgId, w.EventId, new(true), default)).Result).Value);
        Assert.True(on.On);
        Assert.StartsWith("https://example.test/door-tickets/", on.Url);
        var token = on.Url![(on.Url.LastIndexOf('/') + 1)..];

        // What the scanner sees, signed out: the event and its price, and no place yet.
        var seen = Assert.IsType<PublicDoorTicketRecord>(Assert.IsType<OkObjectResult>((await Door(w, letters, null).Info(token, default)).Result).Value);
        Assert.Equal(("Overnight at The Factory", 45m, (HostedEventBookingStatus?)null), (seen.Name, seen.DayPassPrice, seen.MyStatus));
        Assert.Null(seen.NotSellingSentence);

        // The stranger asks at the door.
        var asked = Assert.IsType<PublicDoorTicketRecord>(Assert.IsType<OkObjectResult>(
            (await Door(w, letters, ScenarioWorld.Stranger).Ask(token, new AskAtTheDoorRequest(2, "Sam", "Stranger", "615-555-0100"), default)).Result).Value);
        Assert.Equal(HostedEventBookingStatus.Requested, asked.MyStatus);
        Assert.IsType<ConflictObjectResult>((await Door(w, letters, ScenarioWorld.Stranger).Ask(token, new AskAtTheDoorRequest(2, "Sam", "Stranger", "615-555-0100"), default)).Result);

        var waiting = Assert.IsType<HostedEventDoorTicketsRecord>(Assert.IsType<OkObjectResult>((await Door(w, letters, ScenarioWorld.Nephew).Get(w.OrgId, w.EventId, default)).Result).Value);
        var request = Assert.Single(waiting.Requests);
        Assert.False(waiting.CanChange);

        // The nephew may confirm the door's own request…
        var confirmed = Assert.IsType<HostedEventDoorTicketsRecord>(Assert.IsType<OkObjectResult>(
            (await Door(w, letters, ScenarioWorld.Nephew).Confirm(w.OrgId, w.EventId, request.BookingId, default)).Result).Value);
        Assert.True(Assert.Single(confirmed.Requests).IsConfirmed);
        await using (var db = await w.Sqlite.NewContextAsync())
            Assert.NotNull(await EventPasses.LiveAsync(db, request.BookingId, default));
        Assert.Contains(letters.Sent, l => l.To == "stranger@example.test");

        // …and never a booking asked for anywhere else.
        var refused = Assert.IsType<BadRequestObjectResult>((await Door(w, letters, ScenarioWorld.Nephew).Confirm(w.OrgId, w.EventId, w.AskedBookingId, default)).Result);
        Assert.StartsWith("That place wasn't asked for at the door.", (string)refused.Value!);

        // Turning it off kills the printed code.
        await Door(w, letters, ScenarioWorld.Jim).Set(w.OrgId, w.EventId, new(false), default);
        Assert.IsType<NotFoundResult>((await Door(w, letters, null).Info(token, default)).Result);
    }

    [Fact]
    public async Task An_event_with_no_day_passes_cannot_sell_at_the_door()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Published);
        await w.ChangeEventAsync(e => e.DayPassCapacity = 0);

        var refused = Assert.IsType<BadRequestObjectResult>((await Door(w, new(), ScenarioWorld.Jim).Set(w.OrgId, w.EventId, new(true), default)).Result);
        Assert.StartsWith("This event sells no day passes", (string)refused.Value!);
    }

    [Fact]
    public void An_event_that_is_over_or_called_off_sells_nothing_at_the_door()
    {
        var ev = new HostedEvent { DoorTicketToken = "t", LifecycleState = HostedEventLifecycleState.Live };
        Assert.Null(HostedEventDoorTicketsController.WhyTheDoorIsNotSelling(ev, DateTime.UtcNow));
        ev.LifecycleState = HostedEventLifecycleState.Ended;
        Assert.Equal("This event is over.", HostedEventDoorTicketsController.WhyTheDoorIsNotSelling(ev, DateTime.UtcNow));
        ev.LifecycleState = HostedEventLifecycleState.Cancelled;
        Assert.Equal("This event was called off.", HostedEventDoorTicketsController.WhyTheDoorIsNotSelling(ev, DateTime.UtcNow));
    }
}
