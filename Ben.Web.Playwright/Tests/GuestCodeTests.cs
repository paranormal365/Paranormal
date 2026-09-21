using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The sheet a guide holds up, and what a guest gets for scanning it (item 248).
/// </summary>
/// <remarks>
/// Walked as the two people who are actually there: the guide who makes the code, and a person
/// with an account who has never been anywhere near this group. Wren is that person — no
/// membership, no case, no client access — so anything she can reach here, she reached with the
/// code and nothing else.
/// </remarks>
[TestFixture]
public sealed class GuestCodeTests : BenTestBase
{
    /// <summary>
    /// The group whose investigations are seeded, and one the guide may run.
    /// </summary>
    /// <remarks>
    /// <para>Resolved by SLUG rather than by clicking the first group in a list. The first version
    /// of this clicked, landed on the group Sarah owns rather than the one she administers, found
    /// no investigation, and <c>Assert.Ignore</c>d — which reports as a pass. Four of these five
    /// tests never ran and said nothing about it.</para>
    ///
    /// <para>So there is no Ignore here. A seat that may run investigations and cannot reach this
    /// button is a finding, not a reason to stop looking.</para>
    /// </remarks>
    private async Task<(string OrgId, string InvestigationId)> AnInvestigationAsync()
    {
        var orgId = await OrgIdBySlugAsync("paranormal365");

        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=investigations");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        var codeButton = Main.GetByRole(AriaRole.Button, new() { Name = "Guest code" }).First;
        await Assertions.Expect(codeButton).ToBeVisibleAsync();

        await ClickUntilUrlAsync(codeButton, @"/investigations/[0-9a-f\-]+/join-code");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        var investigationId = System.Text.RegularExpressions.Regex
            .Match(Page.Url, @"/investigations/([0-9a-f\-]{36})/join-code").Groups[1].Value;
        Assert.That(investigationId, Is.Not.Empty, $"Guest code did not reach a sheet: {Page.Url}");
        return (orgId, investigationId);
    }

