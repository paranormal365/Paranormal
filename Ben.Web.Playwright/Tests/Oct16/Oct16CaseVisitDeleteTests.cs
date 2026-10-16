using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// Deleting a visit on a case's Investigations tab (fix/oct16-gaps, 10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>What changed.</b> Ben: "Same rule for both". A case's visit with recordings, written
/// accounts or anybody marked as there can no longer be deleted (it used to take its recordings
/// with it), and the dialog says why. And the dialog's Delete now asks first, inline, instead of
/// deleting on the first click.</para>
///
/// <para><b>What it makes, and how it goes.</b> Each test makes its own case in Paranormal365
/// (unplaced, no client, so nobody is mailed) and its own visit, as Sarah, who administers the
/// group. A recording filed to a visit cannot be deleted by anybody but the case's own purge, so
/// teardown purges the case as the SuperAdmin, which hands the recording back to Sarah as a
/// personal session, and then Sarah deletes that.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("CaseVisitDelete")]
[NonParallelizable]
public class Oct16CaseVisitDeleteTests : Oct16TestBase
{
    private const string Refusal =
        "This visit has 1 recording, and deleting it would lose them. Cancel it instead. "
      + "A canceled visit keeps them on record.";

    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private readonly List<(string Id, string Title)> _cases = [];
    private readonly List<string> _sessions = [];
    private bool _signedIn;

    [SetUp]
    public async Task Sarah()
    {
        _signedIn = false;
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
    }

    [TearDown]
    public async Task PurgeTheCasesAndDeleteTheRecordings()
    {
        if (_cases.Count > 0)
        {
            var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
            foreach (var (id, title) in _cases)
            {
                try
                {
                    await admin.DeleteAsync($"/api/admin/cases/{id}/purge", new() { DataObject = new { confirmTitle = title } });
                }
                catch { /* reported by the test that made it */ }
            }
        }
        _cases.Clear();

        // Detached by the purge, so they are Sarah's own again and hers to delete.
        foreach (var session in _sessions)
        {
            try { await _sarah.DeleteAsync($"/api/field-sessions/{session}"); } catch { }
        }
        _sessions.Clear();
    }

