using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Tours;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// During and around Jim's night (event-scenario-gaps): who was at a class, the people running the event
/// standing out in its room, a walk's room for the people checked in on it, the evidence page a CMS
/// section draws, and what the menus tell guests.
/// </summary>
public sealed class DuringTheEventTests
{
    // ── classes: who was actually there ──────────────────────────────────────

    private static async Task<(Guid SessionId, Guid InId, Guid WaitingId)> ClassAsync(ScenarioWorld w)
    {
        await using var db = await w.Sqlite.NewContextAsync();
        var session = new HostedEventSession
        {
            Id = Guid.NewGuid(), HostedEventId = w.EventId, Title = "Operating the Ovilus", Capacity = 1, RequiresSignUp = true,
            PlacesTaken = 2, StartsAtUtc = ScenarioWorld.FirstDay.AddHours(21), EndsAtUtc = ScenarioWorld.FirstDay.AddHours(22),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Jim,
        };
        var signedIn = new HostedEventSessionSignUp
        {
            Id = Guid.NewGuid(), HostedEventSessionId = session.Id, AppUserId = ScenarioWorld.Came, HostedEventBookingId = w.CameBookingId,
            People = 2, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
        };
        var waiting = new HostedEventSessionSignUp
        {
            Id = Guid.NewGuid(), HostedEventSessionId = session.Id, AppUserId = ScenarioWorld.Missed, HostedEventBookingId = w.MissedBookingId,
            People = 1, WaitlistedUtc = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Missed,
        };
        db.HostedEventSessions.Add(session);
        db.HostedEventSessionSignUps.AddRange(signedIn, waiting);
        await db.SaveChangesAsync();
        return (session.Id, signedIn.Id, waiting.Id);
    }

    private static HostedEventSessionController Sessions(ScenarioWorld w, Guid who)
        => new(w.Sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, w.Security().Object, w.Access())
        { ControllerContext = ScenarioWorld.SignedInAs(who) };

