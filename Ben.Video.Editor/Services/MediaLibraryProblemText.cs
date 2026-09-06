namespace Ben.Video.Editor.Services;

/// <summary>
/// Turns a failure to list somebody's uploaded media into a sentence they can act on.
/// </summary>
/// <remarks>
/// <para>The Server tab used to print whatever the exception said. Signed out that was a raw HTTP
/// message, which this phase replaced with a sign-in offer. What it did not replace was the other
/// common failure: with the site unreachable the tab reads <c>TypeError: Failed to fetch</c>, which
/// is the browser's word for a connection that did not happen and tells the reader nothing about
/// what to do (2026-09-05 audit, F11 and site-11, found on screen).</para>
///
/// <para>Refusals do not come here. A 401 is a state with an action attached and is handled as one;
/// this is for everything that is genuinely a fault.</para>
///
/// <para>Pure, so the wording can be checked without a server to fail against.</para>
/// </remarks>
public static class MediaLibraryProblemText
{
    /// <summary>What to show in place of <paramref name="ex"/>.</summary>
    public static string Describe(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex switch
        {
            // On WebAssembly a connection that never happened arrives as an HttpRequestException
            // wrapping a JavaScript TypeError, so the message is the browser's, not the server's.
            HttpRequestException =>
                "Could not reach the server. Check your connection, then try again.",

            TaskCanceledException or TimeoutException =>
                "The server took too long to answer. Try again in a moment.",

            _ => $"Your uploaded media could not be listed. {ex.Message}",
        };
    }
}
