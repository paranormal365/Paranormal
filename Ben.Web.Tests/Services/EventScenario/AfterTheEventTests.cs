using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// After Jim's night (event-scenario-gaps): the thank-you to who came, "we missed you" to who didn't,
/// the evidence window both letters name, the deadline the door keeps, and the results letter.
/// </summary>
/// <remarks>
/// Ben's scenario, 10/21/2026: <i>"Jim had the site send out thank you messages to those who came and we
/// missed you letters to those who were unable to attend. The letter told them the event evidence would be
/// available in about a week and he was accepting evidence for three days."</i>
/// </remarks>
public sealed class AfterTheEventTests
{
    // ── the window, in words ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "one day")]
    [InlineData(3, "three days")]
    [InlineData(14, "fourteen days")]
    [InlineData(21, "21 days")]
    public void Days_are_said_in_words(int days, string said)
        => Assert.Equal(said, HostedEventEvidence.DaysInWords(days));

    [Theory]
    [InlineData(7, "about a week")]
    [InlineData(14, "about two weeks")]
    [InlineData(30, "about a month")]
    [InlineData(5, "about five days")]
    public void The_results_are_promised_in_words(int days, string said)
        => Assert.Equal(said, HostedEventEvidence.AboutInWords(days));

    [Fact]
    public void The_window_names_three_days_the_deadline_and_about_a_week()
    {
        var ev = Event(evidenceDays: 3, resultsInDays: 7);

        var sentence = HostedEventEvidence.WindowSentence(ev, []);

        Assert.NotNull(sentence);
        Assert.Contains("We're accepting evidence for three days, until ", sentence);
        Assert.Contains("ready to see in about a week", sentence);
        Assert.Equal(HostedEventCalendarSync.Window(ev, []).EndUtc.AddDays(3), HostedEventEvidence.DeadlineUtc(ev, []));
    }

    [Fact]
    public void An_event_that_takes_no_evidence_says_nothing_about_it()
    {
        var ev = Event(evidenceDays: 3, resultsInDays: 7);
        ev.CollectsEvidence = false;
        Assert.Null(HostedEventEvidence.WindowSentence(ev, []));
        Assert.Null(HostedEventEvidence.WindowSentence(Event(null, null), []));
    }

