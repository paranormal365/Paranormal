using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// A guest writing to Jim from the event's page or the app, and Jim answering (event-scenario-gaps; Ben's
/// scenario: "contact Jim either on our app").
/// </summary>
public sealed class WritingToTheOrganizersTests
{
    private static PublicEventGuestThreadController Guest(ScenarioWorld w, Guid who)
        => new(w.Sqlite.Factory, w.Access()) { ControllerContext = ScenarioWorld.SignedInAs(who) };

    private static HostedEventGuestThreadController Organizer(ScenarioWorld w, Guid who)
        => new(w.Sqlite.Factory, w.Access()) { ControllerContext = ScenarioWorld.SignedInAs(who) };

    private static async Task<NotificationSummaryResponse> BellAsync(ScenarioWorld w, Guid who)
    {
        var bell = new NotificationSummaryController(w.Sqlite.Factory, new Mock<IOrganizationSecurityService>().Object)
        { ControllerContext = ScenarioWorld.SignedInAs(who) };
        return Assert.IsType<NotificationSummaryResponse>(Assert.IsType<OkObjectResult>((await bell.GetSummary(default)).Result).Value);
    }

    private static EventGuestThreadRecord Thread(ActionResult<EventGuestThreadRecord> result)
        => Assert.IsType<EventGuestThreadRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task A_guest_writes_Jim_hears_it_answers_and_the_guest_hears_the_answer()
    {
        await using var w = await ScenarioWorld.CreateAsync();

        // Before writing: an empty conversation for the guest, nothing at all for a stranger.
        var empty = Thread(await Guest(w, ScenarioWorld.Came).Get(w.EventId, default));
        Assert.Equal((null, true, "Jim's Investigations"), (empty.ThreadId, empty.CanWrite, empty.OrganizerName));
        Assert.IsType<NoContentResult>((await Guest(w, ScenarioWorld.Stranger).Get(w.EventId, default)).Result);
        Assert.IsType<NotFoundResult>((await Guest(w, ScenarioWorld.Stranger).Post(w.EventId, new("hello?"), default)).Result);

        var sent = Thread(await Guest(w, ScenarioWorld.Came).Post(w.EventId, new("Is there parking at the loading dock?"), default));
        Assert.StartsWith("Sent.", sent.Note);
        Assert.Single(sent.Messages);

        // Jim, who decides the bookings, has it in the group's mail and on his bell; the door helper doesn't.
        var jimBell = await BellAsync(w, ScenarioWorld.Jim);
        Assert.Equal(1, jimBell.OrgMessages.Count);
        Assert.IsType<ForbidResult>((await Organizer(w, ScenarioWorld.Nephew).List(w.OrgId, w.EventId, default)).Result);

        var list = Assert.IsAssignableFrom<IReadOnlyList<EventGuestThreadSummaryRecord>>(
            Assert.IsType<OkObjectResult>((await Organizer(w, ScenarioWorld.Jim).List(w.OrgId, w.EventId, default)).Result).Value);
        var row = Assert.Single(list);
        Assert.Equal((ScenarioWorld.Came, "Came", true, 1), (row.GuestId, row.GuestName, row.WaitingOnUs, row.Count));

        // Opening it marks it read for Jim; answering puts it on the guest's bell, on its own row.
        Thread(await Organizer(w, ScenarioWorld.Jim).Get(w.OrgId, w.EventId, ScenarioWorld.Came, default));
        Assert.Equal(0, (await BellAsync(w, ScenarioWorld.Jim)).OrgMessages.Count);
        var answered = Thread(await Organizer(w, ScenarioWorld.Jim).Answer(
            w.OrgId, w.EventId, ScenarioWorld.Came, new("Yes, around the back."), default));
        Assert.Equal([false, true], answered.Messages.Select(m => m.FromOrganizer));
        Assert.Equal("Jim for Jim's Investigations", answered.Messages[1].AuthorName);

        var cameBell = await BellAsync(w, ScenarioWorld.Came);
        Assert.Equal(1, cameBell.EventOrganizerReplies?.Count);
        Assert.Equal("/o/jims-investigations/events/overnight-at-the-factory#event-organizer-thread", cameBell.EventOrganizerReplies?.Where);
        // Never as the group's mail: the guest has no inbox there to open.
        Assert.Equal(0, cameBell.OrgMessages.Count);
        Assert.Equal(0, (await BellAsync(w, ScenarioWorld.Jim)).OrgMessages.Count);

        // The guest reads it: off their bell, and the next question goes back to Jim's.
        var read = Thread(await Guest(w, ScenarioWorld.Came).Get(w.EventId, default));
        Assert.Equal(["Is there parking at the loading dock?", "Yes, around the back."], read.Messages.Select(m => m.Body));
        Assert.True(read.Messages[0].IsMine);
        Assert.Equal(0, (await BellAsync(w, ScenarioWorld.Came)).EventOrganizerReplies?.Count ?? 0);
        Thread(await Guest(w, ScenarioWorld.Came).Post(w.EventId, new("Thanks!"), default));
        Assert.Equal(1, (await BellAsync(w, ScenarioWorld.Jim)).OrgMessages.Count);

        // One conversation per guest, never public.
        await using var db = await w.Sqlite.NewContextAsync();
        var all = await db.OrgMessages.Where(m => m.HostedEventId == w.EventId).ToListAsync();
        Assert.Single(all, m => m.ParentMessageId == null);
        Assert.All(all, m => Assert.Equal((OrgMessageChannel.EventGuestThread, false), (m.ChannelType, m.IsPublic)));
    }

