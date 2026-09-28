using System.Text.Json;
using Microsoft.Playwright;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Ben, 2026-09-28: "When someone signs up for the site, I would like for them to select their
/// timezone ... end users can see the local time for an investigation or their time for the case."
/// </summary>
/// <remarks>
/// Each browser here is given a zone of its own (Playwright's <c>TimezoneId</c>), because the thing
/// under test is what somebody in a DIFFERENT zone from the place reads — a browser on the same
/// clock as the case would pass every assertion by coincidence.
/// </remarks>
[TestFixture]
[Category("TimeZones")]
public class TimeZoneTests : BenTestBase
{
    [Test]
    public async Task Sign_up_starts_on_the_browsers_zone()
    {
        var context = await Browser.NewContextAsync(new() { TimezoneId = "America/Denver" });
        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync($"{BaseUrl}/signup");
            var picker = page.Locator("#signup-time-zone");
            // The browser's zone arrives after the circuit starts; the picker follows it.
            await Expect(picker).ToHaveValueAsync("America/Denver", new() { Timeout = 20_000 });
            await Expect(picker.Locator("optgroup[label='United States'] option")).ToHaveCountAsync(7);
        }
        finally { await context.CloseAsync(); }
    }

    [Test]
    public async Task A_visit_reads_in_the_cases_time_the_readers_and_the_zone_they_chose()
    {
        var orgId = await OrgIdBySlugAsync("benco");
        var title = $"Clock check {Guid.NewGuid():N}"[..20];
        using var api = await StoreTestApi.OpenAsync();

        // A case in New York, with a visit at 8 PM there — 7 PM in Chicago, 5 PM in Los Angeles.
        var created = await api.SendAsync(HttpMethod.Post, $"/api/organizations/{orgId}/cases", new
        {
            title, description = (string?)null, streetAddress1 = "1 Broadway", streetAddress2 = (string?)null,
            city = "New York", state = "NY", zipCode = "10004", country = "US", latitude = (decimal?)null,
            longitude = (decimal?)null, timeZoneId = "America/New_York",
        });
        var caseId = created.GetProperty("id").GetString()!;
        Assert.That(created.GetProperty("effectiveTimeZoneId").GetString(), Is.EqualTo("America/New_York"));

        try
        {
            await api.SendAsync(HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/investigations", new
            {
                title = "Evening visit", description = (string?)null, location = (string?)null,
                scheduledDateTime = new DateTime(2027, 1, 15, 1, 0, 0, DateTimeKind.Utc),
                endDateTime = (DateTime?)null, status = 0, notes = (string?)null, orgCalendarEventId = (Guid?)null,
            });

            await LoginAsync(MemberEmail, MemberPassword);
            var state = await Context.StorageStateAsync();
            var context = await Browser.NewContextAsync(new() { StorageState = state, TimezoneId = "America/Chicago" });
            try
            {
                var page = await context.NewPageAsync();
                var when = page.Locator("[data-testid='investigation-when']").First;

                await page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cases/{caseId}?tab=investigations");

                // The case's own time by default.
                await Expect(when).ToContainTextAsync("08:00 PM EST", new() { Timeout = 30_000 });

                // The reader's own — their browser's, having chosen nothing.
                await page.Locator("[data-testid='time-zone-mine']").ClickAsync();
                await Expect(when).ToContainTextAsync("07:00 PM CST");

                // Choose Pacific on the profile: the whole site now reads in it.
                await page.GotoAsync($"{BaseUrl}/profile");
                var zone = page.Locator("#profile-time-zone");
                await Expect(zone).ToBeVisibleAsync(new() { Timeout = 30_000 });
                await zone.SelectOptionAsync("America/Los_Angeles");
                await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).First.ClickAsync();
                await Expect(page.GetByText("Saving…")).ToHaveCountAsync(0, new() { Timeout = 15_000 });

                await page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cases/{caseId}?tab=investigations");
                // Remembered: still "My time", and my time is now Pacific.
                await Expect(when).ToContainTextAsync("05:00 PM PST", new() { Timeout = 30_000 });

                // And back to the place's clock for whoever reads next.
                await page.Locator("[data-testid='time-zone-local']").ClickAsync();
                await Expect(when).ToContainTextAsync("08:00 PM EST");

                // Give the profile's choice back.
                await page.GotoAsync($"{BaseUrl}/profile");
                await Expect(zone).ToBeVisibleAsync(new() { Timeout = 30_000 });
                await zone.SelectOptionAsync("");
                await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).First.ClickAsync();
                await Expect(page.GetByText("Saving…")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
            }
            finally { await context.CloseAsync(); }
        }
        finally
        {
            await api.TrySendAsync(HttpMethod.Delete, $"/api/admin/cases/{caseId}/purge", new { confirmTitle = title });
        }
    }
}
