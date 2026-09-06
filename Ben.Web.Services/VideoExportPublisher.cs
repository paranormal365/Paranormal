using Ben.Service.Models.Entities;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Web.Services;

/// <summary>
/// Sends a finished render from the video editor up to the server — the host side of
/// Ben.Video.Editor's <c>VideoEditor.OnPublishExport</c> callback.
///
/// <para>Lives here rather than in each page because all three editor hosts
/// (<c>CaseVideoEditorPage</c>, <c>MyVideosPage</c>, <c>VideoEditorPage</c>) need the identical
/// two-step, and the interesting half of it is easy to get subtly wrong: the publish endpoint
/// (<c>POST /api/video-projects/{id}/publish</c>) attaches the video to an <i>existing</i> project
/// row and 404s without one, so a user who rendered without ever saving to the server has nothing
/// to publish against. This saves the project first in that case, then publishes to what it just
/// created.</para>
///
/// <para><b>Throws on every failure.</b> That is the contract <c>OnPublishExport</c> asks for: the
/// editor's destination prompt catches it, stays open, and keeps "Save to my machine" available.
/// Returning normally tells the editor the video is safely on the server, at which point it
/// discards the only remaining copy — so a swallowed error here loses the user's render.</para>
/// </summary>
public sealed class VideoExportPublisher(
    IBenAdminClient adminClient,
    ProjectService projects,
    ProjectStore projectStore,
    IVideoUploadRelay? uploadRelay = null)
{
    /// <summary>
    /// Publishes <paramref name="exported"/> against the project currently open in the editor,
    /// creating the server-side project row first if there isn't one yet.
    /// </summary>
    /// <param name="caseId">Case to file a newly-created project under, or null for a personal one.</param>
    /// <param name="knownProjectId">
    /// A server project id the caller already established this session — passed back in via
    /// <see cref="PublishResult.ProjectId"/> so a second export in the same session updates the
    /// same project instead of piling up a new row per render.
    /// </param>
    public async Task<PublishResult> PublishAsync(
        ExportedVideo exported, Guid? caseId, Guid? knownProjectId, CancellationToken ct = default)
    {
        // The open project first, the session's own id second. It used to be the other way round,
        // so a stale id from an earlier publish outranked the project actually on screen — and a
        // later export attached itself to whichever project had been published first that session
        // (2026-09-05 audit, site-4).
        //
        // CurrentServerId is the server's row, now a separate field from the browser's own storage
        // key; it is null for a project that has only ever existed in this browser.
        var projectId = projectStore.CurrentServerId ?? knownProjectId;

        if (projectId is null)
        {
            var file  = projects.BuildCurrentProjectFile(projectStore.CurrentProjectName);
            var saved = await adminClient.SaveMyVideoProjectAsync(file, caseId, ct)
                ?? throw new InvalidOperationException(
                    "Couldn't save the project to the server, so there was nowhere to attach the video.");
            projectId = saved.Id;
        }

        // Remembered on the store, so the next publish in this session updates the same project
        // rather than depending on the caller having threaded the id back through.
        projectStore.CurrentServerId = projectId;

        // The browser posts the file itself when it can. Reading it here means returning it over
        // the circuit as a JS-interop value, which Blazor caps at 32 KB by default — so this path
        // could not publish a real render at all (2026-09-05 audit, site-1).
        if (uploadRelay is not null && exported.BlobUrl is { } blobUrl)
        {
            var problem = await uploadRelay.PublishAsync(
                projectId.Value, blobUrl, exported.FileName, exported.ContentType, ct);

            if (problem is not null) throw new InvalidOperationException(problem);

            // Read back rather than parsing a shape the relay never inspected: the publish
            // response travels through the browser, and the record is what the caller shows.
            var record = await adminClient.GetMyVideoProjectAsync(projectId.Value, ct)
                ?? throw new InvalidOperationException(
                    "The video uploaded, but the project could not be read back.");

            return new PublishResult(projectId.Value, record);
        }

        // No relay registered, or no blob URL: fall back to the bytes. Correct for anything small
        // and for a host with no JS of its own, and the reason a missing relay degrades rather
        // than breaks.
        var bytes = await exported.ReadBytesAsync()
            ?? throw new InvalidOperationException("Couldn't read the rendered file back from the browser.");

        var published = await adminClient.PublishVideoProjectAsync(
            projectId.Value, bytes, exported.FileName, exported.ContentType, ct)
            ?? throw new InvalidOperationException("The server rejected the upload.");

        return new PublishResult(projectId.Value, published);
    }

    public sealed record PublishResult(Guid ProjectId, VideoProjectRecord Record);
}
