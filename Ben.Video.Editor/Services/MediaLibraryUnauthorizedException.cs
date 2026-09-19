namespace Ben.Video.Editor.Services;

/// <summary>
/// The media library refused the request because of who is asking.
/// </summary>
/// <remarks>
/// <para>A refusal used to be indistinguishable from an empty library. On the site the provider
/// returned an empty list for any failed response, so a signed-out or expired session showed the
/// Server tab as "no files" — which reads as "you have not uploaded anything" rather than "sign in
/// again" (2026-09-05 audit, site-11). On the standalone host the raw HTTP exception reached the
/// panel and was shown as its message, which is not much better (F11).</para>
///
/// <para>A distinct type so the panel can offer the one thing that helps, rather than describing a
/// state that is not true.</para>
/// </remarks>
public sealed class MediaLibraryUnauthorizedException()
    : Exception("Your session is not signed in, or it has expired, so the media library could not be read.");
