using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Website.Library.Manage.Calendar;
using Ben.Web.Website.Library.Shared;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The rules behind the gaps found on 10/16/2026 while writing that day's e2e tests, and the
/// source checks that hold the markup to them.
/// </summary>
/// <remarks>
/// Each was run against the code before its fix and failed: the zone letters taken at today's
/// date, the default start at this minute tomorrow, the ✕ with no question, the guest list that
/// only offered "Mark as coming", unticking that removed nobody, Enter that searched nothing, the
/// unlabeled boxes, the settings index without the venue, the hero that hid its start time, and
/// the investigations list with no phone layout.
/// </remarks>
public class Oct16GapsTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static IBenUserState Central()
    {
        var user = new Mock<IBenUserState>();
        user.SetupGet(u => u.ViewerTimeZone).Returns(Chicago);
        user.SetupGet(u => u.BrowserTimeZone).Returns(Chicago);
        return user.Object;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    // ── Gap 2: the zone line names the visit's offset, not today's ────────────

    [Fact]
    public void A_November_visit_typed_in_October_reads_CST()
    {
        var october = new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal("CST", InvestigationWindow.ViewerLetters(new DateTime(2026, 11, 20, 19, 0, 0), Central(), october));
        Assert.Equal("CDT", InvestigationWindow.ViewerLetters(new DateTime(2026, 10, 20, 19, 0, 0), Central(), october));
    }

    [Fact]
    public void With_nothing_typed_the_letters_are_for_now()
    {
        var january = new DateTime(2027, 1, 5, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal("CST", InvestigationWindow.ViewerLetters(null, Central(), january));
    }

    [Fact]
    public void The_window_reads_its_letters_at_the_typed_start()
    {
        var window = Source("Ben.Web.Website.Library", "Organization", "NewInvestigationWindow.razor");

        Assert.Contains("InvestigationWindow.ViewerLetters(_when, UserState, DateTime.UtcNow)", window);
        Assert.DoesNotContain("Zones.Abbreviation(UserState.ViewerTimeZone, DateTime.UtcNow)", window);
    }

    // ── Gap 15: a new visit starts at 7:00 PM tomorrow ────────────────────────

    [Theory]
    [InlineData(2026, 10, 16, 6, 32, 2026, 10, 17)]
    [InlineData(2026, 10, 16, 23, 59, 2026, 10, 17)]
    [InlineData(2026, 12, 31, 20, 0, 2027, 1, 1)]
    // The evening before the clocks go back: still 7:00 PM on the wall the next day.
    [InlineData(2026, 10, 31, 21, 15, 2026, 11, 1)]
    public void The_default_start_is_seven_tomorrow_evening_on_the_readers_clock(
        int y, int mo, int d, int h, int mi, int ey, int emo, int ed)
    {
        var start = InvestigationWindow.DefaultStart(new DateTime(y, mo, d, h, mi, 17));

        Assert.Equal(new DateTime(ey, emo, ed, 19, 0, 0), start);
        Assert.Equal(DateTimeKind.Unspecified, start.Kind);
    }

    [Fact]
    public void The_schedule_window_uses_the_usual_start()
    {
        var window = Source("Ben.Web.Website.Library", "Organization", "NewInvestigationWindow.razor");

        Assert.Contains("InvestigationWindow.DefaultStart(UserState.NowInViewerTimeZone())", window);
        Assert.DoesNotContain("TomorrowToTheMinute", window);
    }

    // ── Gap 5: the team ✕ asks first, and says what goes ─────────────────────

    [Fact]
    public void Taking_somebody_off_says_what_goes_with_them()
    {
        Assert.Equal("Take James off the team? Their duty (Lead) goes too.",
            InvestigationRoster.RemovalQuestion("James", ["Lead"], isLead: false));
        Assert.Equal("Take James off the team? Their duties (Lead, Audio) go too.",
            InvestigationRoster.RemovalQuestion("James", ["Lead", "Audio"], isLead: false));
        Assert.Equal("Take James off the team?",
            InvestigationRoster.RemovalQuestion("James", [], isLead: false));
        Assert.Equal("Take this person off the team? They lead this visit, so nobody will lead it until you choose someone.",
            InvestigationRoster.RemovalQuestion(null, [], isLead: true));
    }

    [Fact]
    public void The_cross_asks_rather_than_removes()
    {
        var roster = Source("Ben.Web.Website.Library", "Shared", "InvestigationRoster.razor");

        Assert.Contains("@onclick=\"@(() => AskToRemove(entry))\"", roster);
        Assert.DoesNotContain("@onclick=\"@(() => RemoveFromTeamAsync(entry))\"", roster);
        Assert.DoesNotContain("TelerikDialog", roster);
    }

    // ── Gap 6: check-in and Attended show the server's reason ────────────────

    [Fact]
    public void Check_in_and_attended_show_the_servers_sentence()
    {
        var roster = Source("Ben.Web.Website.Library", "Shared", "InvestigationRoster.razor");

        Assert.DoesNotContain("Check the arrival time is not in the future.", roster);
        Assert.Contains("var (result, error) = await AdminClient.CheckInToInvestigationAsync", roster);
        Assert.Contains("var (result, error) = await AdminClient.OverrideInvestigationAttendanceAsync", roster);
    }

    // ── Gaps 3 and 4: the guest list ─────────────────────────────────────────

    [Theory]
    [InlineData(RsvpStatus.Accepted, RsvpStatus.Declined, RsvpStatus.Invited)]
    [InlineData(RsvpStatus.Declined, RsvpStatus.Accepted, RsvpStatus.Invited)]
    [InlineData(RsvpStatus.Tentative, RsvpStatus.Accepted, RsvpStatus.Declined)]
    [InlineData(RsvpStatus.Invited, RsvpStatus.Accepted, RsvpStatus.Declined)]
    public void Each_answer_is_offered_the_other_sensible_answers(RsvpStatus now, RsvpStatus first, RsvpStatus second)
    {
        var offered = OrgScheduler.AnswersFor(now);

        Assert.Equal(new[] { first, second }, offered);
        Assert.DoesNotContain(now, offered);
    }

    [Fact]
    public void Not_coming_is_said_in_words()
    {
        Assert.Equal("Mark as not coming", OrgScheduler.MarkAs(RsvpStatus.Declined));
        Assert.Equal("Mark as coming", OrgScheduler.MarkAs(RsvpStatus.Accepted));
        Assert.Equal("Mark as not answered", OrgScheduler.MarkAs(RsvpStatus.Invited));
    }

    private static OrgCalendarEventAttendeeRecord Guest(Guid who) => new() { Id = Guid.NewGuid(), AppUserId = who };

    [Fact]
    public void Unticking_a_member_takes_them_off_on_save()
    {
        var kept = Guid.NewGuid();
        var unticked = Guid.NewGuid();
        var byEmail = Guid.NewGuid();
        var list = new[] { Guest(kept), Guest(unticked), Guest(byEmail) };

        var off = OrgScheduler.UntickedInvitees(list, [kept, unticked], new HashSet<Guid> { kept, byEmail },
            isPublic: false, isTourDate: false);

        // Only the unticked member: a guest invited by email has no box, so is never "unticked".
        Assert.Equal(unticked, Assert.Single(off).AppUserId);
    }

    [Fact]
    public void Nobody_is_taken_off_a_public_event_or_a_tour_date_by_the_boxes()
    {
        var member = Guid.NewGuid();
        var list = new[] { Guest(member) };

        Assert.Empty(OrgScheduler.UntickedInvitees(list, [member], new HashSet<Guid>(), isPublic: true, isTourDate: false));
        Assert.Empty(OrgScheduler.UntickedInvitees(list, [member], new HashSet<Guid>(), isPublic: false, isTourDate: true));
    }

    [Fact]
    public void Save_takes_off_the_unticked_and_Whos_coming_has_Remove()
    {
        var scheduler = Source("Ben.Web.Website.Library", "Manage", "Calendar", "OrgScheduler.razor");

        Assert.Contains("UntickedInvitees(attendees.Items", scheduler);
        Assert.Contains("RemoveCalendarAttendeeAsync(OrgId, eventId, unticked.Id)", scheduler);
        Assert.Contains("RemoveCalendarAttendeeAsync(OrgId, _editingEvent.Id, guest.Id)", scheduler);
        Assert.Contains("guest-remove-yes-", scheduler);
        Assert.Contains("_guestBusy == g.Id && _guestBusyAnswer == a ? \"Saving…\"", scheduler);
    }

    // ── Gaps 8 and 9: the place and venue boxes ──────────────────────────────

    [Theory]
    [InlineData("Enter", "Bell", true)]
    [InlineData("NumpadEnter", "Bell", true)]
    [InlineData("Enter", "  ", false)]
    [InlineData("a", "Bell", false)]
    [InlineData(null, "Bell", false)]
    public void Enter_runs_a_search_when_something_is_typed(string? key, string text, bool searches)
        => Assert.Equal(searches, PlaceSearch.IsSearchKey(key, text));

    public static TheoryData<string[], string> SearchBoxes => new()
    {
        { ["Ben.Web.Website.Library", "Manage", "Calendar", "OrgScheduler.razor"], "calendar-place-search" },
        { ["Ben.Web.Website.Library", "Manage", "Events", "OrgEvents.razor"], "new-event-place" },
        { ["Ben.Web.Website.Library", "Manage", "Events", "OrgEventPage.razor"], "venue-search" },
    };

    [Theory]
    [MemberData(nameof(SearchBoxes))]
    public void Each_search_box_is_labeled_searches_on_Enter_and_says_the_same_thing(string[] path, string id)
    {
        var text = Source(path);

        Assert.Contains($"for=\"{id}\"", text);
        // BenTextBox renders an <input>; it is the plain box that keeps the cursor put while typing.
        var box = Regex.Match(text, $"<(input|BenTextBox) id=\"{id}\".*?/>", RegexOptions.Singleline);
        Assert.True(box.Success, $"no input with id {id}");
        Assert.Contains("@onkeydown=", box.Value);
        Assert.Contains("PlaceSearch.IsSearchKey(e.Key", box.Value);
        Assert.Contains("placeholder=\"@PlaceSearch.Placeholder\"", box.Value);
    }

    [Fact]
    public void The_ordinary_events_coming_along_boxes_are_labeled()
    {
        var page = Source("Ben.Web.Website.Library", "Organization", "Public", "PublicEventDetail.razor");

        Assert.Contains("for=\"attend-email\"", page);
        Assert.Matches("<(input|BenTextBox) id=\"attend-email\"", page);
        Assert.Contains("for=\"attend-name\"", page);
        Assert.Matches("<(input|BenTextBox) id=\"attend-name\"", page);
    }

    // ── Gap 7: the case page's attendee table ────────────────────────────────

    [Fact]
    public void The_case_pages_cross_says_what_it_does_and_add_member_is_tied_to_its_select()
    {
        var panel = Source("Ben.Web.Website.Library", "Organization", "Cases", "InvestigationPanel.razor");

        Assert.Contains("aria-label=\"@($\"Take {a.DisplayName ?? \"this person\"} off the team\")\"", panel);
        Assert.Contains("for=\"@($\"investigationpanel-add-member-{i.Id}\")\"", panel);
        Assert.Contains("<BenSelect Id=\"@($\"investigationpanel-add-member-{i.Id}\")\"", panel);
    }

    [Fact]
    public void The_case_pages_delete_asks_first_inline()
    {
        var panel = Source("Ben.Web.Website.Library", "Organization", "Cases", "InvestigationPanel.razor");

        // The footer's Delete only asks; the question's own Delete is what deletes.
        Assert.DoesNotContain("@onclick=\"DeleteInvestigation\" disabled=\"@(!(!_isBusy))\"><BenIcon Name=\"trash-2\"", panel);
        Assert.Contains("_confirmDelete = true;", panel);
        Assert.Contains("Delete this visit? This can't be undone.", panel);
        Assert.Contains("id=\"investigation-dialog-confirm-yes\"", panel);
        Assert.Contains("id=\"investigation-dialog-confirm-no\"", panel);
        Assert.DoesNotContain("<TelerikDialog", panel);
    }

    // ── Gap 11: no unreachable residence warnings ────────────────────────────

    [Theory]
    [InlineData("OrgEvents.razor")]
    [InlineData("OrgEventPage.razor")]
    public void The_venue_boxes_no_longer_warn_about_a_residence_they_can_never_show(string file)
        => Assert.DoesNotContain("That is a private residence", Source("Ben.Web.Website.Library", "Manage", "Events", file));

    // ── Gap 12: the settings index names every section ───────────────────────

    [Fact]
    public void Every_settings_section_has_a_link_in_the_index()
    {
        var settings = Source("Ben.Web.Website.Library", "Organization", "OrgSettingsManager.razor");

        var sections = Regex.Matches(settings, "id=\"(settings-[a-z-]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        var linked = Regex.Matches(settings, "JumpLink\\.To\\(Nav, \"(settings-[a-z-]+)\"\\)").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Contains("settings-venue", sections);
        Assert.DoesNotContain(sections, s => !linked.Contains(s));
    }

    // ── Gap 13: the investigations list on a phone ───────────────────────────

    [Fact]
    public void Every_cell_of_the_investigations_list_names_its_column_for_the_phone_cards()
    {
        var list = Source("Ben.Web.Website.Library", "Organization", "OrgInvestigations.razor");
        var grid = list[list.IndexOf("private RenderFragment Grid", StringComparison.Ordinal)..];
        var rowStart = grid.IndexOf("<tr id=\"inv-row-", StringComparison.Ordinal);
        var row = grid[rowStart..grid.IndexOf("</tr>", rowStart, StringComparison.Ordinal)];

        var cells = Regex.Matches(row, "<td\\b[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(6, cells.Count);
        Assert.All(cells, c => Assert.Contains("data-label=", c));
        Assert.Contains("<div class=\"inv-team-panel\">", list);

        var css = Source("Ben.Web.Website.Library", "Organization", "OrgInvestigations.razor.css");
        Assert.Contains("container-type: inline-size", css);
        Assert.Contains("max-width: calc(100cqi", css);
        Assert.Contains("@media (max-width: 991.98px)", css);
    }

    [Fact]
    public void A_visit_with_no_case_has_an_Edit()
    {
        var list = Source("Ben.Web.Website.Library", "Organization", "OrgInvestigations.razor");

        Assert.DoesNotContain("row.CanEditRecord && row.CaseId is not null", list);
        Assert.Contains("EditInvestigationId=\"@editingId\"", list);
    }

    [Fact]
    public void The_roster_table_stacks_on_a_phone()
    {
        var roster = Source("Ben.Web.Website.Library", "Shared", "InvestigationRoster.razor");
        var table = roster[roster.IndexOf("roster-manage\">", StringComparison.Ordinal)..roster.IndexOf("</table>", StringComparison.Ordinal)];
        var cells = Regex.Matches(table, "<td\\b[^>]*>").Select(m => m.Value).ToList();

        Assert.Equal(5, cells.Count);
        Assert.All(cells, c => Assert.Contains("data-label=", c));
    }

    // ── Gap 16: the hero's tiles keep their sub-line on a phone ──────────────

    [Fact]
    public void No_width_hides_a_facts_sub_line()
    {
        var css = Source("Ben.Web.Website.Library", "Shared", "PageHero.razor.css");

        Assert.DoesNotMatch(new Regex(@"\.page-hero__fact dd span\s*\{[^}]*display:\s*none", RegexOptions.Singleline), css);
        Assert.DoesNotContain("minmax(4.5rem, 1fr)", css);
        Assert.DoesNotContain("overflow-wrap: anywhere", Source("Ben.Web.Website.Library", "Kit", "BenFactRail.razor.css"));
    }
}
