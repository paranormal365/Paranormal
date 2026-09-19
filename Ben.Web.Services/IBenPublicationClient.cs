using Ben.Web.Services.WebApi;
using Ben.Service.Models.Publications;

namespace Ben.Web.Services;

/// <summary>
/// The Publications slice of <see cref="IBenAdminClient"/> — authoring, public reading, subscribing.
/// </summary>
/// <remarks>
/// <para>The two halves are deliberately separate methods rather than one set with a flag. The
/// authoring calls need a signed-in author with permission on the group; the public calls need
/// nobody at all, and a visitor with no account uses them. Sharing a method between the two is how
/// a draft ends up on a public page.</para>
///
/// <para>Every read degrades to empty or null rather than throwing, because the API 404s the whole
/// feature when <c>features.publications</c> is off. The hosting pages sit behind a
/// <c>FeatureGate</c> regardless.</para>
/// </remarks>
public interface IBenPublicationClient
{
    // ── Authoring (permission-gated, org-scoped) ─────────────────────────────

    /// <summary>The group's publications, including ones it has not made public.</summary>
    Task<LoadResult<PublicationRecord>> GetOrgPublicationsAsync(
        Guid organizationId, CancellationToken token = default);

    /// <summary>
    /// Creates a publication. Returns it, or the server's refusal — most often that the title
    /// produced a URL name already in use somewhere on the site.
    /// </summary>
    Task<(PublicationRecord? Publication, string? Error)> CreatePublicationAsync(
        Guid organizationId, SavePublicationRequest request, CancellationToken token = default);

    /// <summary>Retitles a publication or changes whether it is public. The URL name never moves.</summary>
    Task<bool> UpdatePublicationAsync(
        Guid organizationId, Guid publicationId, SavePublicationRequest request,
        CancellationToken token = default);

    /// <summary>
    /// Deletes a publication, returning the server's refusal when it will not.
    /// </summary>
    /// <remarks>
    /// A group administrator may delete an empty publication; a SuperAdmin may delete any. The
    /// refusal says which of posts or subscribers is in the way, because that is the half the
    /// person can do something about.
    /// </remarks>
    Task<(bool Deleted, string? Error)> DeletePublicationAsync(
        Guid organizationId, Guid publicationId, CancellationToken token = default);

    /// <summary>Every post in a publication, drafts included, newest first.</summary>
    Task<LoadResult<PublicationPostRecord>> GetOrgPublicationPostsAsync(
        Guid organizationId, Guid publicationId, CancellationToken token = default);

    /// <summary>Writes a new post. It is a draft until published — creating one publishes nothing.</summary>
    Task<(PublicationPostRecord? Post, string? Error)> CreatePostAsync(
        Guid organizationId, Guid publicationId, SavePublicationPostRequest request,
        CancellationToken token = default);

    /// <summary>Edits a post. Editing a published post changes what is already being read.</summary>
    Task<bool> UpdatePostAsync(
        Guid organizationId, Guid publicationId, Guid postId, SavePublicationPostRequest request,
        CancellationToken token = default);

    /// <summary>Publishes or withdraws a post. Publishing again after withdrawing sets a new date.</summary>
    Task<bool> SetPostPublishedAsync(
        Guid organizationId, Guid publicationId, Guid postId, bool published,
        CancellationToken token = default);

    /// <summary>Deletes a post outright.</summary>
    Task<bool> DeletePostAsync(
        Guid organizationId, Guid publicationId, Guid postId, CancellationToken token = default);

    // ── Reading (anonymous) ──────────────────────────────────────────────────

    /// <summary>The public directory: every public publication that has published something.</summary>
    Task<LoadResult<PublicPublicationRecord>> GetPublicPublicationsAsync(
        CancellationToken token = default);

    /// <summary>One publication and its published posts. Null when there is nothing public to show.</summary>
    Task<PublicPublicationDetailRecord?> GetPublicPublicationAsync(
        string urlName, CancellationToken token = default);

    /// <summary>
    /// One post. <c>BodyHtml</c> is null and <c>RequiresSubscription</c> true when the reader does
    /// not hold the tier — the body is withheld by the server, not hidden by the page.
    /// </summary>
    Task<PublicPublicationPostRecord?> GetPublicPublicationPostAsync(
        string urlName, string postUrlName, CancellationToken token = default);

    // ── Subscribing (signed in) ──────────────────────────────────────────────

    /// <summary>What the caller subscribes to.</summary>
    Task<LoadResult<MySubscriptionRecord>> GetMySubscriptionsAsync(CancellationToken token = default);

    /// <summary>Whether the caller subscribes to one publication.</summary>
    Task<bool> IsSubscribedAsync(string urlName, CancellationToken token = default);

    /// <summary>Subscribes. Idempotent, and revives a cancelled subscription rather than adding one.</summary>
    Task<bool> SubscribeAsync(string urlName, CancellationToken token = default);

    /// <summary>Unsubscribes. Idempotent.</summary>
    Task<bool> UnsubscribeAsync(string urlName, CancellationToken token = default);
}

/// <summary>A publication and its published posts, as a visitor receives them together.</summary>
/// <remarks>
/// Mirrors the API's own shape. One request rather than two because the page needs both halves to
/// render anything at all, and a title with no posts under it is a half-drawn page.
/// </remarks>
public sealed record PublicPublicationDetailRecord(
    PublicPublicationRecord Publication,
    IReadOnlyList<PublicPublicationPostRecord> Posts);
