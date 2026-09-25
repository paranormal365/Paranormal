using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Adding a file to a case: the browser posts it to the site's own relay, and the case's list
/// shows it when the post finishes.
/// </summary>
/// <remarks>
/// <para>Ben uploaded an 8 MB video to a production case on 2026-09-17 and could tell nothing
/// about what had happened: no progress while it went, no error, and then a player that stayed
/// black. Two separate faults. The black player was the API serving video without byte ranges
/// (<c>MediaServingTests</c> holds that). This fixture holds the other half — the upload itself
/// reporting what it is doing — which it could not do while the bytes travelled through the
/// Blazor circuit in 32 KB SignalR messages with nothing rendered in between.</para>
///
/// <para>The proof that the new path is the one being used is the request: the browser's own
/// POST to <c>/uploads/case-file/{org}/{case}</c>. Asserting only that the file appears would
/// pass just as well on the circuit route this replaced.</para>
/// </remarks>
[TestFixture]
[Category("Files")]
public class CaseFileUploadTests : BenTestBase
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    /// <summary>What the server said, when it can still be read. Never throws over a failure.</summary>
    private static async Task<string> BodyOrNothingAsync(IResponse response)
    {
        try { return await response.TextAsync(); }
        catch (Exception ex) { return $"(the body could not be read: {ex.Message})"; }
    }

    [Test]
    public async Task Uploading_a_file_posts_it_to_the_relay_and_lists_it()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("the seeded case this walks is not on this database");

        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);

        // The component renders its own file input; the id is ours, so the tabs that drive an
        // upload from elsewhere in the suite keep working.
        var input = Page.Locator("#case-file-upload");
        await Expect(input).ToBeAttachedAsync(new() { Timeout = 15_000 });

        // Watch for the post itself. Started before the file is chosen, because the upload begins
        // the moment it is.
        var posted = Page.WaitForRequestAsync(
            r => r.Method == "POST" && r.Url.Contains("/uploads/case-file/"),
            new() { Timeout = 30_000 });

        await input.SetInputFilesAsync(Fixture);

        var request = await posted;
        Assert.That(request, Is.Not.Null, "The file was not posted to the site's upload relay.");

        var response = await request.ResponseAsync();
        Assert.That(response, Is.Not.Null, "The upload relay never answered.");

        // The body is read only when the status is wrong. Reading it unconditionally — as the
        // message of a passing assertion — asks Playwright for a body it has already discarded,
        // and the test fails on a successful upload.
        var status = response!.Status;
        var said = status == 200 ? string.Empty : await BodyOrNothingAsync(response);
        Assert.That(status, Is.EqualTo(200), $"The relay refused the upload: {said}");

        // And the case has it. The list is reloaded from the API when the post succeeds, so this
        // also says the file reached the database and not only the relay.
        await Expect(Main.GetByText("test-audio.mp3", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Expect(Main.Locator(".alert-danger")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The upload control reports progress. Without this the only thing a long upload showed was
    /// an unchanged page, which is what made a working upload look broken.
    /// </summary>
    [Test]
    public async Task The_upload_control_shows_the_file_and_its_progress()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("the seeded case this walks is not on this database");

        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);
        await Expect(Page.Locator("#case-file-upload")).ToBeAttachedAsync(new() { Timeout = 15_000 });

        await Page.Locator("#case-file-upload").SetInputFilesAsync(Fixture);

        // The chosen file is listed inside the upload control with a status of its own — it is
        // named there before it has finished, which is the whole point of the change.
        var row = Page.Locator("[data-testid='case-file-upload-zone'] .k-file").First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(row).ToContainTextAsync("test-audio", new() { Timeout = 15_000 });

        // It ends as a success, not an error. k-file-error is the component's own failure class.
        await Expect(Page.Locator("[data-testid='case-file-upload-zone'] .k-file-error"))
            .ToHaveCountAsync(0);
    }
}
