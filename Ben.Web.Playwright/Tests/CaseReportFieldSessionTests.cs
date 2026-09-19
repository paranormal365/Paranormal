using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The case manager citing a field session in the final report.
/// </summary>
/// <remarks>
/// Everything the phones collect in the field lands on the site as sessions. The report is what
/// the client is actually handed, so until a section can point at a session, that material is
/// present and unusable — the write-only shape this codebase has now found seven times. This
/// test walks the whole loop: a session uploaded through the API, then cited through the UI.
/// </remarks>
[TestFixture]
[Category("CaseReports")]
public class CaseReportFieldSessionTests : BenTestBase
{
    [Test]
    public async Task A_manager_can_cite_an_uploaded_field_session_in_a_report()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah, TGH
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
        { Assert.Ignore("TGH case not in the seed data."); return; }

        var ids = Regex.Match(Page.Url, @"/organizations/([0-9a-f\-]+)/cases/([0-9a-f\-]+)");
        Assert.That(ids.Success, Is.True, $"unexpected case URL: {Page.Url}");
        var orgId  = ids.Groups[1].Value;
        var caseId = ids.Groups[2].Value;

        // ── A session, uploaded the way the app uploads one ───────────────────
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new()
        {
            DataObject = new { email = UserEmail, password = UserPassword },
        });
        Assert.That(login.Ok, Is.True, "the seeded org user should be able to sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth  = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var investigations = await api.GetAsync(
            $"/api/organizations/{orgId}/cases/{caseId}/investigations", new() { Headers = auth });
        Assert.That(investigations.Ok, Is.True, await investigations.TextAsync());
        var list = (await investigations.JsonAsync())!.Value;
        if (list.GetArrayLength() == 0)
        { Assert.Ignore("This case has no investigation to record against."); return; }
        var investigationId = list[0].GetProperty("id").GetString();

        var label = $"Report citation check {Guid.NewGuid():N}"[..38];
        // A plain raw string with a placeholder: the document is full of closing braces, and
        // interpolation holes inside JSON braces are a fight with the compiler for no gain.
        var document = """
        {"format_version":"1.0.0",
         "device":{"manufacturer":"Apple","model":"iPhone17,1"},
         "session":{"started_at":"2026-08-25T02:05:07.000Z",
                    "ended_at":"2026-08-25T02:09:07.000Z",
                    "location_label":"__LABEL__",
                    "trigger":{"mode":"hybrid","interval_seconds":2}},
         "readings":[
           {"at":"2026-08-25T02:05:07.000Z","triggered_by":"interval",
            "measurements":{"emf":{"value":48.0,"unit":"uT","baseline":48.0},
                            "room":{"value":"Cellar"}}},
           {"at":"2026-08-25T02:06:07.000Z","triggered_by":"manual",
            "measurements":{"marker":{"value":"manual_marker"},
                            "room":{"value":"Cellar"}},
            "note":"cold spot by the stairs"}]}
        """;

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload
        {
            Name = "data.json",
            MimeType = "application/json",
            Buffer = System.Text.Encoding.UTF8.GetBytes(document.Replace("__LABEL__", label)),
        });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        form.Append("investigationId", investigationId!);

        var upload = await api.PostAsync("/api/field-sessions/document",
            new() { Headers = auth, Multipart = form });
        Assert.That(upload.Ok, Is.True, $"upload failed: {await upload.TextAsync()}");

        // ── The report, built the way a manager builds one ────────────────────
        var reportsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Reports" })
                             .Or(Page.GetByText("Reports", new() { Exact = true })).First;
        await reportsTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "New Report" }).First.ClickAsync();
        var title = $"Field kit report {DateTime.UtcNow:HHmmss}";
        await Page.Locator("#reportbuilder-title-af64").FillAsync(title);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).First.ClickAsync();

        // The editor, not the list: the Save button only exists once a report is open.
        await Expect(Page.Locator("#reportbuilder-executive-summary-66bf"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Section" }).First.ClickAsync();
        await Page.Locator("#reportbuilder-title-cf7b").FillAsync("Field work");
        // BenSelect is a native <select>, so this is a real selection rather than a popup dance.
        await Page.Locator("select.form-select").First
                  .SelectOptionAsync(new SelectOptionValue { Label = "Field Sessions" });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First.ClickAsync();

        // ── Citing it ─────────────────────────────────────────────────────────
        var addSession = Page.Locator("[data-testid='add-field-session']").First;
        await Expect(addSession).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await addSession.ClickAsync();

        var candidate = Page.Locator("[data-testid='available-field-session']")
                            .Filter(new() { HasTextString = label }).First;
        await Expect(candidate).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await candidate.Locator("[data-testid='cite-field-session']").ClickAsync();

        var cited = Page.Locator("[data-testid='report-field-session']")
                        .Filter(new() { HasTextString = label }).First;
        await Expect(cited).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The citation carries its readout: what the night held, in one paragraph, so the PDF
        // stands on its own. The uploaded document has magnetic readings, so it names the peak.
        var readout = cited.Locator("[data-testid='session-readout']");
        await Expect(readout).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(readout).ToContainTextAsync("peaked at");
        TestContext.Out.WriteLine("readout: " + (await readout.InnerTextAsync()).Trim());
        // Cited means reachable: the manager can open the recording it points at.
        await Expect(cited.GetByRole(AriaRole.Link, new() { Name = "Play back" }))
            .ToBeVisibleAsync();

        // What the row actually SAYS. The counts read as English — a report that says
        // "1 marks" is the sort of thing a client notices before anything else — and the
        // section is labelled in words rather than the enum's own "FieldSessions".
        var summary = await cited.InnerTextAsync();
        Assert.That(summary, Does.Contain("2 readings"));
        Assert.That(summary, Does.Contain("1 mark").And.Not.Contain("1 marks"));
        await Expect(Page.GetByText("Field Sessions", new() { Exact = true }).First)
            .ToBeVisibleAsync();

        var reconnect = await Page.Locator("#components-reconnect-modal").CountAsync();
        Assert.That(reconnect, Is.EqualTo(0), "the circuit should still be alive");
    }

    /// <summary>
    /// A section with nothing to cite must SAY there is nothing, rather than showing a blank
    /// panel that reads as a broken page.
    /// </summary>
    [Test]
    public async Task An_empty_field_session_picker_explains_itself()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
        { Assert.Ignore("TGH case not in the seed data."); return; }

        var reportsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Reports" })
                             .Or(Page.GetByText("Reports", new() { Exact = true })).First;
        await reportsTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "New Report" }).First.ClickAsync();
        await Page.Locator("#reportbuilder-title-af64").FillAsync($"Empty picker {DateTime.UtcNow:HHmmss}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).First.ClickAsync();
        await Expect(Page.Locator("#reportbuilder-executive-summary-66bf"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Section" }).First.ClickAsync();
        await Page.Locator("#reportbuilder-title-cf7b").FillAsync("Field work");
        await Page.Locator("select.form-select").First
                  .SelectOptionAsync(new SelectOptionValue { Label = "Field Sessions" });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First.ClickAsync();

        var addSession = Page.Locator("[data-testid='add-field-session']").First;
        await Expect(addSession).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await addSession.ClickAsync();

        // Either sessions are listed, or the panel says why there are none. What it must never
        // be is empty and silent.
        var listed  = Page.Locator("[data-testid='available-field-session']");
        var explains = Page.GetByText("No field sessions have been uploaded", new() { Exact = false });
        await Expect(listed.First.Or(explains.First)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
