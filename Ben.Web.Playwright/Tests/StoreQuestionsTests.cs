using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A product's FAQ and shoppers' questions (store sellers, backlog 251, P12): the page shows the FAQ
/// after the reviews; a signed-in shopper asks a question, the seller answers it from her inbox —
/// never told who asked — and the shopper reads the answer under My questions.
/// </summary>
/// <remarks>Seeded (StoreDemoSeeder): Hazel's Hand-Built REM Pod has two FAQ entries.</remarks>
[TestFixture]
[Category("Store")]
public class StoreQuestionsTests : BenTestBase
{
    [Test]
    [Description("Sarah reads the REM pod's FAQ and asks a question; Hazel answers it; Sarah reads the answer.")]
    public async Task A_shopper_asks_and_the_seller_answers()
    {
        var question = $"Does it work in the cold, below freezing? ({Guid.NewGuid().ToString("N")[..6]})";
        var answer = "Yes — it's tested down to 14°F.";

        // The shopper: the FAQ, then the question.
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/store/p/hand-built-rem-pod");
        await WaitForTheCircuitAsync();
        var faq = Page.Locator("[data-testid=product-faq]");
        await Expect(faq).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(faq.Locator("[data-testid=faq-entry]").First).ToContainTextAsync("How long do the batteries last?");

        await ClickUntilAsync(Page.Locator("#ask-open"), Page.Locator("#ask-text"));
        await FillAndConfirmAsync("#ask-text", question);
        await Page.Locator("#ask-send").ClickAsync();
        await Expect(Page.Locator("#ask-text")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(Page.Locator("#ask-refusal")).ToHaveCountAsync(0);

        await Page.GotoAsync($"{BaseUrl}/store/questions");
        await WaitForTheCircuitAsync();
        var mine = Page.Locator("[data-testid=my-question]", new() { HasText = question });
        await Expect(mine).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(mine).ToContainTextAsync("Waiting for an answer");

        // The seller: her inbox has it, with nobody's name on it.
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling/questions");
        await WaitForTheCircuitAsync();
        var waiting = Page.Locator("[data-testid=store-question]", new() { HasText = question });
        await Expect(waiting).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(waiting).Not.ToContainTextAsync("Sarah");
        await ClickUntilAsync(waiting.Locator("[data-testid=question-answer]"), Page.Locator("#question-text"));
        await FillAndConfirmAsync("#question-text", answer);
        await Page.Locator("#question-confirm").ClickAsync();
        await Expect(Page.Locator("[data-testid=store-question]", new() { HasText = question })).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        // The shopper again: the answer.
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/store/questions");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=my-question]", new() { HasText = question }).Locator("[data-testid=my-question-answer]"))
            .ToHaveTextAsync(answer, new() { Timeout = 30_000 });
    }

    [Test]
    [Description("A visitor who isn't signed in sees the FAQ, and is offered sign-in instead of the question box.")]
    public async Task A_visitor_is_offered_sign_in_to_ask()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/hand-built-rem-pod");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=product-faq]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#ask-sign-in")).ToBeVisibleAsync();
        await Expect(Page.Locator("#ask-open")).ToHaveCountAsync(0);
    }
}
