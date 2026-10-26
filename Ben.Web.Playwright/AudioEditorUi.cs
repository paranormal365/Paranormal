using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Ben.Web.Playwright;

/// <summary>
/// How the tests reach the audio editor's controls since phase 2 (10/26/2026) regrouped them.
/// </summary>
/// <remarks>
/// <para>The editor's panels are tabs under the player: EVP Markers, Clips, Listen, Display, Edit.
/// The toolbar keeps the everyday buttons; the rest are in its View, Listen and Edit menus, which
/// Telerik draws at the end of the page, outside the editor.</para>
///
/// <para>One place for these, so the next regrouping changes one file rather than every test that
/// opens a panel. Tests used to click buttons named "Edit", "EQ / Filters", "Show Spectrogram" and
/// "Mel Scale"; the first of those is now a menu, and the others are gone.</para>
/// </remarks>
public static class AudioEditorUi
{
    /// <summary>Opens one of the panels under the player, by its tab's name.</summary>
    public static async Task OpenTabAsync(ILocator editor, string tab)
    {
        var button = editor.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = false }).First;
        await Expect(button).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await button.ClickAsync();
        await Expect(button).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
    }

    /// <summary>Chooses an item from the toolbar's View, Listen or Edit menu.</summary>
    /// <param name="menu">"view", "listen" or "edit": the menu button's id is audio-menu-{menu}.</param>
    /// <param name="item">The item's text, or the start of it.</param>
    public static async Task ChooseAsync(IPage page, string menu, string item)
    {
        await page.Locator($"#audio-menu-{menu}").ClickAsync();
        var choice = page.Locator(".k-menu-popup, .k-popup").GetByText(item, new() { Exact = false }).First;
        await Expect(choice).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await choice.ClickAsync();
    }

    /// <summary>The toolbar's Spectrogram toggle.</summary>
    public static ILocator SpectrogramToggle(ILocator editor) =>
        editor.Locator("#audio-editor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Spectrogram", Exact = true });

    /// <summary>Turns the spectrogram on or off, whichever it is now.</summary>
    public static async Task SetSpectrogramAsync(ILocator editor, bool on)
    {
        var toggle = SpectrogramToggle(editor);
        await Expect(toggle).ToBeEnabledAsync(new() { Timeout = 30_000 });
        if (await toggle.GetAttributeAsync("aria-pressed") != (on ? "true" : "false"))
            await toggle.ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-pressed", on ? "true" : "false", new() { Timeout = 10_000 });
    }
}
