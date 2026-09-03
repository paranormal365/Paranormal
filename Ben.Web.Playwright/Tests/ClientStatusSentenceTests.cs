using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>The client's case page says, under the badge, the sentence their mail will carry (item 206).</summary>
[TestFixture]
[Category("ClientStatusSentence")]
public class ClientStatusSentenceTests : BenTestBase
{
    [Test]
    public async Task A_client_sees_what_their_case_status_means()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        // The list is clickable cards, and it renders only once the circuit is live.
        await Page.WaitForSelectorAsync("[data-testid='my-case-card'], [data-testid='no-cases'], .alert", new() { Timeout = 30_000 });
        var first = Page.Locator("[data-testid='my-case-card']").First;
        if (await first.CountAsync() == 0) Assert.Ignore("this client has no case to open");
        await first.ClickAsync();

        var sentence = Page.Locator("[data-testid='status-sentence']");
        await Expect(sentence).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var text = (await sentence.InnerTextAsync()).Trim();
        Assert.That(text, Does.EndWith("."));
        Assert.That(text.Length, Is.GreaterThan(30), "a sentence, not a label");
        TestContext.Out.WriteLine("status sentence: " + text);
    }
}
