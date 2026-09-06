using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Saving a project to the server, however this host reaches it.
/// </summary>
/// <remarks>
/// <para>The editor used to post the project itself, over a named HttpClient. That worked on the
/// standalone host, where the browser holds its own bearer token — and never worked on the site,
/// where the token lives in the circuit and a message handler registered at the root cannot reach
/// it. So the site's Save to Server button, and the prompt offered after every export, both
/// answered 401 (2026-09-05 audit, F13).</para>
///
/// <para>An interface because the two hosts genuinely reach the server differently, and pretending
/// otherwise is what produced a button that could not work on one of them. The site implements it
/// over the client it already authenticates; the standalone host implements it over HTTP.</para>
/// </remarks>
public interface IProjectServerStore
{
    /// <summary>Whether saving to the server is possible at all right now.</summary>
    /// <remarks>
    /// What the toolbar's cloud button and the post-export prompt should be gated on. Offering a
    /// destination that cannot work is worse than not offering it.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Saves <paramref name="file"/>, updating <paramref name="existingId"/> when there is one.
    /// </summary>
    /// <returns>
    /// The server's id for the project, or null with a reason.
    /// </returns>
    /// <remarks>
    /// Updating rather than always creating is the point of <paramref name="existingId"/>. Every
    /// save used to create a new row, so a project saved five times became five projects and the
    /// list filled with copies of the same thing under the same name (2026-09-05 audit,
    /// persistence-13).
    /// </remarks>
    Task<(Guid? Id, string? Problem)> SaveAsync(
        ProjectFile file, Guid? existingId, Guid? caseId = null, CancellationToken ct = default);
}