    /// <summary>A case of its own, and one visit on it next week, as Sarah.</summary>
    private async Task<(string CaseId, string VisitId, string VisitTitle)> ACaseVisitAsync(string what)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var caseTitle = $"Oct16 delete {what} {tag}";
        var made = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/cases", new()
        {
            DataObject = new
            {
                title = caseTitle, description = (string?)null,
                streetAddress1 = "1 Test Lane", streetAddress2 = (string?)null,
                city = "Adams", state = "TN", zipCode = "37010", country = "US",
                latitude = (decimal?)null, longitude = (decimal?)null,
            },
        }), $"making the case \"{caseTitle}\"");
        var caseId = made.GetProperty("id").GetString()!;
        _cases.Add((caseId, caseTitle));

        var start = DateTime.UtcNow.Date.AddDays(7).AddHours(24);
        var visitTitle = $"Oct16 visit {what} {tag}";
        var visit = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/cases/{caseId}/investigations", new()
        {
            DataObject = new
            {
                title = visitTitle, description = (string?)null, location = (string?)null,
                scheduledDateTime = start, endDateTime = start.AddHours(3), status = 0,
                notes = (string?)null, orgCalendarEventId = (Guid?)null,
            },
        }), $"scheduling \"{visitTitle}\"");
        return (caseId, visit.GetProperty("id").GetString()!, visitTitle);
    }

    /// <summary>A short session document, filed to the visit, as Sarah.</summary>
    private async Task ARecordingOnAsync(string visitId)
    {
        static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var at = DateTime.UtcNow.AddHours(-2);
        var document = "{\"format_version\":\"1.0.0\",\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone17,1\"},"
            + "\"session\":{\"started_at\":\"" + Stamp(at) + "\",\"ended_at\":\"" + Stamp(at.AddSeconds(4)) + "\","
            + "\"location_label\":\"Oct16 delete test\",\"trigger\":{\"mode\":\"hybrid\",\"interval_seconds\":2}},"
            + "\"readings\":[{\"at\":\"" + Stamp(at) + "\",\"triggered_by\":\"interval\","
            + "\"measurements\":{\"emf\":{\"value\":48,\"unit\":\"uT\",\"baseline\":48.0}}}]}";

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "data.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(document) });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        form.Append("investigationId", visitId);
        var upload = await OkJsonAsync(await _sarah.PostAsync("/api/field-sessions/document", new() { Multipart = form }),
            "filing a recording to the visit");
        _sessions.Add(upload.GetProperty("id").GetString()!);
    }

    private ILocator Dialog => Page.Locator(".modal.show").First;
    private ILocator VisitCard(string title) => Main.Locator(".card", new() { HasTextString = title });

    /// <summary>The case's Investigations tab, then Edit on the visit, until its dialog is open.</summary>
    private async Task OpenTheVisitAsync(string caseId, string visitId, string title)
    {
        if (!_signedIn)
        {
            await LoginAsync(UserEmail, UserPassword);
            _signedIn = true;
        }
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cases/{caseId}?tab=investigations");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        await Expect(VisitCard(title).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ClickUntilAsync(Page.Locator($"#case-inv-edit-{visitId}"), Dialog.Locator("#investigation-dialog-delete"));
        await Expect(Dialog.Locator("#investigationpanel-title-092d")).ToHaveValueAsync(title, new() { Timeout = 15_000 });
    }

    private async Task<bool> VisitExistsAsync(string caseId, string visitId)
        => (await _sarah.GetAsync($"/api/organizations/{_orgId}/cases/{caseId}/investigations/{visitId}")).Ok;

    // ── (1) A recording keeps the visit ─────────────────────────────────────

    [Test]
    public async Task Deleting_a_case_visit_with_a_recording_is_refused_in_words_and_the_visit_stays()
    {
        var (caseId, visitId, title) = await ACaseVisitAsync("recording");
        await ARecordingOnAsync(visitId);

        await OpenTheVisitAsync(caseId, visitId, title);
        await Dialog.Locator("#investigation-dialog-delete").ClickAsync();
        await Expect(Dialog.Locator("#investigation-dialog-confirm")).ToContainTextAsync("Delete this visit? This can't be undone.");
        await Dialog.Locator("#investigation-dialog-confirm-yes").ClickAsync();

        await Expect(Page.Locator("#investigation-dialog-error")).ToHaveTextAsync(Refusal, new() { Timeout = 15_000 });
        await Expect(Page.Locator("#investigation-dialog-confirm")).ToHaveCountAsync(0);
        Assert.That(await VisitExistsAsync(caseId, visitId), Is.True, "The refusal showed and the visit was deleted anyway.");

        await Dialog.Locator(".dialog-footer-actions").GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Expect(VisitCard(title).First).ToBeVisibleAsync();
    }

    // ── (2) An empty visit: asked first, Keep, then Delete ──────────────────

    [Test]
    public async Task Deleting_an_empty_case_visit_asks_first_Keep_changes_nothing_and_Delete_removes_it()
    {
        var (caseId, visitId, title) = await ACaseVisitAsync("empty");

        await OpenTheVisitAsync(caseId, visitId, title);

        // The first press asks; it no longer deletes.
        await Dialog.Locator("#investigation-dialog-delete").ClickAsync();
        var ask = Dialog.Locator("#investigation-dialog-confirm");
        await Expect(ask).ToContainTextAsync("Delete this visit? This can't be undone.");
        await Expect(Dialog.Locator("#investigation-dialog-confirm-no")).ToHaveTextAsync("Keep");
        await Expect(Dialog.Locator("#investigation-dialog-confirm-yes")).ToHaveTextAsync("Delete");
        Assert.That(await VisitExistsAsync(caseId, visitId), Is.True, "Pressing Delete once deleted the visit without asking.");

        // Keep: the question goes, the dialog and the visit stay.
        await Dialog.Locator("#investigation-dialog-confirm-no").ClickAsync();
        await Expect(ask).ToHaveCountAsync(0);
        await Expect(Dialog).ToBeVisibleAsync();
        Assert.That(await VisitExistsAsync(caseId, visitId), Is.True, "Keep deleted the visit.");

        // Delete: asked again, then gone, from the page and the server.
        await Dialog.Locator("#investigation-dialog-delete").ClickAsync();
        await Dialog.Locator("#investigation-dialog-confirm-yes").ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(VisitCard(title)).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        Assert.That(await VisitExistsAsync(caseId, visitId), Is.False, "The card went and the visit is still on the server.");
    }

    // ── (3) The question on every screen ────────────────────────────────────

    /// <summary>
    /// The question and its Keep / Delete, on every screen in <see cref="Oct16TestBase.Widths"/>:
    /// visible, inside the screen, not on top of each other, and at least 44px to touch below 992px.
    /// </summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_delete_questions_buttons_fit_every_screen_and_are_big_enough_to_touch(string theme)
    {
        await UseThemeAsync(theme);
        var (caseId, visitId, title) = await ACaseVisitAsync($"look {theme}");

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenTheVisitAsync(caseId, visitId, title);

            await Dialog.Locator("#investigation-dialog-delete").ClickAsync();
            var ask = Dialog.Locator("#investigation-dialog-confirm");
            var keep = Dialog.Locator("#investigation-dialog-confirm-no");
            var delete = Dialog.Locator("#investigation-dialog-confirm-yes");

            problems.AddRange(await LookAtAsync(ask, "case-visit-delete-question", theme));
            problems.AddRange(await LookAtAsync(keep, "case-visit-delete-keep", theme, delete));
            problems.AddRange(await LookAtAsync(delete, "case-visit-delete-delete", theme, keep));
            problems.AddRange(await TouchTargetAsync(keep, "case-visit-delete-keep", theme));
            problems.AddRange(await TouchTargetAsync(delete, "case-visit-delete-delete", theme));

            await keep.ClickAsync();
            await Expect(ask).ToHaveCountAsync(0);
            await Dialog.Locator(".dialog-footer-actions").GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }

        Assert.That(await VisitExistsAsync(caseId, visitId), Is.True, "Looking at the question deleted the visit.");
        NothingWrong(problems);
    }
}
