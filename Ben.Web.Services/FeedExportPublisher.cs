using Ben.Service.Models.Feed;
using Ben.Video.Editor.Models;

namespace Ben.Web.Services;

/// <summary>
/// Sends a finished render from the video editor to the PUBLIC FEED — the host side of the
/// editor's "Post to the feed" destination (item 186 F7).
/// </summary>
/// <remarks>
/// <para>The sibling of <see cref="VideoExportPublisher"/>, with the same shape and the same
/// contract: <b>throws on every failure</b>, so the editor's destination prompt stays open and
/// "Save to my machine" stays available. Returning normally is the signal that the render is
/// safely on the server — after which the editor discards the only remaining copy.</para>
///
/// <para>The upload goes through the feed's own multipart door — the same one a browser post
/// uses — so a render is ingested, stripped, screened, and feature-scored exactly like any other
/// upload. There is deliberately no separate editor upload path to keep honest.</para>
/// </remarks>
public sealed class FeedExportPublisher(IBenAdminClient adminClient)
{
    /// <summary>
    /// Posts the render. <paramref name="sourceCaseId"/> carries the case lineage (and its org's
    /// unclaimed attribution) when the editor was opened from a case;
    /// <paramref name="consentToPublishPrivateEngagement"/> is the recorded tick a
    /// private-engagement case requires — the server refuses without it.
    /// </summary>
    public async Task<FeedPostRecord> PostAsync(
        ExportedVideo exported,
        string body,
        Guid? experienceTypeId,
        Guid? sourceCaseId,
        bool consentToPublishPrivateEngagement,
        CancellationToken ct = default)
    {
        var bytes = await exported.ReadBytesAsync()
            ?? throw new InvalidOperationException("Couldn't read the rendered file back from the browser.");

        using var stream = new MemoryStream(bytes);
        var (post, error) = await adminClient.CreatePostAsync(
            string.IsNullOrWhiteSpace(body) ? exported.FileName : body.Trim(),
            token: ct,
            media: stream,
            mediaFileName: exported.FileName,
            mediaContentType: exported.ContentType,
            experienceTypeId: experienceTypeId,
            sourceCaseId: sourceCaseId,
            consentToPublishPrivateEngagement: consentToPublishPrivateEngagement);

        return post ?? throw new InvalidOperationException(
            error ?? "The feed rejected the upload.");
    }
}