    [Fact]
    public void The_door_shuts_after_the_window_and_says_when()
    {
        var ev = Event(evidenceDays: 3, resultsInDays: null);
        var deadline = HostedEventEvidence.DeadlineUtc(ev, [])!.Value;

        Assert.Null(HostedEventEvidence.WhyClosed(ev, [], deadline.AddMinutes(-1)));
        Assert.StartsWith("This event stopped taking evidence on ", HostedEventEvidence.WhyClosed(ev, [], deadline.AddMinutes(1)));
        // No deadline set: open for as long as the event takes evidence.
        Assert.Null(HostedEventEvidence.WhyClosed(Event(null, null), [], deadline.AddYears(1)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("/o/jims-investigations/p/results", null)]
    [InlineData("https://jims.example.com/results", null)]
    [InlineData("http://jims.example.com/results", "Give a page on this site")]
    [InlineData("//evil.example/x", "Give a page on this site")]
    [InlineData("javascript:alert(1)", "Give a page on this site")]
    public void Only_a_page_here_or_an_https_address_goes_in_a_letter_to_every_guest(string? url, string? refusal)
    {
        var why = HostedEventEvidence.WhyNotAResultsUrl(url);
        if (refusal is null) Assert.Null(why);
        else Assert.StartsWith(refusal, why);
    }

    [Fact]
    public void Evidence_is_ranked_by_positive_votes_then_by_when_it_was_caught()
    {
        var t = new DateTime(2026, 10, 31, 2, 0, 0, DateTimeKind.Utc);
        var ranked = HostedEventEvidence.Rank(
            [("late, no votes", 0, t.AddHours(2)), ("early, no votes", 0, t), ("two votes", 2, t.AddHours(3)), ("five votes", 5, t.AddHours(4))],
            x => x.Item2, x => x.Item3).Select(x => x.Item1);

        Assert.Equal(["five votes", "two votes", "early, no votes", "late, no votes"], ranked);
    }

    private static HostedEvent Event(int? evidenceDays, int? resultsInDays) => new()
    {
        Name = "Overnight at The Factory", TimeZoneId = "America/Chicago", CollectsEvidence = true,
        StartsOn = new DateTime(2026, 10, 30), EndsOn = new DateTime(2026, 10, 31),
        EvidenceDays = evidenceDays, ResultsInDays = resultsInDays,
    };

    // ── the letters the morning after ────────────────────────────────────────

    private static HostedEventThankYouJob Job(ScenarioWorld w, ScenarioWorld.Letters letters)
        => new(w.Sqlite.Factory, w.Mailer(letters), NullLogger<HostedEventThankYouJob>.Instance);

    /// <summary>
    /// The door checked one party in and never saw the other: one is thanked, the other is missed —
    /// and both are told the evidence window.
    /// </summary>
    [Fact]
    public async Task Who_came_is_thanked_and_who_did_not_is_missed_and_both_hear_the_window()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        await w.ChangeEventAsync(e => { e.EvidenceDays = 3; e.ResultsInDays = 7; e.MissedYouNote = "Next time!"; });
        var letters = new ScenarioWorld.Letters();

        await Job(w, letters).RunAtAsync(ScenarioWorld.EndsUtc.AddHours(13), default);

        var thanks = Assert.Single(letters.Sent, l => l.To == "came@example.test");
        Assert.StartsWith("Thank you for coming", thanks.Subject);
        Assert.Contains("accepting evidence for three days", thanks.HtmlBody);
        Assert.Contains("about a week", thanks.HtmlBody);
        Assert.Contains("#event-evidence", thanks.HtmlBody);

        var missed = Assert.Single(letters.Sent, l => l.To == "missed@example.test");
        Assert.Equal("We missed you at Overnight at The Factory", missed.Subject);
        Assert.Contains("Next time!", missed.HtmlBody);
        Assert.Contains("accepting evidence for three days", missed.HtmlBody);
        Assert.Equal(Ben.Data.Common.Mail.MailKinds.EventMissedYou.Key, missed.Kind);

        // Somebody still asking had no place, and hears nothing.
        Assert.DoesNotContain(letters.Sent, l => l.To == "asked@example.test");
        Assert.Equal(2, letters.Sent.Count);
    }

    /// <summary>A door that recorded nobody cannot tell who came, so everybody with a place is thanked.</summary>
    [Fact]
    public async Task With_no_check_ins_at_all_everybody_with_a_place_is_thanked()
    {
        await using var w = await ScenarioWorld.CreateAsync(checkedIn: false);
        var letters = new ScenarioWorld.Letters();

        await Job(w, letters).RunAtAsync(ScenarioWorld.EndsUtc.AddHours(13), default);

        Assert.Equal(["came@example.test", "missed@example.test"], letters.Sent.Select(l => l.To).Order());
        Assert.All(letters.Sent, l => Assert.StartsWith("Thank you for coming", l.Subject));
    }

    [Fact]
    public async Task Turning_off_the_missed_you_letter_leaves_the_no_show_alone_and_still_thanks_who_came()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        await w.ChangeEventAsync(e => e.SendMissedYou = false);
        var letters = new ScenarioWorld.Letters();

        await Job(w, letters).RunAtAsync(ScenarioWorld.EndsUtc.AddHours(13), default);
        await Job(w, letters).RunAtAsync(ScenarioWorld.EndsUtc.AddHours(20), default);

        Assert.Equal("came@example.test", Assert.Single(letters.Sent).To);
        await using var db = await w.Sqlite.NewContextAsync();
        Assert.NotNull((await db.HostedEvents.SingleAsync(e => e.Id == w.EventId)).ThankYouSentUtc);
    }

    [Fact]
    public async Task Only_the_missed_you_letter_can_go_when_the_thank_you_is_off()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        await w.ChangeEventAsync(e => e.SendThankYou = false);
        var letters = new ScenarioWorld.Letters();

