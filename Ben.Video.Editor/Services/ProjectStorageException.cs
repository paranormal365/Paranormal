namespace Ben.Video.Editor.Services;

/// <summary>
/// The browser refused to store something.
/// </summary>
/// <remarks>
/// <para>The JavaScript side has always reported this honestly: <c>setItem</c> returns false when
/// the quota is exhausted or storage is denied outright, which is what happens in a private window
/// or with third-party storage blocked. The C# side called it through <c>InvokeVoidAsync</c> and
/// threw the answer away, so a save that had not saved anything went on to report "Project saved."
/// — and the work was gone at the next reload (2026-09-05 audit, persistence-9).</para>
///
/// <para>A distinct type rather than a bare exception so the editor can tell "storage said no",
/// which is worth explaining to somebody, from an ordinary bug, which is not.</para>
/// </remarks>
public sealed class ProjectStorageException(string message) : Exception(message)
{
    /// <summary>The message shown when a write is refused.</summary>
    /// <remarks>
    /// Names the two things that actually cause it, because both have something the person can do
    /// about them, and neither is obvious from "save failed".
    /// </remarks>
    public static ProjectStorageException WriteRefused(string what) =>
        new($"The browser would not store {what}. This usually means its storage is full, or that "
          + "storage is blocked — a private window blocks it. Save the project to a file, or to "
          + "the server, so the work is not lost.");
}
