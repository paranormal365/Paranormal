using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// A visit with no case can have a team (fix/investigation-team-and-rsvp, 10/16/2026): its Team
/// panel on Group → Investigations has "Add to the team" and a ✕ per person for whoever manages it,
/// and nothing of the kind for a plain member.
/// </summary>
/// <remarks>
/// <para>Before the branch the panel said "Nobody has been added to this investigation yet." and
/// offered nothing; the only add door was the case page's, so a case-less visit could never be
/// staffed, checked in to, led or given duties.</para>
///
/// <para><b>Seats.</b> Sarah administers Paranormal365 and manages by role (the seeded Owner is the
/// SuperAdmin, who passes every check by being SuperAdmin, so he would prove nothing). James is a
/// plain Member. Victor is a Viewer, who must not be offered. One visit, "Oct16 team visit" at Bell
/// Witch Cave, is made the first time and reused (written before fix/oct16-gaps let a case-less
/// visit be deleted; Oct16TeamRemovalTests makes and deletes its own); every test starts it with an
/// empty team.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("InvestigationTeam")]
[NonParallelizable]
public class Oct16InvestigationTeamTests : Oct16TestBase
{
    private const string VisitTitle = "Oct16 team visit";

    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private string _visitId = "";
    private string _jamesId = "";
    private string _jamesName = "";
    private string _victorId = "";

    [SetUp]
    public async Task AVisitWithAnEmptyTeam()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        _jamesId = await MyUserIdAsync(await ApiAsAsync(MemberEmail, MemberPassword));
        _victorId = await MyUserIdAsync(await ApiAsAsync(ViewerEmail, ViewerPassword));

        var members = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/roster"), "reading the group's members");
        var james = members.EnumerateArray().FirstOrDefault(m => m.GetProperty("appUserId").GetString() == _jamesId);
        Assert.That(james.ValueKind, Is.EqualTo(JsonValueKind.Object), "James is not a member of Paranormal365.");
        _jamesName = james.GetProperty("displayName").GetString() ?? "A member";

        var list = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations"), "reading the visits");
        var existing = list.EnumerateArray().FirstOrDefault(r => r.GetProperty("title").GetString() == VisitTitle);
        if (existing.ValueKind == JsonValueKind.Object)
        {
            _visitId = existing.GetProperty("id").GetString()!;
        }
        else
        {
            // Kept to the group (GroupOnly = 1) so it stays off the cave's public page — unless the
            // plan shares every landmark visit, in which case the server's default is taken.
            object Visit(int? visibility) => new
            {
                title = VisitTitle,
                scheduledDateTime = DateTime.UtcNow.Date.AddDays(30).AddHours(1),
                endDateTime = DateTime.UtcNow.Date.AddDays(30).AddHours(5),
                placeId = BellWitchCaveId,
                visibility,
            };
            var url = $"/api/organizations/{_orgId}/investigations";
            var response = await _sarah.PostAsync(url, new() { DataObject = Visit(1) });
            if (!response.Ok) response = await _sarah.PostAsync(url, new() { DataObject = Visit(null) });
            _visitId = (await OkJsonAsync(response, "scheduling the team visit")).GetProperty("id").GetString()!;
        }

