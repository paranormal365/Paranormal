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
    ProjectStore projectStore)
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
        // ProjectStore.CurrentProjectId is the SERVER id whenever the project was opened from the
        // server (LoadFromFileAsync stores it); it's null for a project that only ever existed in
        // this browser. knownProjectId covers the in-between case — created by an earlier publish
        // in this same session, so ProjectStore doesn't know about it either.
        var projectId = knownProjectId ?? projectStore.CurrentProjectId;

        if (projectId is null)
        {
            var file  = projects.BuildCurrentProjectFile(projectStore.CurrentProjectName);
            var saved = await adminClient.SaveMyVideoProjectAsync(file, caseId, ct)
                ?? throw new InvalidOperationException(
                    "Couldn't save the project to the server, so there was nowhere to attach the video.");
            projectId = saved.Id;
        }

        // The one place the render actually lands on the .NET heap — deliberately not read until
        // we know we have somewhere to put it (see ExportedVideo's own remarks).
        var bytes = await exported.ReadBytesAsync()
            ?? throw new InvalidOperationException("Couldn't read the rendered file back from the browser.");

        var published = await adminClient.PublishVideoProjectAsync(
            projectId.Value, bytes, exported.FileName, exported.ContentType, ct)
            ?? throw new InvalidOperationException("The server rejected the upload.");

        return new PublishResult(projectId.Value, published);
    }

    public sealed record PublishResult(Guid ProjectId, VideoProjectRecord Record);
}
