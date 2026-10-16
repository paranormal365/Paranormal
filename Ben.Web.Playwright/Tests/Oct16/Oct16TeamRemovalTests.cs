using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The Team panel on Group → Investigations after fix/oct16-gaps (10/16/2026): the ✕ asks first
/// and names the duties that go with the person (gap 5), check-in shows the server's own reason
/// (gap 6), and the list and its Team panel fit every screen (gap 13).
/// </summary>
/// <remarks>
/// <para><b>Before.</b> The ✕ took somebody off in one click and their duties went with them
/// unsaid; a refused check-in always said "Check the arrival time is not in the future.", whatever
/// the reason; and at 375px the list scrolled sideways with half the Team panel off the screen.</para>
///
/// <para>Each test gets its own visit with no case at Bell Witch Cave, with James (a plain Member)
/// on its team, made by Sarah (an administrator, not the SuperAdmin owner), and deleted afterwards.
/// Attendance is unmarked first, because the server refuses to delete a visit somebody was at.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("InvestigationTeam")]
[NonParallelizable]
public class Oct16TeamRemovalTests : Oct16TestBase
{
    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private string _visitId = "";
    private string _jamesId = "";
    private string _jamesName = "";
    private string _jamesAttendeeId = "";

    [SetUp]
    public async Task AVisitWithJamesOnTheTeam()
    {
        _signedIn = false;   // each test has a fresh browser context
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        _jamesId = await MyUserIdAsync(await ApiAsAsync(MemberEmail, MemberPassword));

        var members = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/roster"), "reading the group's members");
        _jamesName = members.EnumerateArray()
            .First(m => m.GetProperty("appUserId").GetString() == _jamesId)
            .GetProperty("displayName").GetString() ?? "James";

        var start = DateTime.UtcNow.Date.AddDays(20).AddHours(1);
        _visitId = await CaselessVisitAsync(_sarah, _orgId, $"Oct16 team ✕ {Guid.NewGuid():N}"[..21], start, start.AddHours(4));
        _jamesAttendeeId = await PutOnTheTeamAsync(_jamesId);
    }

    [TearDown]
    public async Task DeleteTheVisit()
    {
        if (_visitId.Length > 0)
            try { await DeleteVisitAsync(_sarah, _orgId, _visitId); } catch { /* reported by the test */ }
        _visitId = "";
    }

    private string Api => $"/api/organizations/{_orgId}/investigations/{_visitId}";

    private async Task<string> PutOnTheTeamAsync(string userId)
    {
        var roster = await OkJsonAsync(await _sarah.PostAsync($"{Api}/attendees", new() { DataObject = new { appUserId = userId } }),
            "putting somebody on the team");
        return roster.EnumerateArray().First(r => r.GetProperty("appUserId").GetString() == userId)
            .GetProperty("attendeeId").GetString()!;
    }

    private async Task<List<JsonElement>> RosterAsync()
        => (await OkJsonAsync(await _sarah.GetAsync($"{Api}/roster"), "reading the team")).EnumerateArray().ToList();

    private async Task<JsonElement> BoardAsync()
        => await OkJsonAsync(await _sarah.GetAsync($"{Api}/duties"), "reading the duty board");