    /// <summary>
    /// Ben's scenario: "classes … with either limits on people who could attend and track who attends".
    /// The door can mark a class's people there; somebody still waiting was never let in.
    /// </summary>
    [Fact]
    public async Task The_door_marks_who_was_at_a_class_and_a_person_still_waiting_cannot_be_marked()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Live);
        var (sessionId, inId, waitingId) = await ClassAsync(w);
        var door = Sessions(w, ScenarioWorld.Nephew);

        var marked = await door.SetAttended(w.OrgId, w.EventId, sessionId, inId, new(true), default);
        var roster = Assert.IsType<HostedEventSessionRosterRecord>(Assert.IsType<OkObjectResult>(marked.Result ?? new ObjectResult(marked.Value)).Value);
        Assert.NotNull(Assert.Single(roster.In).AttendedUtc);
        Assert.Equal(2, roster.PeopleThere);
        Assert.True(roster.CanMarkAttendance);

        var refused = Assert.IsType<BadRequestObjectResult>((await door.SetAttended(w.OrgId, w.EventId, sessionId, waitingId, new(true), default)).Result);
        Assert.StartsWith("They're still waiting for a place", (string)refused.Value!);

        var undone = Assert.IsType<HostedEventSessionRosterRecord>(Assert.IsType<OkObjectResult>(
            (await door.SetAttended(w.OrgId, w.EventId, sessionId, inId, new(false), default)).Result).Value);
        Assert.Null(Assert.Single(undone.In).AttendedUtc);
        Assert.Equal(0, undone.PeopleThere);

        // A guest may not mark anybody, themselves included.
        Assert.IsType<ForbidResult>((await Sessions(w, ScenarioWorld.Came).SetAttended(w.OrgId, w.EventId, sessionId, inId, new(true), default)).Result);
    }

    /// <summary>
    /// Ben, 10/21/2026: door helpers get a page and an app screen to mark class attendance. The door can't
    /// read the program (the group's), so it gets the classes with their rosters on their own.
    /// </summary>
    [Fact]
    public async Task The_door_reads_every_class_that_takes_sign_ups_with_its_roster()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Live);
        var (sessionId, inId, _) = await ClassAsync(w);

        Assert.IsType<ForbidResult>((await Sessions(w, ScenarioWorld.Nephew).Get(w.OrgId, w.EventId, default)).Result);

        var classes = Assert.IsType<HostedEventClassesRecord>(Assert.IsType<OkObjectResult>(
            (await Sessions(w, ScenarioWorld.Nephew).Classes(w.OrgId, w.EventId, default)).Result).Value);
        var ovilus = Assert.Single(classes.Classes);
        Assert.Equal((sessionId, "Operating the Ovilus", 1), (ovilus.SessionId, ovilus.Title, ovilus.Capacity));
        Assert.Equal(inId, Assert.Single(ovilus.Roster.In).SignUpId);
        Assert.Single(ovilus.Roster.Waiting);
        Assert.True(ovilus.Roster.CanMarkAttendance);

        Assert.IsType<ForbidResult>((await Sessions(w, ScenarioWorld.Came).Classes(w.OrgId, w.EventId, default)).Result);
    }

    [Fact]
    public void Marking_someone_there_twice_keeps_the_first_time()
    {
        var signUp = new HostedEventSessionSignUp();
        var first = new DateTime(2026, 10, 30, 21, 5, 0, DateTimeKind.Utc);
        SessionSignUps.MarkAttended(signUp, true, ScenarioWorld.Nephew, first);
        SessionSignUps.MarkAttended(signUp, true, ScenarioWorld.Jim, first.AddMinutes(10));
        Assert.Equal(first, signUp.AttendedUtc);
        Assert.Equal(ScenarioWorld.Jim, signUp.AttendanceRecordedByAppUserId);
    }

    // ── the event's room: the people running it stand out ───────────────────

    [Fact]
    public async Task In_the_room_the_organizer_and_a_helper_carry_their_role_and_a_guest_carries_none()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Live);
        await using var db = await w.Sqlite.NewContextAsync();
        var ev = await db.HostedEvents.SingleAsync(e => e.Id == w.EventId);

        var roles = await EventRoom.RolesAsync(db, ev, [ScenarioWorld.Jim, ScenarioWorld.Nephew, ScenarioWorld.Came], default);

        Assert.Equal("Organizer", roles[ScenarioWorld.Jim]);
        Assert.Equal("Door", roles[ScenarioWorld.Nephew]);
        Assert.False(roles.ContainsKey(ScenarioWorld.Came));
    }

    // ── a walk's room ────────────────────────────────────────────────────────

    private static async Task<(Guid DateId, Guid Guide, Guid CheckedIn, Guid Reserved)> WalkAsync(ScenarioWorld w, DateTime starts)
    {
        await using var db = await w.Sqlite.NewContextAsync();
        var now = DateTime.UtcNow;
        var tourId = Guid.NewGuid();
        // A tour starts somewhere: SQLite keeps the foreign key to the group's address.
        var typeId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        db.OrganizationAddressTypes.Add(new OrganizationAddressType { Id = typeId, Name = "Start", DateCreated = now, CreatedByAppUserId = ScenarioWorld.Jim });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = w.OrgId, OrganizationAddressTypeId = typeId, StreetAddress1 = "1 Printers Alley",
            City = "Nashville", State = "TN", ZipCode = "37201", DateCreated = now, CreatedByAppUserId = ScenarioWorld.Jim,
        });
        db.Tours.Add(new Tour
        {
            Id = tourId, OrganizationId = w.OrgId, Name = "Printers Alley", UrlName = "printers-alley", TimeZoneId = "America/Chicago",
            StartOrganizationAddressId = addressId, DateCreated = now, CreatedByAppUserId = ScenarioWorld.Jim,
        });
        var dateId = Guid.NewGuid();
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = dateId, OrganizationId = w.OrgId, TourId = tourId, Title = "Printers Alley After Dark", IsPublic = true,
            StartDateTime = starts, EndDateTime = starts.AddHours(2), DateCreated = now, CreatedByAppUserId = ScenarioWorld.Jim,
        });
        db.OrgCalendarEventGuides.Add(new OrgCalendarEventGuide { Id = Guid.NewGuid(), OrgCalendarEventId = dateId, AppUserId = ScenarioWorld.Nephew, DateCreated = now, CreatedByAppUserId = ScenarioWorld.Jim });
        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = dateId, AppUserId = ScenarioWorld.Came, RsvpStatus = RsvpStatus.Accepted,
            SeatStatus = TourSeatStatus.Reserved, CheckedInUtc = starts.AddMinutes(5), DateCreated = now, CreatedByAppUserId = ScenarioWorld.Came,
        });
        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = dateId, AppUserId = ScenarioWorld.Missed, RsvpStatus = RsvpStatus.Accepted,
            SeatStatus = TourSeatStatus.Reserved, DateCreated = now, CreatedByAppUserId = ScenarioWorld.Missed,
        });
        await db.SaveChangesAsync();
        return (dateId, ScenarioWorld.Nephew, ScenarioWorld.Came, ScenarioWorld.Missed);
    }

    private static PublicTourRoomController TourRoomFor(ScenarioWorld w, Guid who)
        => new(w.Sqlite.Factory, w.Security().Object) { ControllerContext = ScenarioWorld.SignedInAs(who) };

    /// <summary>
    /// Ben, 10/20/2026: "stay in touch with people checked in with the event or tour if you both are checked
    /// in", the guide's posts in a different color. A seat that was reserved and never used is no place in it.
    /// </summary>
    [Fact]
    public async Task A_walks_room_is_for_the_people_checked_in_and_the_guide_whose_posts_carry_their_role()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var (dateId, guide, checkedIn, reserved) = await WalkAsync(w, DateTime.UtcNow.AddHours(-1));

        // Somebody with a seat who was never checked in, and a stranger, are told nothing (204).
        Assert.IsType<NoContentResult>((await TourRoomFor(w, reserved).Get(dateId, default)).Result);
        Assert.IsType<NoContentResult>((await TourRoomFor(w, ScenarioWorld.Stranger).Get(dateId, default)).Result);
        Assert.IsType<NotFoundResult>((await TourRoomFor(w, reserved).Post(dateId, new("hello?"), default)).Result);

        Assert.IsType<OkObjectResult>((await TourRoomFor(w, guide).Post(dateId, new("Meet at the corner."), default)).Result);
        var room = Assert.IsType<TourRoomRecord>(Assert.IsType<OkObjectResult>(
            (await TourRoomFor(w, checkedIn).Post(dateId, new("Thanks, everyone!"), default)).Result).Value);

        Assert.True(room.CanPost);
        Assert.Null(room.MyRole);
        Assert.Equal(["Thanks, everyone!", "Meet at the corner."], room.Messages.Select(m => m.Body));
        Assert.Null(room.Messages[0].AuthorRole);
        Assert.Equal("Guide", room.Messages[1].AuthorRole);

        // Nothing posted in a walk's room is on the public feed.
        await using var db = await w.Sqlite.NewContextAsync();
        Assert.All(await db.OrgMessages.Where(m => m.OrgCalendarEventId == dateId).ToListAsync(),
                   m => Assert.Equal((OrgMessageChannel.TourRoom, false, (Guid?)null), (m.ChannelType, m.IsPublic, m.OrganizationId)));
    }

    [Fact]
    public async Task A_walks_room_closes_a_week_after_the_walk_and_can_still_be_read()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var (dateId, _, checkedIn, _) = await WalkAsync(w, DateTime.UtcNow.AddDays(-9));

        var closed = Assert.IsType<ConflictObjectResult>((await TourRoomFor(w, checkedIn).Post(dateId, new("Late"), default)).Result);
        Assert.StartsWith("The room closed a week after the walk.", (string)closed.Value!);
        var read = Assert.IsType<TourRoomRecord>(Assert.IsType<OkObjectResult>((await TourRoomFor(w, checkedIn).Get(dateId, default)).Result).Value);
        Assert.False(read.CanPost);
    }

    [Fact]
    public async Task Only_the_author_or_the_people_running_the_walk_take_a_post_down()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var (dateId, guide, checkedIn, _) = await WalkAsync(w, DateTime.UtcNow.AddHours(-1));
        var posted = Assert.IsType<TourRoomRecord>(Assert.IsType<OkObjectResult>(
            (await TourRoomFor(w, guide).Post(dateId, new("From the guide"), default)).Result).Value).Messages[0];

        Assert.IsType<ConflictObjectResult>((await TourRoomFor(w, checkedIn).Remove(dateId, posted.Id, default)).Result);
        var mine = Assert.IsType<TourRoomRecord>(Assert.IsType<OkObjectResult>(
            (await TourRoomFor(w, checkedIn).Post(dateId, new("Mine"), default)).Result).Value).Messages[0];
        Assert.IsType<OkObjectResult>((await TourRoomFor(w, guide).Remove(dateId, mine.Id, default)).Result);
    }

    // ── the evidence page a CMS section draws ────────────────────────────────

    [Fact]
    public async Task An_evidence_section_draws_the_events_accepted_evidence_ranked()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        await using (var db = await w.Sqlite.NewContextAsync())
        {
            foreach (var (name, confirms) in new[] { ("quiet.wav", 0), ("loud.wav", 1) })
            {
                var fileId = Guid.NewGuid();
                db.UploadFiles.Add(new UploadFile
                {
                    Id = fileId, UploadFileTypeId = new Guid("20000000-0000-0000-0000-000000000001"), AppUserId = ScenarioWorld.Came,
                    FileName = name, StoredFileName = name, ContentType = "audio/wav", FileSize = 3, StoragePath = name, IsPublic = true,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
                });
                db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
                {
                    Id = Guid.NewGuid(), OrgCalendarEventId = w.UmbrellaId, SubmittedByAppUserId = ScenarioWorld.Came, UploadFileId = fileId,
                    Status = EvidenceSubmissionStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
                });
                if (confirms > 0)
                    db.EvidenceVotes.Add(new EvidenceVote { Id = Guid.NewGuid(), UploadFileId = fileId, VoterAppUserId = ScenarioWorld.Missed, VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }

        Assert.True(CmsEmbed.IsEmbed(CmsSectionType.EventEvidence));
        await using var read = await w.Sqlite.NewContextAsync();
        var json = await CmsEmbed.ResolveAsync(read, w.OrgId, CmsSectionType.EventEvidence, $$"""{"eventId":"{{w.EventId}}"}""", default);
        var section = System.Text.Json.JsonSerializer.Deserialize<CmsEventSectionRecord>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.False(section.Missing);
        Assert.Equal(["loud.wav", "quiet.wav"], section.Evidence!.Select(e => e.FileName));
        Assert.Equal(1, section.Evidence![0].PositiveVotes);

        // Another group's page can't point at Jim's event.
        var elsewhere = await CmsEmbed.ResolveAsync(read, Guid.NewGuid(), CmsSectionType.EventEvidence, $$"""{"eventId":"{{w.EventId}}"}""", default);
        Assert.Contains("\"missing\":true", elsewhere, StringComparison.OrdinalIgnoreCase);
    }

    // ── what the menus tell guests ───────────────────────────────────────────

    [Fact]
    public void The_menu_credit_carries_the_small_site_mark_as_a_picture_the_pdf_can_draw()
    {
        var mark = Ben.Data.Common.Menus.MenuDesigns.CreditMarkJpeg();
        // JPEG: Telerik draws it without an image decoder; a PNG made every menu PDF throw.
        Assert.Equal([0xFF, 0xD8], mark.Take(2));
        Assert.Equal("/favicon.png", Ben.Data.Common.Menus.MenuDesigns.CreditMarkPath);
        Assert.Contains("ISHAUNTED LLC", Ben.Data.Common.Menus.MenuDesigns.Credit(2026));
    }

    [Fact]
    public void What_the_event_price_covers_is_said_in_one_line_for_the_page_the_letter_and_the_pass()
    {
        var friday = new DateTime(2026, 10, 30);
        EventMenuRecord Menu(string heading, bool throughout, params MenuPricing[] pricings) => new(
            Guid.NewGuid(), Guid.NewGuid(), "planner's title", heading, heading, null, MenuMeal.Dinner, throughout, "classic-bistro",
            null, null, DateTime.UtcNow, [],
            [new EventMenuSectionRecord(Guid.NewGuid(), Guid.NewGuid(), "Dishes", null, "Dishes",
                [.. pricings.Select(p => new EventMenuEntryRecord(Guid.NewGuid(), Guid.NewGuid(), p, p == MenuPricing.PricedSeparately ? 5 : null, null,
                    new MenuLibraryItemRecord(Guid.NewGuid(), MenuItemScope.Event, Guid.NewGuid(), "Jim's", null, null, Guid.NewGuid(), "Dishes",
                        "Pot roast", null, null, MenuDietary.None, MenuAllergen.None, null, false, null)))])],
            throughout ? [] : [new EventMenuMealRecord(Guid.NewGuid(), friday, null, "Dinner", null)],
            "Overnight at The Factory", "The Factory", null);

        Assert.Null(EventMenuPresentation.IncludedWithTheEvent([]));
        Assert.Null(EventMenuPresentation.IncludedWithTheEvent([Menu("Dinner", false, MenuPricing.PricedSeparately)]));
        Assert.Equal("Friday dinner and some of Snack table", EventMenuPresentation.IncludedWithTheEvent([
            Menu("Dinner", false, MenuPricing.IncludedInEvent),
            Menu("Snack table", true, MenuPricing.IncludedInEvent, MenuPricing.PricedSeparately)]));
    }
}
