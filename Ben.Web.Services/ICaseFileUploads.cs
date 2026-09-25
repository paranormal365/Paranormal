namespace Ben.Web.Services;

/// <summary>
/// Where the browser may post a file so it lands on a case.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: "use the telerik upload component to show progress while uploading in
/// order to confirm the upload instead of it not working at all." The Files tab used to read the
/// file through the Blazor circuit, which carries it in SignalR messages: no progress, no way to
/// cancel, and an 8 MB recording arriving as a long silence that is indistinguishable from a
/// failure. An upload component posts straight to a URL instead — which means there has to be a
/// URL, and it must not be the API's, because the browser has no access token and must never be
/// given one.</para>
/// <para>So the site mints a short-lived ticket bound to this one case and hands back a URL on its
/// own origin. The endpoint behind it redeems the ticket, and streams the body to the API under the
/// person's own token — the same trust shape the video editor's publish relay uses, for the same
/// reason. Implemented by the host, because only the host has the ticket service and the token.</para>
/// </remarks>
public interface ICaseFileUploads
{
    /// <summary>
    /// A URL the browser may post one file to, or null when nobody is signed in.
    /// </summary>
    /// <remarks>
    /// Bound to the case: a ticket minted for one case cannot be replayed against another, because
    /// the endpoint it unlocks names that case in its own path.
    /// </remarks>
    string? SaveUrlFor(Guid organizationId, Guid caseId);
}
