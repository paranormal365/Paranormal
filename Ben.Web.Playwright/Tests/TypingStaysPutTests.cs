using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Deleting in the middle of a box deletes there, however fast the keys come (Ben, 10/15/2026).
/// </summary>
/// <remarks>
/// <para>A box drawn as <c>value="@_field"</c> with <c>@oninput</c> setting the field sent every
/// keystroke's value back from the server. When the reply lagged behind the typing (holding
/// Backspace, or a slow connection), an older copy of the text was written over the box and the
/// browser moved the cursor to the end, so the next delete removed the end of the text.</para>
///
/// <para>The six deletes are made from script in one go, so all six reach the server before the
/// first reply comes back. That is the slow-connection case made certain: on localhost a reply
/// takes a few milliseconds and real key presses only sometimes outrun it.</para>
///
/// <para>The Help search box is the control: it uses <c>@bind:event="oninput"</c>, which Blazor
/// already keeps in step with the browser, and it passed before the fix as well.</para>
/// </remarks>
[TestFixture]
[Category("TypingStaysPut")]
public class TypingStaysPutTests : BenTestBase
{
    private const string Typed = "The cold spot was on the stair landing tonight";
    private const string Expected = "The cold spot was on the landing tonight";

    [Test]
    [Description("A message box on the Contact page keeps the cursor where the deleting happens.")]
    public async Task Deleting_mid_sentence_in_the_contact_message_stays_put()
    {
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForTheCircuitAsync();
        await DeleteStairAndCheckAsync(Page, Page.Locator("#contactpage-message-0df0"));
    }

    [Test]
    [Description("A one-line box on the Contact page keeps the cursor where the deleting happens.")]
    public async Task Deleting_mid_sentence_in_the_contact_subject_stays_put()
    {
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForTheCircuitAsync();
        await DeleteStairAndCheckAsync(Page, Page.Locator("#contactpage-subject-287e"));
    }

    [Test]
    [Description("The Help search box, which binds with @bind:event=\"oninput\", already kept its cursor.")]
    public async Task Deleting_mid_sentence_in_the_help_search_stays_put()
    {
        await Page.GotoAsync($"{BaseUrl}/help");
        await WaitForTheCircuitAsync();
        await DeleteStairAndCheckAsync(Page, Page.Locator("#help-find"));
    }

    /// <summary>
    /// Types a sentence into <paramref name="box"/>, deletes "stair" from its middle in one burst,
    /// and checks the text, the cursor and one more real Backspace. FeedTests uses it too.
    /// </summary>
    internal static async Task DeleteStairAndCheckAsync(IPage page, ILocator box)
    {
        await box.ClickAsync();
        await box.PressSequentiallyAsync(Typed);
        await Assertions.Expect(box).ToHaveValueAsync(Typed);
        await page.WaitForTimeoutAsync(500);   // the typing's own replies have all arrived

        // Six deletes from just after "stair" (the word and the space before it), each one an input
        // event like a held Backspace, all sent before the server can answer the first.
        await box.EvaluateAsync(@"el => {
            el.focus();
            let at = el.value.indexOf('stair') + 5;
            el.setSelectionRange(at, at);
            for (let i = 0; i < 6; i++) {
                el.setRangeText('', at - 1, at, 'end');
                at--;
                el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'deleteContentBackward' }));
            }
        }");
        await page.WaitForTimeoutAsync(1500);   // every reply the server was going to send has arrived

        await Assertions.Expect(box).ToHaveValueAsync(Expected);
        var caret = await box.EvaluateAsync<int>("el => el.selectionStart");
        Assert.That(caret, Is.EqualTo("The cold spot was on the".Length),
            "the cursor should stay where the deleting happened, not jump to the end");

        // And the next key lands there too.
        await box.PressAsync("Backspace");
        await Assertions.Expect(box).ToHaveValueAsync("The cold spot was on th landing tonight");
    }
}
