using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A server the editor cannot reach says so in words.
/// </summary>
/// <remarks>
/// Found by opening the Server tab with the site down: it read "TypeError: Failed to fetch", the
/// browser's phrase for a connection that never happened (2026-09-05 audit, F11 and site-11).
/// </remarks>
public sealed class MediaLibraryProblemTextTests
{
    [Fact]
    public void An_unreachable_server_is_described_not_quoted()
    {
        var text = MediaLibraryProblemText.Describe(
            new HttpRequestException("TypeError: Failed to fetch"));

        Assert.DoesNotContain("TypeError", text);
        Assert.Contains("could not reach", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_server_that_never_answered_says_to_try_again()
    {
        var text = MediaLibraryProblemText.Describe(new TaskCanceledException());

        Assert.Contains("too long", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Anything unrecognised still shows what went wrong — hiding it would trade one useless
    /// message for no message at all — but says what failed first.
    /// </summary>
    [Fact]
    public void An_unfamiliar_failure_keeps_its_detail_behind_a_plain_sentence()
    {
        var text = MediaLibraryProblemText.Describe(new InvalidOperationException("boom"));

        Assert.StartsWith("Your uploaded media could not be listed.", text);
        Assert.Contains("boom", text);
    }
}
