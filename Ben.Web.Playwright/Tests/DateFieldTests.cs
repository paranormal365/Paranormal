using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A date field has to keep the date you are building.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-09: <em>"I had entered 09 and was moving the numbers up for the date from 00 to 18
/// and before I could even get to 18, it moved and my 09 had changed to 10."</em>
/// </para>
/// <para>
/// Measured on the isolated stack: an <b>empty</b> Telerik date field rebuilds the whole date the
/// first time a segment is stepped. Type <c>09</c>, press Up once on the day, and the field reads
/// <c>10/01</c>. It is deterministic, not a race — a single slow press does it. A field that
/// already holds a date steps its segments correctly, twelve fast presses running.
/// </para>
/// </remarks>
[TestFixture]
[Category("DateField")]
public class DateFieldTests : BenTestBase
{
    private async Task<bool> OpenProposeDatesAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        await OpenTabAsync("Investigations",
            Main.GetByText("Date Proposals to Client", new() { Exact = false }).First);
        await SkipAnyTourAsync();
        await Main.GetByText("Date Proposals to Client", new() { Exact = false }).First.ClickAsync();

        var propose = Main.GetByRole(AriaRole.Button, new() { Name = "Propose Dates" });
        await Expect(propose).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await propose.ClickAsync();
        await Expect(Page.Locator(".modal.show").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Page.WaitForTimeoutAsync(500);
        return true;
    }

    /// <summary>
    /// The date half of the first proposed slot, whichever control is carrying it — a union, so
    /// this test still finds the field if the split into a date box and a time box is ever
    /// undone, and reports the mask rather than "no such element".
    /// </summary>
    private ILocator FirstDateBox =>
        Page.Locator(".modal.show .k-datepicker input.k-input-inner, .modal.show .k-datetimepicker input.k-input-inner").First;

    /// <summary>
    /// The precondition for the fault, stated as a rule: no date field is ever handed over empty.
    /// </summary>
    [Test]
    public async Task A_proposed_date_starts_from_a_real_date()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var value = await FirstDateBox.InputValueAsync();
        TestContext.Out.WriteLine($"first slot: \"{value}\"");

        Assert.That(value, Does.Match(@"^\d{2}/\d{2}/\d{4}$"),
            "The date field was handed over empty, which is what lets a stepped segment rewrite the whole date.");
    }

    /// <summary>Stepping the day must not touch the month.</summary>
    [Test]
    public async Task Stepping_the_day_leaves_the_month_alone()
    {
        if (!await OpenProposeDatesAsync())
            Assert.Ignore("Seeded Paranormal365/Belmont case not reachable.");

        var before = await FirstDateBox.InputValueAsync();
        Assert.That(before, Does.Match(@"^\d{2}/\d{2}/\d{4}$"), $"Unexpected starting value \"{before}\".");

        // The day segment of MM/dd/yyyy.
        await Page.EvaluateAsync(
            @"() => { const e = document.querySelector('.modal.show .k-datepicker input.k-input-inner');
                      e.focus(); e.setSelectionRange(3, 5); }");
        await Page.WaitForTimeoutAsync(200);

        for (var i = 0; i < 8; i++)
        {
            await Page.Keyboard.PressAsync("ArrowUp");
            await Page.WaitForTimeoutAsync(40);
        }
        await Page.WaitForTimeoutAsync(800);

        var after = await FirstDateBox.InputValueAsync();
        TestContext.Out.WriteLine($"{before} -> {after}");

        var month = new Func<string, string>(v => Regex.Match(v, @"^(\d{2})/").Groups[1].Value);
        Assert.That(month(after), Is.EqualTo(month(before)),
            $"Stepping the day changed the month: {before} became {after}.");
    }
}
