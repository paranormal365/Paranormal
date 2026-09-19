using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The coupon screen can actually create the campaign a comp needs: 100% off, every period
/// forever, as a batch of single-use codes.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-18:</b> "Would I create a code if I wanted to create another comped
/// subscription and just make it free for 100 years or something?" — the answer is yes, and a
/// <c>Forever</c> coupon is better than a hand-written subscription row: it renews itself at zero
/// with no card (the renewal job fulfils directly when payable is nothing, before it ever looks
/// for a card), it leaves a redemption record saying WHY a group pays nothing, and it writes a
/// ledger row each period instead of leaving a mystery subscription behind.</para>
///
/// <para><b>Why a browser test and not a source scan.</b> The server has supported
/// <c>CouponDuration.Forever</c> and generated batches since the coupon work shipped, and
/// <c>AdminCoupons.razor</c> has the <c>&lt;option&gt;</c> elements in its markup. Neither fact
/// means a SuperAdmin can do it: this codebase has repeatedly shipped endpoints whose page never
/// called them, and the admin pages are the worst place for that because only one person can open
/// them, so nobody else ever finds them broken. The existing <c>AdminPageTests</c> only asserts
/// <c>/admin/coupons</c> renders a heading.</para>
///
/// <para>So this drives the whole path a comp actually takes — open the form, choose the options,
/// save, then generate codes against the saved campaign and read them back.</para>
/// </remarks>
[TestFixture]
[Category("Admin")]
public class CouponCompTests : BenTestBase
{
    private static string UniqueName() => $"Comp probe {Guid.NewGuid():N}"[..24];

    /// <summary>
    /// The two choices a permanent comp depends on are offered, not merely supported.
    /// </summary>
    [Test]
    public async Task TheCouponForm_OffersForeverAndGeneratedCodes()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/coupons", new() { Timeout = 25_000 });
        await WaitUntilLoadedAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "New Campaign" }).ClickAsync();
        await Page.WaitForSelectorAsync("#cpn-duration", new() { Timeout = 15_000 });

        var durations = await Page.Locator("#cpn-duration option").AllInnerTextsAsync();
        var kinds = await Page.Locator("#cpn-kind option").AllInnerTextsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(" | ", durations), Does.Contain("forever").IgnoreCase,
                "the duration picker offers no 'forever', so a permanent comp cannot be made here");
            Assert.That(string.Join(" | ", kinds), Does.Contain("generated").IgnoreCase,
                "the kind picker offers no generated batch, so every comp would share one code");
        });
    }

    /// <summary>
    /// And the campaign saves and yields codes — the whole path a comp takes, in one go.
    /// </summary>
    /// <remarks>
    /// A batch of single-use codes is the safe shape for a 100%-off forever campaign: a shared one
    /// that leaked would be a permanent free plan for anybody who typed it. This asserts the
    /// per-code uses box exists for the same reason.
    /// </remarks>
    [Test]
    public async Task AForeverCompCampaign_SavesAndGeneratesCodes()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/coupons", new() { Timeout = 25_000 });
        await WaitUntilLoadedAsync();

        var name = UniqueName();

        await Page.GetByRole(AriaRole.Button, new() { Name = "New Campaign" }).ClickAsync();
        await Page.WaitForSelectorAsync("#cpn-name", new() { Timeout = 15_000 });

        await Page.FillAsync("#cpn-name", name);
        await Page.SelectOptionAsync("#cpn-kind", new SelectOptionValue { Label = "A batch of generated single-use codes" });
        await Page.SelectOptionAsync("#cpn-disc-kind", "percent");
        await Page.FillAsync("#cpn-disc-val", "100");
        await Page.SelectOptionAsync("#cpn-duration", new SelectOptionValue { Label = "Every period, forever" });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // The campaign reaches the list, which is the server having accepted 100%/Forever/Generated.
        //
        // Read from the page's TEXT rather than asserted visible: a Telerik grid's cells never
        // satisfy Playwright's visibility check on this screen — the first run of this test watched
        // the right cell resolve forty-one times and be called hidden every time — and the admin
        // tests in this repo read InnerText for the same reason. The fact under test is that the
        // row is there at all.
        var listed = await WaitForTextAsync(name, TimeSpan.FromSeconds(20));
        Assert.That(listed, Is.True,
            $"the campaign never reached the list, so 100%/Forever/Generated was refused:\n"
            + await Main.InnerTextAsync());

        // ── and it can actually produce codes ────────────────────────────────
        var row = Main.Locator("tr", new() { HasText = name });
        await row.GetByRole(AriaRole.Button, new() { Name = "Codes" }).First.ClickAsync(new() { Force = true });
        await Page.WaitForSelectorAsync("#gen-count", new() { Timeout = 15_000 });

        // One use per code: a 100%-off forever code that can be redeemed repeatedly is a standing
        // free plan for anybody who gets hold of it.
        await Page.FillAsync("#gen-count", "2");
        await Page.FillAsync("#gen-uses", "1");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate" }).ClickAsync();

        // The empty-state sentence goes away, which only happens when codes came back.
        var gotCodes = await WaitUntilAsync(
            async () => !(await Main.InnerTextAsync()).Contains("No codes yet"),
            TimeSpan.FromSeconds(20));

        var afterGenerate = await Main.InnerTextAsync();
        Assert.Multiple(() =>
        {
            Assert.That(afterGenerate, Does.Not.Contain("An unhandled error has occurred"),
                "generating codes threw");
            Assert.That(gotCodes, Is.True,
                $"Generate produced no codes:\n{afterGenerate}");
        });
    }

    /// <summary>Polls the rendered text, which is how the admin screens are read here.</summary>
    private Task<bool> WaitForTextAsync(string needle, TimeSpan within)
        => WaitUntilAsync(async () => (await Main.InnerTextAsync()).Contains(needle), within);

    private async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            try { if (await condition()) return true; }
            catch (PlaywrightException) { /* mid-render; try again */ }
            await Task.Delay(400);
        }
        return false;
    }
}