    /// <summary>
    /// A code nobody has used yet.
    /// </summary>
    /// <remarks>
    /// Always a FRESH one, never whatever is on screen. These walks share one seeded
    /// investigation, and one of them revokes a holder's pass — which is per person per code, so
    /// a later walk reusing the same sheet was correctly refused with "the guide took that pass
    /// back" and failed on the product doing exactly what it should. Rotating is also the cheapest
    /// isolation available here: it is a button the guide already has.
    /// </remarks>
    private async Task<string> AFreshCodeAsync()
    {
        var typed = Page.Locator("#join-code-typed");
        var rotate = Page.Locator("#join-code-rotate");

        if (await rotate.CountAsync() > 0)
        {
            var before = (await typed.InnerTextAsync()).Trim();
            await rotate.ClickAsync();
            // The sheet is replaced in place, so the only signal is the code itself changing.
            await Assertions.Expect(typed).Not.ToHaveTextAsync(before);
        }
        else
        {
            await ClickUntilAsync(Page.Locator("#join-code-make"), typed);
        }

        await Assertions.Expect(typed).ToBeVisibleAsync();
        return (await typed.InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Types a code into the join page and presses Check.
    /// </summary>
    /// <remarks>
    /// The circuit wait is the whole reason this is a method. <c>@bind</c> updates on the change
    /// event, and a value filled before Blazor has attached its handlers is a value the component
    /// never sees — the box looks right, the button does nothing, and the failure names the
    /// missing result rather than the race that caused it.
    /// </remarks>
    private async Task TypeTheCodeAsync(string code, ILocator expected)
    {
        await Page.GotoAsync($"{BaseUrl}/tonight");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        await Page.Locator("#tonight-code").FillAsync(code);
        await ClickUntilAsync(Page.Locator("#tonight-check"), expected);
    }

    [Test]
    public async Task A_guide_gets_a_sheet_with_a_picture_and_a_code_to_read_out()
    {
        await LoginAsync(UserEmail, UserPassword);
        await AnInvestigationAsync();

        var code = await AFreshCodeAsync();

        // Both halves, because they are for different people: the camera reads the picture, and
        // somebody who had to install the app first types the short one.
        Assert.That(code, Does.Match(@"^[A-Z0-9]{4}-[A-Z0-9]{4}$"),
            "the printed code should be two groups of four, readable off a sheet in the dark");

        var qr = Page.Locator("#join-code-sheet img");
        await Assertions.Expect(qr).ToBeVisibleAsync();
        var src = await qr.GetAttributeAsync("src");
        Assert.That(src, Does.StartWith("data:image/png;base64,"),
            "the picture travels with the record, so the token is never in a URL");

        // The sheet says when it stops. A code with no end is the failure this feature must not have.
        await Assertions.Expect(Page.Locator("#join-code-sheet"))
            .ToContainTextAsync("Stops working");
    }

    [Test]
    public async Task A_stranger_can_read_what_the_code_is_for_before_signing_in()
    {
        await LoginAsync(UserEmail, UserPassword);
        await AnInvestigationAsync();
        var code = await AFreshCodeAsync();

        await LogoutAsync();

        await TypeTheCodeAsync(code, Page.Locator("#tonight-invitation"));

        // What a signed-out stranger is shown, and the list is deliberately short. Anything about
        // the case, the client or the address here would be a leak the sheet never made.
        var card = Page.Locator("#tonight-invitation");
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#tonight-signin")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_typed_code_that_is_not_one_is_told_so_in_words()
    {
        await TypeTheCodeAsync("ZZZZ-ZZZZ", Page.Locator("#tonight-refused"));

        // A sentence, not a status. The reader is in a field and needs to know what to do next.
        await Assertions.Expect(Page.Locator("#tonight-refused"))
            .ToContainTextAsync("don't recognise");
    }

    [Test]
    public async Task Somebody_in_no_group_at_all_can_join_with_the_code_and_only_that()
    {
        await LoginAsync(UserEmail, UserPassword);
        var (orgId, investigationId) = await AnInvestigationAsync();
        var code = await AFreshCodeAsync();

        await LoginAsync(SoloEmail, SoloPassword);

        // Wren belongs to nothing. Without the code this address is not hers to open, which is the
        // control that makes the join below mean something.
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/investigations/{investigationId}/join-code");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        Assert.That(await Page.Locator("#join-code-sheet").CountAsync(), Is.Zero,
            "somebody in no group should never see a guide's sheet");

        await TypeTheCodeAsync(code, Page.Locator("#tonight-join"));
        await ClickUntilAsync(Page.Locator("#tonight-join"), Page.Locator("#tonight-joined"));

        await Assertions.Expect(Page.Locator("#tonight-joined")).ToBeVisibleAsync();

        // Joined, and STILL not in the group: the credential is for tonight's recordings, not a
        // way into anything the group can see. This is the rule the whole feature is built around.
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=investigations");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        Assert.That(
            await Main.GetByRole(AriaRole.Button, new() { Name = "Guest code" }).CountAsync(),
            Is.Zero,
            "a guest pass must not turn into the run of the group's investigations");
    }

    [Test]
    public async Task The_guide_sees_who_joined_and_can_stop_one_of_them()
    {
        await LoginAsync(UserEmail, UserPassword);
        var (orgId, investigationId) = await AnInvestigationAsync();
        var code = await AFreshCodeAsync();

        await LoginAsync(SoloEmail, SoloPassword);
        await TypeTheCodeAsync(code, Page.Locator("#tonight-join"));
        await ClickUntilAsync(Page.Locator("#tonight-join"), Page.Locator("#tonight-joined"));

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/investigations/{investigationId}/join-code");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        // "Who is working tonight" is the question a guide actually has, and it has an answer.
        await Assertions.Expect(Page.Locator("#join-code-holder-count")).Not.ToHaveTextAsync("0");

        var stop = Main.GetByRole(AriaRole.Button, new() { Name = "Stop " }).First;
        await Assertions.Expect(stop).ToBeVisibleAsync();
        await ClickUntilAsync(stop, Main.GetByText("Stopped").First);
        await Assertions.Expect(Main.GetByText("Stopped").First).ToBeVisibleAsync();
    }
}
