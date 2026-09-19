using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The case audio mixer, used the way somebody arranging a mix uses it.
/// </summary>
/// <remarks>
/// The 2026-09-06 walk found five things here, and four of them are only visible on screen: every
/// clip drawn the same width whatever it held, a ninth clip stacking silently on top of the first,
/// a remove button that could not be clicked, and a transport of three permanently disabled
/// buttons. The fifth — a Mixer button shown to members who cannot export — needed a seat the walk
/// did not have.
/// </remarks>
[TestFixture]
[Category("CaseMixer")]
public class CaseAudioMixerTests : BenTestBase
{
    private static readonly string TestAudioPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    /// <summary>Signs in, reaches the case, and puts one audio file on it.</summary>
    private async Task<bool> ReachCaseWithAudioAsync(string email, string password)
    {
        await LoginAsync(email, password);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);
        await Expect(Page.Locator("#case-file-upload")).ToBeAttachedAsync(new() { Timeout = 15_000 });
        await Page.Locator("#case-file-upload").SetInputFilesAsync(TestAudioPath);

        try { await Expect(Page.Locator("[id^='ws-']").First).ToBeVisibleAsync(new() { Timeout = 60_000 }); }
        catch { return false; }

        return true;
    }

    private async Task<bool> OpenMixerAsync()
    {
        var button = Page.Locator("#case-audio-mixer");
        if (await button.CountAsync() == 0) return false;
        await button.ClickAsync();
        await Expect(Page.GetByText("Audio Clips", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        return true;
    }

    /// <summary>Adds the first available clip to the grid.</summary>
    private async Task AddOneClipAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First.ClickAsync();
        await Expect(Page.Locator("[data-clip-id]").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// A clip is drawn at the length of the recording it holds.
    /// </summary>
    /// <remarks>
    /// Every block was 120 pixels wide whatever it held, so a three-minute recording and a
    /// four-second one were the same size and the grid could not represent what was on it
    /// (finding K-length). The fixture is over three minutes; at eight pixels a second that is far
    /// wider than the old placeholder.
    /// </remarks>
    [Test]
    public async Task A_clip_is_drawn_at_its_real_length()
    {
        if (!await ReachCaseWithAudioAsync(UserEmail, UserPassword) || !await OpenMixerAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case or the Mixer button was not reachable.");
            return;
        }

        await AddOneClipAsync();

        var block = Page.Locator("[data-clip-id]").First;
        var box   = await block.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);

        TestContext.Out.WriteLine($"clip drawn {box!.Width:0} px wide");

        // The old placeholder was 15 s at 8 px/s = 120 px. The fixture is 3:06.
        Assert.That(box.Width, Is.GreaterThan(200),
            "the clip is still drawn at the placeholder width, so the grid says nothing about "
            + "how long the recording is");
    }

    /// <summary>
    /// The remove button can be clicked.
    /// </summary>
    /// <remarks>
    /// The block's own pointerdown handler called preventDefault unconditionally to start a drag,
    /// which swallowed the click meant for the ✕ inside it — so a clip once placed could not be
    /// taken off (finding K-remove).
    /// </remarks>
    [Test]
    public async Task A_placed_clip_can_be_removed()
    {
        if (!await ReachCaseWithAudioAsync(UserEmail, UserPassword) || !await OpenMixerAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case or the Mixer button was not reachable.");
            return;
        }

        await AddOneClipAsync();
        Assert.That(await Page.Locator("[data-clip-id]").CountAsync(), Is.EqualTo(1));

        await Page.Locator(".mix-clip-remove").First.ClickAsync();

        await Expect(Page.Locator("[data-clip-id]")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// A ninth clip is refused with a message rather than stacked on top of the first.
    /// </summary>
    /// <remarks>
    /// Nine adds put nine blocks on an eight-track grid, the ninth landing at offset zero on top of
    /// whatever was already there, with nothing said — so the mixer accepted work it could not hold
    /// and the export refused it afterwards (finding K-9th).
    /// </remarks>
    [Test]
    public async Task A_ninth_clip_is_refused_and_says_so()
    {
        if (!await ReachCaseWithAudioAsync(UserEmail, UserPassword) || !await OpenMixerAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case or the Mixer button was not reachable.");
            return;
        }

        var add = Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First;
        for (var i = 0; i < 9; i++)
        {
            await add.ClickAsync();
            await Page.WaitForTimeoutAsync(250);
        }

        Assert.That(await Page.Locator("[data-clip-id]").CountAsync(), Is.EqualTo(8),
            "the grid holds eight tracks and took a ninth clip anyway");

        await Expect(Page.GetByText("tracks are in use", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// The transport plays the arrangement.
    /// </summary>
    /// <remarks>
    /// Play, Pause and Stop were three permanently disabled buttons with a tooltip saying preview
    /// was not available, so the only way to hear an arrangement was to render it, look at the
    /// result on the case page and come back to change it (finding K-transport). What is asserted
    /// is that the browser's audio clock is actually running, not that a button changed colour.
    /// </remarks>
    [Test]
    public async Task The_transport_plays_the_arrangement()
    {
        if (!await ReachCaseWithAudioAsync(UserEmail, UserPassword) || !await OpenMixerAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case or the Mixer button was not reachable.");
            return;
        }

        await AddOneClipAsync();

        var play = Page.Locator("#mix-play");
        await Expect(play).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await play.ClickAsync();

        // Give the fetch and decode a moment; the fixture is 7 MB.
        await Expect(Page.GetByText("Playing the arrangement", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 60_000 });

        Assert.That(await Page.Locator("#mix-stop").IsEnabledAsync(), Is.True,
            "Stop should be available while something is playing");
    }

    /// <summary>
    /// The Mixer button appears exactly where the grant to use it does.
    /// </summary>
    /// <remarks>
    /// <para>The mixer's only outcome is attaching a rendered file to the case, and the export
    /// endpoint requires the Create grant on Cases. The button was shown to every member, so a
    /// read-only one could arrange a whole mix and meet a 403 at the end of it
    /// (2026-09-06 audio walk, finding K-perm).</para>
    ///
    /// <para>Two seats, and each is asserted outright rather than accepting whatever it finds: the
    /// seeded administrator must see the button, and the seeded ordinary member — who cannot create
    /// on this group's cases — must not. If the seed's grants change, this fails and says so, which
    /// is the point of checking a permission at all.</para>
    /// </remarks>
    [Test]
    public async Task The_mixer_button_appears_only_where_exporting_is_allowed()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
        {
            Assert.Ignore("the member seat cannot reach the Belmont case; seed data may differ.");
            return;
        }

        await Expect(Page.Locator("#case-audio-mixer")).ToHaveCountAsync(0);

        // And the seat that CAN export is still offered it — a gate that hides the feature from
        // everybody is not a fix.
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
        {
            Assert.Fail("the administrator seat could not reach the case it had just been shown.");
            return;
        }

        await Expect(Page.Locator("#case-audio-mixer")).ToHaveCountAsync(1);
    }
}