    /// <summary>
    /// Hands James one of the group's duties: not the lead's (that one also makes him lead, which
    /// adds a sentence of its own), and one he may hold, with an override where the group allows it.
    /// </summary>
    private async Task<string> GiveJamesADutyAsync()
    {
        var duties = (await BoardAsync()).GetProperty("duties").EnumerateArray()
            .Where(d => !string.Equals(d.GetProperty("name").GetString(), "Lead Investigator", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.TryGetProperty("minimumLevelName", out var m) && m.ValueKind == JsonValueKind.String ? 1 : 0)
            .ToList();

        var refusals = new List<string>();
        foreach (var duty in duties)
        {
            var url = $"{Api}/attendees/{_jamesAttendeeId}/duties/{duty.GetProperty("id").GetString()}";
            foreach (var withOverride in new[] { false, true })
            {
                var given = await _sarah.PutAsync(url, new() { DataObject = new { @override = withOverride } });
                if (given.Ok) return duty.GetProperty("name").GetString()!;
                refusals.Add($"{duty.GetProperty("name").GetString()}: {await given.TextAsync()}");
            }
        }

        Assert.Ignore("No duty of Paranormal365's could be given to James, so there is no duty to see go with him. "
                    + string.Join(" | ", refusals));
        return "";
    }

    private string VisitUrl => $"{BaseUrl}/organizations/{_orgId}?tab=investigations&inv={_visitId}";
    private ILocator TeamCard => Page.Locator($"#inv-row-{_visitId} + tr.inv-team-row .card").First;
    private ILocator TeamPanel => Page.Locator($"#inv-row-{_visitId} + tr.inv-team-row .inv-team-panel");
    private ILocator AddSection => Page.Locator($"#investigation-team-add-{_visitId}");
    private ILocator Picker => Page.Locator($"#investigation-team-pick-{_visitId}");
    private ILocator Remove => Page.Locator($"#roster-remove-{_jamesAttendeeId}");
    private ILocator Ask => Page.Locator($"#roster-remove-ask-{_visitId}");
    private ILocator TakeOff => Page.Locator($"#roster-remove-yes-{_visitId}");
    private ILocator Keep => Page.Locator($"#roster-remove-no-{_visitId}");
    private ILocator JamesChip => TeamCard.Locator("span.badge.rounded-pill", new() { HasTextString = _jamesName });

    /// <summary>The duty's row on "Who's doing what", and James's badge in it.</summary>
    private ILocator DutyBadge(string duty) => TeamCard
        .Locator("div.border.rounded.px-2", new()
        {
            Has = Page.Locator("span.fw-semibold", new() { HasTextRegex = new System.Text.RegularExpressions.Regex($"^{System.Text.RegularExpressions.Regex.Escape(duty)}$") }),
        })
        .Locator("span.badge.bg-info", new() { HasTextString = _jamesName });

    private bool _signedIn;

    private async Task OpenTheTeamAsync()
    {
        if (!_signedIn)
        {
            await LoginAsync(UserEmail, UserPassword);
            _signedIn = true;
        }
        await Page.GotoAsync(VisitUrl);
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();
        await Expect(TeamCard).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(TeamCard).ToContainTextAsync("arrived", new() { Timeout = 15_000 });
    }

    // ── Gap 5: the ✕ asks ───────────────────────────────────────────────────

    /// <summary>
    /// The ✕ on somebody holding a duty asks, naming the duty. "Keep them" changes nothing;
    /// "Take them off" takes them and their duty badge away, on the screen and on the server.
    /// </summary>
    [Test]
    public async Task The_remove_button_asks_first_names_the_duty_and_Keep_them_changes_nothing()
    {
        var duty = await GiveJamesADutyAsync();
        await OpenTheTeamAsync();
        await Expect(DutyBadge(duty)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Remove.ClickAsync();
        await Expect(Ask).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Ask.Locator("span.flex-grow-1")).ToHaveTextAsync(
            $"Take {_jamesName} off the team? Their duty ({duty}) goes too.");
        await Expect(TakeOff).ToHaveTextAsync("Take them off");
        await Expect(Keep).ToHaveTextAsync("Keep them");

        // Asked, not done.
        Assert.That((await RosterAsync()).Select(r => r.GetProperty("appUserId").GetString()), Does.Contain(_jamesId),
            "Pressing ✕ took James off before anybody said yes.");

        await Keep.ClickAsync();
        await Expect(Ask).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Expect(JamesChip).ToBeVisibleAsync();
        await Expect(DutyBadge(duty)).ToBeVisibleAsync();
        Assert.That((await RosterAsync()).Select(r => r.GetProperty("appUserId").GetString()), Does.Contain(_jamesId),
            "\"Keep them\" took James off the team.");
        Assert.That((await BoardAsync()).GetProperty("assignments").EnumerateArray()
                .Any(a => a.GetProperty("attendeeId").GetString() == _jamesAttendeeId), Is.True,
            "\"Keep them\" took James's duty away.");

        await Remove.ClickAsync();
        await TakeOff.ClickAsync();
        await Expect(JamesChip).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(DutyBadge(duty)).ToHaveCountAsync(0);
        await Expect(Ask).ToHaveCountAsync(0);
        Assert.That(await RosterAsync(), Is.Empty, "The page shows James gone and the server still has him.");
        Assert.That((await BoardAsync()).GetProperty("assignments").EnumerateArray().Count(), Is.EqualTo(0),
            "James went and his duty stayed behind.");
    }

    /// <summary>Somebody marked as there stays on the team; the server's sentence says why.</summary>
    [Test]
    public async Task Taking_off_somebody_marked_as_there_shows_the_servers_refusal()
    {
        await OkJsonAsync(await _sarah.PutAsync($"{Api}/attendees/{_jamesAttendeeId}/attendance",
            new() { DataObject = new { didAttend = true, statedArrivalTime = DateTime.UtcNow.AddHours(-1) } }), "marking James as there");

        await OpenTheTeamAsync();
        await Remove.ClickAsync();
        await TakeOff.ClickAsync();

        var refusal = AddSection.Locator(".alert-danger");
        await Expect(refusal).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(refusal).ToHaveTextAsync(
            $"{_jamesName} is recorded as having been there, so they stay on the team. Untick Attended first if that was a mistake.");
        await Expect(JamesChip).ToBeVisibleAsync();
        await Expect(Ask).ToHaveCountAsync(0);
        Assert.That((await RosterAsync()).Select(r => r.GetProperty("appUserId").GetString()), Does.Contain(_jamesId),
            "The refusal showed and James was taken off anyway.");
    }

    // ── Gap 6: check-in says the server's reason ────────────────────────────

    /// <summary>
    /// Sarah, on the team, says she arrived tomorrow. The server refuses a future arrival in its
    /// own words, which the page now shows in place of the old fixed sentence.
    /// </summary>
    [Test]
    public async Task A_refused_check_in_shows_the_servers_sentence()
    {
        var sarahId = await MyUserIdAsync(_sarah);
        await PutOnTheTeamAsync(sarahId);

        await OpenTheTeamAsync();
        await TeamCard.GetByRole(AriaRole.Button, new() { Name = "I've arrived" }).ClickAsync();
        var when = Page.Locator("#investigationroster-when-did-you-get-ab36");
        await Expect(when).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Two days on, in the evening: in the future on any clock Sarah's profile may keep.
        var tomorrow = DateTime.Now.Date.AddDays(2).AddHours(21);
        await TypeIntoDateFieldAsync(when, BoxDateTime(tomorrow));
        await Expect(when).ToHaveValueAsync(BoxDateTime(tomorrow));
        await TeamCard.GetByRole(AriaRole.Button, new() { Name = "Check in", Exact = true }).ClickAsync();

        var error = TeamCard.Locator(".alert-danger").First;
        await Expect(error).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(error).ToHaveTextAsync("That arrival time is in the future. Check the date and try again.");
        await Expect(TeamCard).Not.ToContainTextAsync("Check the arrival time is not in the future.");

        var sarah = (await RosterAsync()).Single(r => r.GetProperty("appUserId").GetString() == sarahId);
        Assert.That(sarah.GetProperty("didAttend").ValueKind, Is.Not.EqualTo(JsonValueKind.True),
            "A refused check-in still marked Sarah as there.");
    }

    // ── Gap 13: the list and the Team panel on every screen ─────────────────

    /// <summary>
    /// At every width, with the Team panel open: the panel, its picker, the ✕ and the inline
    /// question are inside the screen and the page does not slide sideways. Below 992px each visit
    /// is a card whose cells name their column; from 992px it is the table. Below 992px the
    /// controls a finger presses are at least 44px.
    /// </summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_investigations_list_and_its_Team_panel_fit_every_screen(string theme)
    {
        await UseThemeAsync(theme);
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenTheTeamAsync();
            await Expect(Remove).ToBeVisibleAsync(new() { Timeout = 15_000 });

            problems.AddRange(await LookAtAsync(TeamPanel, "inv-team-panel", theme));
            problems.AddRange(await LookAtAsync(AddSection, "inv-team-add", theme));
            if (await Picker.CountAsync() > 0)
            {
                problems.AddRange(await LookAtAsync(Picker, "inv-team-picker", theme,
                    AddSection.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true })));
                problems.AddRange(await TouchTargetAsync(Picker, "inv-team-picker", theme));
            }
            problems.AddRange(await LookAtAsync(Remove, "inv-team-remove", theme,
                Page.Locator($"#roster-lead-{_jamesAttendeeId}"), Page.Locator($"#roster-attended-{_jamesAttendeeId}")));
            problems.AddRange(await TouchTargetAsync(Remove, "inv-team-remove", theme));
            problems.AddRange(await TouchTargetAsync(
                AddSection.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }), "inv-team-add-button", theme));

            var edit = Page.Locator($"#inv-edit-{_visitId}");
            problems.AddRange(await LookAtAsync(edit, "inv-edit", theme));
            problems.AddRange(await TouchTargetAsync(edit, "inv-edit", theme));

            var (scrollWidth, innerWidth) = await PageWidthsAsync();
            if (scrollWidth > innerWidth)
                problems.Add($"investigations list at {width}px ({theme}): scrollWidth {scrollWidth} > innerWidth {innerWidth}.");

            problems.AddRange(await LayoutProblemsAsync(width, theme));

            // The ✕'s question, and its two answers.
            await Remove.ClickAsync();
            await Expect(Ask).ToBeVisibleAsync(new() { Timeout = 10_000 });
            problems.AddRange(await LookAtAsync(Ask, "inv-team-remove-ask", theme));
            problems.AddRange(await LookAtAsync(TakeOff, "inv-team-take-off", theme, Keep));
            problems.AddRange(await TouchTargetAsync(TakeOff, "inv-team-take-off", theme));
            problems.AddRange(await TouchTargetAsync(Keep, "inv-team-keep", theme));
            await Keep.ClickAsync();
            await Expect(Ask).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }

        NothingWrong(problems);
    }

    /// <summary>Cards with data-label cells below 992px, the table from 992px.</summary>
    private async Task<List<string>> LayoutProblemsAsync(int width, string theme)
    {
        var problems = new List<string>();
        var tag = $"investigations list at {width}px ({theme})";
        var shape = await Page.EvaluateAsync<JsonElement>(@"(id) => {
            const row = document.getElementById('inv-row-' + id);
            const table = row.closest('table');
            const thead = table.querySelector('thead');
            return {
                table: getComputedStyle(table).display,
                thead: getComputedStyle(thead).display,
                row: getComputedStyle(row).display,
                cells: [...row.children].map(td => ({
                    label: td.getAttribute('data-label') || '',
                    title: td.classList.contains('inv-title'),
                    before: getComputedStyle(td, '::before').content,
                    beforeShown: getComputedStyle(td, '::before').display !== 'none',
                })),
            };
        }", _visitId);

        var table = shape.GetProperty("table").GetString();
        var thead = shape.GetProperty("thead").GetString();
        var row = shape.GetProperty("row").GetString();

        if (width < 992)
        {
            if (thead != "none") problems.Add($"{tag}: the heading row shows ({thead}); below 992px it is cards.");
            if (row != "grid") problems.Add($"{tag}: a visit's row is '{row}', not a card (grid).");
            foreach (var cell in shape.GetProperty("cells").EnumerateArray())
            {
                var label = cell.GetProperty("label").GetString()!;
                if (label.Length == 0) { problems.Add($"{tag}: a cell has no data-label."); continue; }
                if (cell.GetProperty("title").GetBoolean()) continue;   // the title leads the card unlabeled
                var before = cell.GetProperty("before").GetString();
                if (!cell.GetProperty("beforeShown").GetBoolean() || before != $"\"{label}\"")
                    problems.Add($"{tag}: the {label} cell does not name its column (::before is {before}).");
            }
        }
        else
        {
            if (table != "table") problems.Add($"{tag}: the list is '{table}', not a table, at 992px and up.");
            if (thead == "none") problems.Add($"{tag}: the heading row is hidden at 992px and up.");
            if (row != "table-row") problems.Add($"{tag}: a visit's row is '{row}', not a table row.");
        }

        return problems;
    }
}
