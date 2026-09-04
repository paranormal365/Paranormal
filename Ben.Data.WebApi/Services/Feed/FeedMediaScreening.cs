using Ben.Data.Common.Enums;

namespace Ben.Data.WebApi.Services.Feed;

/// <summary>What a screener decided about one file, and why.</summary>
/// <param name="State">Where the media should sit. Never <see cref="FeedMediaReviewState.Pending"/>
/// from a screener that actually looked — Pending means "nobody has decided yet".</param>
/// <param name="Reason">
/// A short note for the review queue: what the screener objected to, or how confident it was.
/// Shown to moderators, never to the poster — a person told exactly which classifier tripped is a
/// person told exactly how to dress the next upload.
/// </param>
/// <param name="Score">
/// The classifier's NSFW probability, 0–1, when a classifier decided; null when a person will,
/// or when the screener has no number to give. Stored on the post so the spam rule (item 217)
/// can count confident refusals per author without parsing the note.
/// </param>
public readonly record struct FeedMediaVerdict(FeedMediaReviewState State, string? Reason, double? Score = null);

/// <summary>
/// Decides whether a photo or video posted to the feed may be shown (item 186 F5).
/// </summary>
/// <remarks>
/// <para>An interface with a swappable implementation because the two halves of this problem have
/// very different shapes. Deciding "is this pornography" is a solved, buyable capability that
/// wants a trained model; deciding "is this really an EVP recording" is a research problem we are
/// starting a feedback loop for (F6). Screening had to be able to improve without the feed's
/// upload path being rewritten each time.</para>
///
/// <para><b>The contract is fail-closed.</b> A screener that throws, times out, or cannot make up
/// its mind leaves media where it started — Pending — and Pending never serves. Nothing here can
/// cause an unscreened file to be shown; the worst a broken screener can do is leave a queue for
/// a person to work through.</para>
/// </remarks>
public interface IFeedMediaScreener
{
    /// <summary>
    /// Looks at a stored file and says where its post's media should sit.
    /// </summary>
    /// <param name="storagePath">The file as stored — the storage-root-RELATIVE path recorded on
    /// <c>UploadFile.StoragePath</c>, readable only through <c>IFileStorageService</c>, never
    /// directly from disk (the first live screener decoded it as a filesystem path and reported
    /// every healthy photo undecodable). The ORIGINAL, not the sanitized copy: what matters here
    /// is what the image shows, and stripping location data changes nothing about that while
    /// re-encoding could soften exactly the detail a classifier needs.</param>
    /// <param name="contentType">Its type, so a screener can tell a photo from a video.</param>
    /// <param name="ct">Cancellation.</param>
    Task<FeedMediaVerdict> ScreenAsync(string storagePath, string? contentType, CancellationToken ct);

    /// <summary>
    /// Whether this screener actually examines content, as opposed to routing everything to a
    /// person.
    /// </summary>
    /// <remarks>
    /// Read by the site-administration screens so an operator can see, without reading the source,
    /// whether automatic screening is running — and by the dark-launch reminder, which should not
    /// suggest turning the feed on while every upload depends on somebody noticing a queue.
    /// </remarks>
    bool IsAutomatic { get; }
}

/// <summary>
/// The screener that ships today: it approves nothing by itself and sends every file to a person.
/// </summary>
/// <remarks>
/// <para><b>Deliberately not a stub that approves.</b> The obvious placeholder — wave everything
/// through until the real classifier arrives — is the one implementation that could put the site
/// in the state Ben asked to avoid, and it would do it silently. Leaving media Pending means the
/// worst case is a photo that has not appeared yet, which somebody will notice and fix, rather
/// than one that has appeared and cannot be recalled.</para>
///
/// <para>This is a working arrangement, not a placeholder with the lights off: a moderator sees
/// everything awaiting review and clears it. What it is not is <i>scalable</i> — which is what the
/// automatic screener is for, and why <see cref="IsAutomatic"/> reports false so the product can
/// say so honestly.</para>
/// </remarks>
public sealed class ManualReviewScreener : IFeedMediaScreener
{
    public bool IsAutomatic => false;

    public Task<FeedMediaVerdict> ScreenAsync(string storagePath, string? contentType, CancellationToken ct)
        => Task.FromResult(new FeedMediaVerdict(
            FeedMediaReviewState.Pending,
            "Waiting for a moderator — automatic screening is not configured on this site."));
}
