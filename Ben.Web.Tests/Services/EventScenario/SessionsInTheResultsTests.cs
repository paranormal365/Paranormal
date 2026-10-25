using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.FieldSessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// Sessions from Jim's night in the session player, for anybody (event-scenario-gaps; Ben's scenario: "On the
/// site, they can see the app and all the sessions will run in the session player. Any EVPs will be
/// displayed").
/// </summary>
/// <remarks>
/// Three yeses, each tested on its own: the sender's (they were told only the group would see it), the
/// organizer's, and the event's (public, taking evidence, at a public place). Withdrawing any takes it off.
/// </remarks>
public sealed class SessionsInTheResultsTests
{
    private static async Task<Guid> SessionAsync(ScenarioWorld w)
    {
        await using var db = await w.Sqlite.NewContextAsync();
        var docId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = docId, UploadFileTypeId = new Guid("20000000-0000-0000-0000-000000000001"), AppUserId = ScenarioWorld.Came,
            FileName = "data.json", StoredFileName = "data.json", ContentType = "application/json", StoragePath = "x/data.json",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
        });
        var id = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = id, SubmittedByAppUserId = ScenarioWorld.Came, DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = docId,
            DeviceModel = "iPhone17,2", LocationLabel = "Loading dock", StartedAt = ScenarioWorld.FirstDay.AddHours(22),
            EndedAt = ScenarioWorld.FirstDay.AddHours(23), ReadingCount = 600, MarkerCount = 4, HostedEventId = w.EventId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static FieldSessionPublishController Publisher(ScenarioWorld w, Guid who)
    {
        var storage = new Mock<IFileStorageService>().Object;
        return new FieldSessionPublishController(w.Sqlite.Factory, new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
            NullLogger<FieldSessionPublishController>.Instance, Ben.Web.Tests.Controllers.TestBundles.Store(storage), storage)
        { ControllerContext = ScenarioWorld.SignedInAs(who) };
    }

    private static PublicFieldSessionController Public(ScenarioWorld w)
    {
        var storage = new Mock<IFileStorageService>().Object;
        return new PublicFieldSessionController(w.Sqlite.Factory, storage, Ben.Web.Tests.Controllers.TestBundles.Store(storage))
        { ControllerContext = ScenarioWorld.SignedInAs(null) };
    }

    private static async Task<List<PublicEventSessionRow>> ListedAsync(ScenarioWorld w)
        => [.. Assert.IsAssignableFrom<IEnumerable<PublicEventSessionRow>>(
               Assert.IsType<OkObjectResult>((await Public(w).ForHostedEvent(w.EventId, default)).Result).Value)];

    [Fact]
    public async Task A_session_is_played_by_anybody_only_once_its_sender_and_the_organizer_both_say_yes()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var id = await SessionAsync(w);

        // The organizer can't show it before the sender agrees, and says why.
        var early = Assert.IsType<BadRequestObjectResult>(await Publisher(w, ScenarioWorld.Jim).ShowWithResults(id, default));
        Assert.StartsWith("The person who sent it hasn't agreed", (string)early.Value!);

        // Only the sender can agree; anybody else is told nothing.
        Assert.IsType<NotFoundResult>(await Publisher(w, ScenarioWorld.Stranger).AgreeToShow(id, default));
        Assert.IsType<NoContentResult>(await Publisher(w, ScenarioWorld.Came).AgreeToShow(id, default));

        // Agreed but not shown: still nothing public.
        Assert.Empty(await ListedAsync(w));
        Assert.IsType<NotFoundResult>((await Public(w).Summary(id, default)).Result);

        // Only the event's own people can show it.
        Assert.IsType<NotFoundResult>(await Publisher(w, ScenarioWorld.Stranger).ShowWithResults(id, default));
        Assert.IsType<NoContentResult>(await Publisher(w, ScenarioWorld.Nephew).ShowWithResults(id, default));

        // Shown, but the findings are still the people who were there's until the organizer publishes (10/25/2026).
        Assert.Empty(await ListedAsync(w));
        await w.ChangeEventAsync(e => e.FindingsPublishedUtc = DateTime.UtcNow);

        var row = Assert.Single(await ListedAsync(w));
        Assert.Equal((id, w.PlaceId, 4, true), (row.Id, row.PlaceId, row.MarkerCount, row.MediaReady));
        var summary = Assert.IsType<PublicSessionSummary>(Assert.IsType<OkObjectResult>((await Public(w).Summary(id, default)).Result).Value);
        Assert.Equal((w.PlaceId, "The Factory"), (summary.PlaceId, summary.PlaceName));

        // The sender changes their mind: off at once, with nothing to remember.
        Assert.IsType<NoContentResult>(await Publisher(w, ScenarioWorld.Came).WithdrawShow(id, default));
        Assert.Empty(await ListedAsync(w));
        Assert.IsType<NotFoundResult>((await Public(w).Summary(id, default)).Result);
    }

    [Fact]
    public async Task An_event_that_takes_no_evidence_or_is_somewhere_private_shows_no_sessions()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var id = await SessionAsync(w);
        await w.ChangeEventAsync(e => e.FindingsPublishedUtc = DateTime.UtcNow);
        Assert.IsType<NoContentResult>(await Publisher(w, ScenarioWorld.Came).AgreeToShow(id, default));
        Assert.IsType<NoContentResult>(await Publisher(w, ScenarioWorld.Jim).ShowWithResults(id, default));
        Assert.Single(await ListedAsync(w));

        await w.ChangeEventAsync(e => e.CollectsEvidence = false);
        Assert.Empty(await ListedAsync(w));
        var refused = Assert.IsType<BadRequestObjectResult>(await Publisher(w, ScenarioWorld.Jim).ShowWithResults(id, default));
        Assert.StartsWith("This event isn't taking evidence", (string)refused.Value!);

        await w.ChangeEventAsync(e => e.CollectsEvidence = true);
        await using (var db = await w.Sqlite.NewContextAsync())
        {
            (await db.Places.SingleAsync(p => p.Id == w.PlaceId)).Kind = PlaceKind.PrivateResidence;
            await db.SaveChangesAsync();
        }
        Assert.Empty(await ListedAsync(w));
        Assert.IsType<NotFoundResult>((await Public(w).Summary(id, default)).Result);
    }

    /// <summary>Archiving tidies the organizer's list; the results stay as they were (10/25/2026).</summary>
    [Fact]
    public async Task Archiving_the_event_keeps_its_shown_sessions()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var id = await SessionAsync(w);
        await Publisher(w, ScenarioWorld.Came).AgreeToShow(id, default);
        await Publisher(w, ScenarioWorld.Jim).ShowWithResults(id, default);
        await w.ChangeEventAsync(e => e.FindingsPublishedUtc = DateTime.UtcNow);

        await w.ChangeEventAsync(e => e.LifecycleState = HostedEventLifecycleState.Archived);
        Assert.Single(await ListedAsync(w));
    }

    [Fact]
    public async Task Taking_the_event_off_the_site_takes_its_sessions_with_it()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var id = await SessionAsync(w);
        await Publisher(w, ScenarioWorld.Came).AgreeToShow(id, default);
        await Publisher(w, ScenarioWorld.Jim).ShowWithResults(id, default);
        await w.ChangeEventAsync(e => e.FindingsPublishedUtc = DateTime.UtcNow);
        Assert.Single(await ListedAsync(w));

        await w.ChangeEventAsync(e => e.LifecycleState = HostedEventLifecycleState.Removed);
        Assert.Empty(await ListedAsync(w));
        Assert.IsType<NotFoundResult>((await Public(w).Summary(id, default)).Result);
    }
}
