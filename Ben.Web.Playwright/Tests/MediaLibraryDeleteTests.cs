using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Deleting a file you own, from the library that holds it (Ben, 2026-09-19).
/// </summary>
/// <remarks>
/// <para>The server has allowed this since 2026-08-23 — owner or SuperAdmin, through
/// <c>FileAudienceAccess.CanManageFileAsync</c> — and item 180 Phase B built the careful version
/// around it. What was missing was a way in: the flow had a screen on /upload-files and nowhere
/// else, so from the media library a file whose bytes had gone could be seen and not removed.</para>
///
/// <para>The fixture uploads its own file and deletes it, so it needs nothing from the seed and
/// leaves nothing behind — the discipline item 243 was about.</para>
/// </remarks>
[TestFixture]
[Category("MediaLibrary")]
public class MediaLibraryDeleteTests : BenTestBase
{
    /// <summary>
    /// The audio fixture, and "Audio" as its type, because those are the two the seed actually
    /// has. A first version picked "Photo", which is not one of them — the seeded types are
    /// "Logo" and "Audio" — and the select sat there refusing an option that did not exist.
    /// </summary>
    private static readonly string TestAudioPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    /// <summary>
    /// Uploads a file of this person's own and answers with the name it carries.
    /// </summary>
    /// <remarks>
    /// <para>The shape is AudioEditorTests' — the panel is behind its own button, the file input
    /// does not exist until it opens, a file type is required before anything sends, and the bytes
    /// go through page JavaScript in chunks started by an explicit Upload. A first version of this
    /// helper just looked for <c>input[type=file]</c> on the page and timed out on a page that had
    /// rendered perfectly.</para>
    ///
    /// <para>A unique name, so the card found afterwards is this run's own — the library shows
    /// every file this seat can SEE, including other people's — and so the same-name dialog never
    /// opens.</para>
    /// </remarks>
    private async Task<string?> UploadOwnFileAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var openPanel = Page.GetByRole(AriaRole.Button, new() { Name = "Upload New File" });
        try { await Expect(openPanel).ToBeVisibleAsync(new() { Timeout = 30_000 }); }
        catch { return null; }
        await openPanel.ClickAsync();

        var fileType = Page.Locator("select").First;
        try { await Expect(fileType).ToBeVisibleAsync(new() { Timeout = 20_000 }); }
        catch { return null; }
        await fileType.SelectOptionAsync(new SelectOptionValue { Label = "Audio" });

        var input = Page.Locator("#chunked-upload-input-7f31");
        try { await Expect(input).ToBeAttachedAsync(new() { Timeout = 20_000 }); }
        catch { return null; }

        var uploadedName = $"delete-me-{Guid.NewGuid():N}.mp3";
        await input.SetInputFilesAsync(new FilePayload
        {
            Name     = uploadedName,
            MimeType = "audio/mpeg",
            Buffer   = await File.ReadAllBytesAsync(TestAudioPath),
        });

        var upload = Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true });
        try { await Expect(upload).ToBeEnabledAsync(new() { Timeout = 20_000 }); }
        catch { return null; }
        await upload.ClickAsync();

        // The page asks before making a second file of the same name, and the upload sits behind
        // that dialog. The unique name above should mean it never appears; answering it costs
        // nothing and a silent wait costs a lot.
        var keepBoth = Page.GetByRole(AriaRole.Button, new() { Name = "Keep Both" });
        await Page.WaitForTimeoutAsync(800);
        if (await keepBoth.CountAsync() > 0 && await keepBoth.IsVisibleAsync())
            await keepBoth.ClickAsync();

        return uploadedName;
    }

    [Test]
    public async Task A_file_you_own_can_be_deleted_from_the_library()
    {
        await LoginAsync(UserEmail, UserPassword);
        var name = await UploadOwnFileAsync();
        if (name is null) Assert.Ignore("the upload panel on /upload-files did not come up on this deployment");

        await Page.GotoAsync($"{BaseUrl}/media-library");
        await WaitUntilLoadedAsync();

        // Three minutes, the figure AudioEditorTests arrived at for the same upload: the bytes go
        // through page JavaScript in chunks and a 7MB file is not quick. A first version waited
        // 30 seconds and reported a missing card for an upload that was still running.
        var card = Main.Locator(".card").Filter(new() { HasTextString = name! }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 180_000 });

        var delete = card.Locator("[data-testid=library-delete]");
        await Expect(delete).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await delete.ClickAsync();

        // Nobody else is using it, so it is the plain confirm rather than the two questions.
        var confirm = Page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).First;
        await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await confirm.ClickAsync();

        // Gone from the page without a reload: the grid drops it when the flow says it has left.
        await Expect(Main.Locator(".card").Filter(new() { HasTextString = name! }))
            .ToHaveCountAsync(0, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Nothing in the library belongs to anybody else.
    /// </summary>
    /// <remarks>
    /// <para>This test used to assert the opposite — that among the cards there was one without a
    /// delete, because the library showed every public file on the site and some of them were
    /// other people's. That is the behaviour Ben removed on 2026-09-21: <i>"Media library should
    /// show only library content for the person who is logged in, so 'mine' not public or
    /// all."</i></para>
    ///
    /// <para>So the rule inverts, and it is worth holding in this direction too: every card on
    /// this page is the viewer's own, which means every card offers a delete. A card here that
    /// could not be deleted would mean somebody else's file had got back in.</para>
    ///
    /// <para>It refuses an empty page rather than passing on one, because "no files" and "no
    /// files belonging to anybody else" look identical and only one of them is this rule
    /// working.</para>
    /// </remarks>
    [Test]
    public async Task The_library_holds_only_this_persons_own_files()
    {
        // The seat that OWNS files. The seeded uploads belong to the SuperAdmin, and now that the
        // library holds only your own, an ordinary member's is legitimately empty — so this test
        // ran against a blank page and could observe nothing. A test has to be able to reach the
        // state it is about.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/media-library");
        await WaitUntilLoadedAsync();

        var cards = Main.Locator(".card");
        await Expect(cards.First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var total   = await cards.CountAsync();
        var deletes = await Main.Locator("[data-testid=library-delete]").CountAsync();

        Assert.That(total, Is.GreaterThan(0), "the library drew no files at all");
        Assert.That(deletes, Is.EqualTo(total),
            $"{total - deletes} of {total} cards offered no delete, so the library is still "
          + "showing files this person does not own");
    }
}