    [Fact]
    public async Task Anybody_with_a_booking_may_write_and_nobody_once_the_event_is_off_the_site()
    {
        await using var w = await ScenarioWorld.CreateAsync();

        // Still asking, or named on somebody else's booking: both may ask.
        Thread(await Guest(w, ScenarioWorld.Asked).Post(w.EventId, new("Is there room for one more?"), default));
        Thread(await Guest(w, ScenarioWorld.CameGuest).Post(w.EventId, new("Can I bring a camera?"), default));
        Assert.Equal(2, (await BellAsync(w, ScenarioWorld.Jim)).OrgMessages.Count);

        var blank = Assert.IsType<BadRequestObjectResult>((await Guest(w, ScenarioWorld.Came).Post(w.EventId, new("   "), default)).Result);
        Assert.Equal("Write something first.", blank.Value);

        await w.ChangeEventAsync(e => e.LifecycleState = HostedEventLifecycleState.Removed);
        var closed = Assert.IsType<ConflictObjectResult>((await Guest(w, ScenarioWorld.Came).Post(w.EventId, new("Hello?"), default)).Result);
        Assert.StartsWith("This event isn't on the site", (string)closed.Value!);
        var reply = Assert.IsType<ConflictObjectResult>((await Organizer(w, ScenarioWorld.Jim).Answer(
            w.OrgId, w.EventId, ScenarioWorld.Asked, new("Sorry"), default)).Result);
        Assert.StartsWith("This event isn't on the site", (string)reply.Value!);
    }

    [Fact]
    public async Task What_a_guest_writes_is_kept_as_words_and_never_becomes_markup_in_the_groups_mail()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        await using (var db = await w.Sqlite.NewContextAsync())
        {
            (await db.Users.SingleAsync(u => u.Id == ScenarioWorld.Came)).DisplayName = "<img src=x onerror=alert(1)>";
            await db.SaveChangesAsync();
        }

        var sent = Thread(await Guest(w, ScenarioWorld.Came).Post(w.EventId, new("<b>hi</b>"), default));
        Assert.Equal("<b>hi</b>", sent.Messages[0].Body);

        await using var check = await w.Sqlite.NewContextAsync();
        var root = await check.OrgMessages.SingleAsync(m => m.HostedEventId == w.EventId && m.ParentMessageId == null);
        Assert.DoesNotContain("<img", root.Body);
        Assert.Contains("&lt;img", root.Body);
        Assert.Contains($"/organizations/{w.OrgId}/events/{w.EventId}/guest-messages?guest={ScenarioWorld.Came}", root.Body);
    }
}