        await EmptyTheTeamAsync();
    }

    [TearDown]
    public async Task EmptyTheTeamAgain()
    {
        try { await EmptyTheTeamAsync(); } catch { /* the next SetUp tries again */ }
    }

    private async Task<List<JsonElement>> RosterAsync()
        => (await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations/{_visitId}/roster"),
                "reading the team")).EnumerateArray().ToList();

    private async Task EmptyTheTeamAsync()
    {
        foreach (var entry in await RosterAsync())
        {
            var gone = await _sarah.DeleteAsync(
                $"/api/organizations/{_orgId}/investigations/{_visitId}/attendees/{entry.GetProperty("attendeeId").GetString()}");
            Assert.That(gone.Ok, Is.True, "a leftover team member could not be taken off: " + await gone.TextAsync());
        }
    }

    private string VisitUrl => $"{BaseUrl}/organizations/{_orgId}?tab=investigations&inv={_visitId}";
    /// <summary>The open Team panel: the roster card in the row under the visit (one open at a time).</summary>
    private ILocator TeamCard => Page.Locator("td.bg-body-tertiary .card").First;
    private ILocator AddSection => Page.Locator($"#investigation-team-add-{_visitId}");
    private ILocator Picker => Page.Locator($"#investigation-team-pick-{_visitId}");
    private ILocator JamesChip => TeamCard.Locator("span.badge.rounded-pill", new() { HasTextString = _jamesName });
    private ILocator JamesRow => TeamCard.Locator("table tbody tr", new() { HasTextString = _jamesName });
    private ILocator PeopleCount => Page.Locator($"#inv-row-{_visitId} td.text-center");

    private async Task OpenTheTeamAsync(string email, string password)
    {
        await LoginAsync(email, password);
        await Page.GotoAsync(VisitUrl);
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();
        // The ?inv= row arrives highlighted with its team already open.
        await Expect(TeamCard).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(TeamCard).ToContainTextAsync("arrived", new() { Timeout = 15_000 });
    }

    // ── The manager's half ──────────────────────────────────────────────────

    [Test]
    public async Task An_administrator_puts_a_member_on_the_team_makes_them_lead_and_takes_them_off()
    {
        await OpenTheTeamAsync(UserEmail, UserPassword);
        await Expect(TeamCard).ToContainTextAsync("Nobody has been added to this investigation yet.");

        // The picker: active members who can take part, never a Viewer.
        await Expect(Picker).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(AddSection.GetByText("Add to the team", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Picker.Locator($"option[value='{_jamesId}']")).ToHaveCountAsync(1);
        await Expect(Picker.Locator($"option[value='{_victorId}']")).ToHaveCountAsync(0);

        var add = AddSection.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true });
        await Expect(add).ToBeDisabledAsync();   // nothing chosen yet
        await Picker.SelectOptionAsync(new[] { new SelectOptionValue { Value = _jamesId } });
        await Expect(add).ToBeEnabledAsync();
        await add.ClickAsync();

        // On the team: a chip, a manage row, the grid's count, and the server agrees.
        await Expect(JamesChip).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(JamesRow).ToBeVisibleAsync();
        await Expect(PeopleCount).ToHaveTextAsync("1");
        await Expect(Picker.Locator($"option[value='{_jamesId}']")).ToHaveCountAsync(0);
        Assert.That((await RosterAsync()).Select(r => r.GetProperty("appUserId").GetString()), Does.Contain(_jamesId),
            "The page shows James on the team and the server does not have him.");

        // The Lead radio needs a team row, which nobody could have before.
        var lead = JamesRow.Locator("input[type=radio]");
        await Expect(lead).ToBeEnabledAsync();
        await lead.CheckAsync();
        await Expect(JamesChip).ToContainTextAsync("Lead", new() { Timeout = 15_000 });
        Assert.That((await RosterAsync()).Single(r => r.GetProperty("appUserId").GetString() == _jamesId)
                        .GetProperty("isLead").GetBoolean(), Is.True, "The radio says lead and the server does not.");

        // ✕ asks first since fix/oct16-gaps (gap 5); "Take them off" takes him off: chip, row and
        // count go, and he is offered again.
        await JamesRow.Locator($"button[title='Take {_jamesName} off the team']").ClickAsync();
        await Expect(Page.Locator($"#roster-remove-ask-{_visitId}")).ToContainTextAsync($"Take {_jamesName} off the team?");
        await Page.Locator($"#roster-remove-yes-{_visitId}").ClickAsync();
        await Expect(JamesChip).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(TeamCard).ToContainTextAsync("Nobody has been added to this investigation yet.");
        await Expect(PeopleCount).ToHaveTextAsync("0");
        await Expect(Picker.Locator($"option[value='{_jamesId}']")).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        Assert.That(await RosterAsync(), Is.Empty, "The page shows an empty team and the server still has somebody on it.");
    }

    /// <summary>
    /// Somebody else put James on the team after this page drew its picker (another tab, another
    /// person). Pressing Add then is refused, and the refusal is said in words — not swallowed,
    /// which is what the old adapter did with every refusal.
    /// </summary>
    [Test]
    public async Task Adding_somebody_already_on_the_team_is_refused_with_a_sentence()
    {
        await OpenTheTeamAsync(UserEmail, UserPassword);
        await Expect(Picker).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Picker.SelectOptionAsync(new[] { new SelectOptionValue { Value = _jamesId } });

        // The other tab.
        await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/investigations/{_visitId}/attendees",
            new() { DataObject = new { appUserId = _jamesId } }), "adding James from the other tab");

        await AddSection.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        var refusal = AddSection.Locator(".alert-danger");
        await Expect(refusal).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(refusal).ToContainTextAsync($"{_jamesName} is already on this team.");
        Assert.That((await RosterAsync()).Count(r => r.GetProperty("appUserId").GetString() == _jamesId), Is.EqualTo(1),
            "A refused second add still changed the team.");
    }

    // ── The member's half ───────────────────────────────────────────────────

    /// <summary>
    /// A plain member reads the team and is offered nothing that changes it. He is put on the team
    /// first (not as lead — a lead manages the visit), so there is a row where a wrongly shown ✕ or
    /// Lead radio would appear.
    /// </summary>
    [Test]
    public async Task A_plain_member_sees_the_team_and_no_way_to_change_it()
    {
        await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/investigations/{_visitId}/attendees",
            new() { DataObject = new { appUserId = _jamesId } }), "putting James on the team");

        await OpenTheTeamAsync(MemberEmail, MemberPassword);
        await Expect(JamesChip).ToBeVisibleAsync(new() { Timeout = 15_000 });
        // His own way in is still there: being on the team is what lets him check in.
        await Expect(TeamCard.GetByRole(AriaRole.Button, new() { Name = "I've arrived" })).ToBeVisibleAsync();

        await Expect(AddSection).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Add to the team", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(TeamCard.Locator("button[title^='Take ']")).ToHaveCountAsync(0);
        await Expect(TeamCard.Locator("input[type=radio]")).ToHaveCountAsync(0);
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    /// <summary>
    /// The picker and a ✕ on a phone and a laptop. The panel opens inside the investigations grid,
    /// which scrolls sideways on a phone, so this is where a control can be off the screen while
    /// the page itself never slides.
    /// </summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_add_to_team_picker_and_the_remove_button_fit_the_screen(string theme)
    {
        await UseThemeAsync(theme);
        await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/investigations/{_visitId}/attendees",
            new() { DataObject = new { appUserId = _jamesId } }), "putting James on the team to have a ✕ to look at");

        await LoginAsync(UserEmail, UserPassword);
        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync(VisitUrl);
            await WaitForTheCircuitAsync();
            await WaitUntilLoadedAsync();
            await SkipAnyTourAsync();
            await Expect(JamesRow).ToBeVisibleAsync(new() { Timeout = 30_000 });

            var addSentence = AddSection.Locator(".form-text");
            problems.AddRange(await LookAtAsync(AddSection, "team-add-picker", theme, JamesRow));
            if (await Picker.CountAsync() > 0)
                problems.AddRange(await LookAtAsync(Picker, "team-add-select", theme,
                    AddSection.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }), addSentence));
            var remove = JamesRow.Locator($"button[title='Take {_jamesName} off the team']");
            problems.AddRange(await LookAtAsync(remove, "team-remove-button", theme,
                JamesRow.Locator("input[type=radio]")));
            problems.AddRange(await TouchTargetAsync(remove, "team-remove-button", theme));
            problems.AddRange(await TouchTargetAsync(
                AddSection.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }), "team-add-button", theme));
        }

        NothingWrong(problems);
    }
}