        await Job(w, letters).RunAtAsync(ScenarioWorld.EndsUtc.AddHours(13), default);

        Assert.Equal("missed@example.test", Assert.Single(letters.Sent).To);
    }

    // ── offering evidence ────────────────────────────────────────────────────

    private static EventEvidenceController Evidence(ScenarioWorld w, Guid who)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(x => x.OrgFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((o, n) => Path.Combine(Path.GetTempPath(), "scenario-evidence", o.ToString(), n));
        return new EventEvidenceController(
            w.Sqlite.Factory, storage.Object, new Ben.Data.WebApi.Services.PlatformMessageService(w.Sqlite.Factory),
            TestMedia.Ingest(), TestMedia.Stripper(), w.Security().Object,
            new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
            new Ben.Data.WebApi.Services.Media.MediaRetentionPolicy(new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(w.Sqlite.Factory)))
        { ControllerContext = ScenarioWorld.SignedInAs(who) };
    }

    private static IFormFile Whisper() =>
        new FormFile(new MemoryStream("evp"u8.ToArray()), 0, 3, "file", "whisper.wav")
        { Headers = new HeaderDictionary(), ContentType = "audio/wav" };

    /// <summary>
    /// A guest who booked the ordinary way, signed in, may offer what they caught.
    /// </summary>
    /// <remarks>
    /// The door asked only for an emailed invitation or membership of the group, so Jim's guests — who
    /// booked through the event's page — were told "Evidence can be offered by people who attended this
    /// event". The guest a booking named counts too.
    /// </remarks>
    [Fact]
    public async Task A_confirmed_guest_and_the_guest_they_named_may_offer_evidence_and_a_stranger_may_not()
    {
        await using var w = await ScenarioWorld.CreateAsync();

        Assert.IsType<OkObjectResult>((await Evidence(w, ScenarioWorld.Came).Submit(w.UmbrellaId, "stairs", Whisper(), default)).Result);
        Assert.IsType<OkObjectResult>((await Evidence(w, ScenarioWorld.CameGuest).Submit(w.UmbrellaId, null, Whisper(), default)).Result);

        var stranger = Assert.IsType<BadRequestObjectResult>((await Evidence(w, ScenarioWorld.Stranger).Submit(w.UmbrellaId, null, Whisper(), default)).Result);
        Assert.Equal("Evidence can be offered by people who attended this event.", stranger.Value);
        // Still asking is not a place.
        Assert.IsType<BadRequestObjectResult>((await Evidence(w, ScenarioWorld.Asked).Submit(w.UmbrellaId, null, Whisper(), default)).Result);
    }

    [Fact]
    public async Task After_the_window_the_door_refuses_in_words()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        // The last night ended three days ago; one day of evidence is long gone.
        await w.ChangeEventAsync(e => e.EvidenceDays = 1);

        var refused = Assert.IsType<BadRequestObjectResult>((await Evidence(w, ScenarioWorld.Came).Submit(w.UmbrellaId, null, Whisper(), default)).Result);
        Assert.StartsWith("This event stopped taking evidence on ", (string)refused.Value!);

        await w.ChangeEventAsync(e => e.EvidenceDays = 30);
        Assert.IsType<OkObjectResult>((await Evidence(w, ScenarioWorld.Came).Submit(w.UmbrellaId, null, Whisper(), default)).Result);
    }

    /// <summary>The public list: most positive votes first, then the earliest caught.</summary>
    [Fact]
    public async Task The_accepted_evidence_is_served_most_confirmed_first_then_earliest_caught()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var first = await AcceptedAsync(w, "first.wav", caught: ScenarioWorld.FirstDay.AddHours(21), confirms: 0);
        var second = await AcceptedAsync(w, "second.wav", caught: ScenarioWorld.FirstDay.AddHours(22), confirms: 2);
        var third = await AcceptedAsync(w, "third.wav", caught: ScenarioWorld.FirstDay.AddHours(20), confirms: 0);

        var served = Assert.IsAssignableFrom<IEnumerable<EventEvidenceController.EvidenceSubmissionRecord>>(
            Assert.IsType<OkObjectResult>((await Evidence(w, ScenarioWorld.Stranger).Accepted(w.UmbrellaId, default)).Result).Value).ToList();

        Assert.Equal(["second.wav", "third.wav", "first.wav"], served.Select(s => s.FileName));
        Assert.Equal(2, served[0].PositiveVotes);
        Assert.Equal(ScenarioWorld.FirstDay.AddHours(20), served[1].CaughtAtUtc);
        _ = (first, second, third);
    }

    private static async Task<Guid> AcceptedAsync(ScenarioWorld w, string name, DateTime caught, int confirms)
    {
        await using var db = await w.Sqlite.NewContextAsync();
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = new Guid("20000000-0000-0000-0000-000000000001"), AppUserId = ScenarioWorld.Came,
            FileName = name, StoredFileName = name, ContentType = "audio/wav", FileSize = 3, StoragePath = name, IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
        });
        db.UploadFileMetadata.Add(new UploadFileMetadata { Id = Guid.NewGuid(), UploadFileId = fileId, MediaKind = "Audio", CapturedAtUtc = caught, ExtractedAtUtc = DateTime.UtcNow });
        var id = Guid.NewGuid();
        db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
        {
            Id = id, OrgCalendarEventId = w.UmbrellaId, SubmittedByAppUserId = ScenarioWorld.Came, UploadFileId = fileId,
            Status = EvidenceSubmissionStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ScenarioWorld.Came,
        });
        foreach (var voter in new[] { ScenarioWorld.Missed, ScenarioWorld.Stranger }.Take(confirms))
            db.EvidenceVotes.Add(new EvidenceVote
            {
                Id = Guid.NewGuid(), UploadFileId = fileId, VoterAppUserId = voter, VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow,
            });
        // A dispute never ranks anything up.
        db.EvidenceVotes.Add(new EvidenceVote
        {
            Id = Guid.NewGuid(), UploadFileId = fileId, VoterAppUserId = ScenarioWorld.Jim, VoteType = EvidenceVoteType.Disputes, DateVoted = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ── the results letter ───────────────────────────────────────────────────

    private static HostedEventResultsController Results(ScenarioWorld w, ScenarioWorld.Letters letters, Guid who)
        => new(w.Sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, w.Security().Object, w.Access(),
               w.Mailer(letters), ScenarioWorld.Site, NullLogger<HostedEventResultsController>.Instance)
        { ControllerContext = ScenarioWorld.SignedInAs(who) };

    /// <summary>
    /// Everybody with a place gets the link and the QR code, once; the person who missed the night too.
    /// </summary>
    [Fact]
    public async Task The_results_letter_reaches_everybody_with_a_place_once_with_the_link_and_a_qr_code()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        await w.ChangeEventAsync(e => e.ResultsUrl = "/o/jims-investigations/p/factory-results");
        var letters = new ScenarioWorld.Letters();

        var sent = Assert.IsType<HostedEventResultsSentRecord>(Assert.IsType<OkObjectResult>(
            (await Results(w, letters, ScenarioWorld.Jim).SendResults(w.OrgId, w.EventId, new SendHostedEventResultsRequest("Thank you all."), default)).Result).Value);

        Assert.Equal(2, sent.Sent);
        Assert.Equal("https://example.test/o/jims-investigations/p/factory-results", sent.ResultsUrl);
        Assert.Equal(["came@example.test", "missed@example.test"], letters.Sent.Select(l => l.To).Order());
        var letter = letters.Sent[0];
        Assert.Contains("https://example.test/o/jims-investigations/p/factory-results", letter.HtmlBody);
        Assert.Contains("data:image/png;base64,", letter.HtmlBody);
        Assert.Contains($"https://api.example.test/api/public/hosted-events/{w.EventId}/results-qr.png", letter.HtmlBody);
        Assert.Contains("Thank you all.", letter.HtmlBody);
        Assert.Equal(2, sent.After.GuestsSentResults);

        // Pressing it again writes to nobody twice.
        var again = Assert.IsType<HostedEventResultsSentRecord>(Assert.IsType<OkObjectResult>(
            (await Results(w, letters, ScenarioWorld.Jim).SendResults(w.OrgId, w.EventId, new SendHostedEventResultsRequest(null), default)).Result).Value);
        Assert.Equal((0, 2), (again.Sent, again.AlreadySent));
        Assert.Equal(2, letters.Sent.Count);
    }

    [Fact]
    public async Task The_results_letter_waits_for_the_event_to_end_and_only_the_organizer_sends_it()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Live);
        var letters = new ScenarioWorld.Letters();

        var early = Assert.IsType<BadRequestObjectResult>(
            (await Results(w, letters, ScenarioWorld.Jim).SendResults(w.OrgId, w.EventId, new SendHostedEventResultsRequest(null), default)).Result);
        Assert.Equal("The results letter goes once the event is over.", early.Value);

        Assert.IsType<ForbidResult>(
            (await Results(w, letters, ScenarioWorld.Nephew).SendResults(w.OrgId, w.EventId, new SendHostedEventResultsRequest(null), default)).Result);
        Assert.Empty(letters.Sent);
    }

    [Fact]
    public async Task The_qr_code_opens_the_event_page_when_no_results_page_was_given()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var results = Assert.IsType<PublicHostedEventResultsRecord>(Assert.IsType<OkObjectResult>(
            (await Results(w, new(), ScenarioWorld.Stranger).Results(w.EventId, default)).Result).Value);

        Assert.Equal("https://example.test/o/jims-investigations/events/overnight-at-the-factory#event-evidence", results.ResultsUrl);
        Assert.IsType<FileContentResult>(await Results(w, new(), ScenarioWorld.Stranger).ResultsQr(w.EventId, null, default));
    }

    // ── the organizer's settings ─────────────────────────────────────────────

    private static HostedEventAfterController After(ScenarioWorld w, Guid who)
        => new(w.Sqlite.Factory, new Mock<AutoMapper.IMapper>().Object, w.Security().Object, w.Access(), new HostedEventCalendarSync(),
               new Mock<Ben.Data.WebApi.Services.IMediaIngestService>().Object, new Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
               NullLogger<HostedEventAfterController>.Instance)
        { ControllerContext = ScenarioWorld.SignedInAs(who) };

    [Fact]
    public async Task The_window_is_saved_and_refused_in_words_never_clamped()
    {
        await using var w = await ScenarioWorld.CreateAsync();
        var after = After(w, ScenarioWorld.Jim);

        var tooLong = Assert.IsType<BadRequestObjectResult>((await after.SetAfter(w.OrgId, w.EventId,
            new SetHostedEventAfterRequest(true, true, null, EvidenceDays: 90, SetsEvidence: true), default)).Result);
        Assert.StartsWith("Evidence can be taken for 1 to 60 days", (string)tooLong.Value!);

        var saved = Assert.IsType<HostedEventAfterRecord>(Assert.IsType<OkObjectResult>((await after.SetAfter(w.OrgId, w.EventId,
            new SetHostedEventAfterRequest(true, true, null, EvidenceDays: 3, ResultsInDays: 7, SendMissedYou: true,
                MissedYouNote: "  We missed you!  ", AsksForVotes: true, ResultsUrl: "/o/jims-investigations/p/results", SetsEvidence: true), default)).Result).Value);
        Assert.Equal((3, 7, "We missed you!", true), (saved.EvidenceDays, saved.ResultsInDays, saved.MissedYouNote, saved.AsksForVotes));
        Assert.Equal(1, saved.GuestsMissed);
        Assert.NotNull(saved.EvidenceDeadlineUtc);

        // An older caller that sends only the first three fields changes none of these.
        var older = Assert.IsType<HostedEventAfterRecord>(Assert.IsType<OkObjectResult>((await after.SetAfter(w.OrgId, w.EventId,
            new SetHostedEventAfterRequest(true, true, "Thanks"), default)).Result).Value);
        Assert.Equal(3, older.EvidenceDays);
    }
}
