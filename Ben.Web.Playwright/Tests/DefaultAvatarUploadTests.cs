using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Items 162/163: the default-avatar settings are uploads, and replacing removes the old file.
/// Uses a real PNG through the file input — the one interaction the sandboxed browser pane
/// cannot drive, which is why this waited for the harness.
/// </summary>
[TestFixture]
[Category("DefaultAvatarUpload")]
public class DefaultAvatarUploadTests : BenTestBase
{
    /// <summary>A 1x1 transparent PNG, bytes inline so the test carries its own fixture.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Test]
    public async Task Uploading_a_default_avatar_sets_it_and_shows_the_preview()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        await WaitUntilLoadedAsync();

        var card = Main.Locator(".card", new() { HasText = "Default profile picture — woman" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var hadImage = await card.Locator("img").CountAsync() > 0;

        var tmp = Path.Combine(Path.GetTempPath(), $"e2e-avatar-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tmp, TinyPng);
        try
        {
            await card.Locator("input[type=file]").SetInputFilesAsync(tmp);

            // The proof is the preview appearing (upload + save + refetch all succeeded) and the
            // badge flipping to Set.
            await Expect(card.Locator("img")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(card.Locator(".badge", new() { HasText = "Set" })).ToBeVisibleAsync(new() { Timeout = 15_000 });

            // Replace-removes-old: upload AGAIN and the setting still resolves (the previous
            // file's deletion must not break the new preview).
            await card.Locator("input[type=file]").SetInputFilesAsync(tmp);
            await Expect(card.Locator("img")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }
        finally
        {
            File.Delete(tmp);
            // Leave the shared database as found: clear the woman default unless one existed
            // before this test ran.
            if (!hadImage)
            {
                var clear = card.GetByRole(AriaRole.Button, new() { Name = "Clear" });
                if (await clear.CountAsync() > 0)
                    await ClickUntilAsync(clear, card.Locator(".badge", new() { HasText = "Not set" }));
            }
        }
    }
}
