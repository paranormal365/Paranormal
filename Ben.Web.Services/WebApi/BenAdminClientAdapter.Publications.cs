using Ben.Service.Models.Publications;
using Ben.Web.Services;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Publications half of the adapter — implements <see cref="Ben.Web.Services.IBenPublicationClient"/>.
/// </summary>
/// <remarks>
/// <para><b>The public reads go out anonymously on purpose.</b> They use
/// <c>GetAnonymousAsync</c>, which sends no bearer token even when the reader happens to be signed
/// in. That is not an oversight — it is the only way the site's own pages exercise the same request
/// a stranger makes. Send the token and every page works for the author and quietly breaks for the
/// visitor, which is exactly the failure this codebase keeps finding.</para>
///
/// <para>Reads degrade to empty rather than throwing: the API 404s the feature wholesale when
/// <c>features.publications</c> is off, and the pages sit behind a <c>FeatureGate</c> anyway.</para>
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    private static string OrgPublications(Guid organizationId)
        => $"/api/organizations/{organizationId}/publications";

    // ── Authoring ────────────────────────────────────────────────────────────

    public Task<LoadResult<PublicationRecord>> GetOrgPublicationsAsync(
        Guid organizationId, CancellationToken token = default)
            => _api.GetListAsync<PublicationRecord>(OrgPublications(organizationId), token);

    public Task<(PublicationRecord? Publication, string? Error)> CreatePublicationAsync(
        Guid organizationId, SavePublicationRequest request, CancellationToken token = default)
        // The refusal matters here: "that title is already taken" is something the author can act
        // on, and a bare null would read as "something broke".
        => _api.SendExpectingReasonAsync<SavePublicationRequest, PublicationRecord>(
            HttpMethod.Post, OrgPublications(organizationId), request, token);

    public Task<bool> UpdatePublicationAsync(
        Guid organizationId, Guid publicationId, SavePublicationRequest request,
        CancellationToken token = default)
        => _api.PutVoidAsync($"{OrgPublications(organizationId)}/{publicationId}", request, token);

    public Task<(bool Deleted, string? Error)> DeletePublicationAsync(
        Guid organizationId, Guid publicationId, CancellationToken token = default)
        // DeleteExpectingReason, not Delete: "it still has two posts in it" is a rule the person
        // can act on, and the plain helper would flatten it into a false that reads as a breakage.
        => _api.DeleteExpectingReasonAsync(
            $"{OrgPublications(organizationId)}/{publicationId}", token);

    public Task<LoadResult<PublicationPostRecord>> GetOrgPublicationPostsAsync(
        Guid organizationId, Guid publicationId, CancellationToken token = default)
            => _api.GetListAsync<PublicationPostRecord>($"{OrgPublications(organizationId)}/{publicationId}/posts", token);

    public Task<(PublicationPostRecord? Post, string? Error)> CreatePostAsync(
        Guid organizationId, Guid publicationId, SavePublicationPostRequest request,
        CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SavePublicationPostRequest, PublicationPostRecord>(
            HttpMethod.Post, $"{OrgPublications(organizationId)}/{publicationId}/posts", request, token);

    public Task<bool> UpdatePostAsync(
        Guid organizationId, Guid publicationId, Guid postId, SavePublicationPostRequest request,
        CancellationToken token = default)
        => _api.PutVoidAsync(
            $"{OrgPublications(organizationId)}/{publicationId}/posts/{postId}", request, token);

    public Task<bool> SetPostPublishedAsync(
        Guid organizationId, Guid publicationId, Guid postId, bool published,
        CancellationToken token = default)
        => _api.PostVoidAsync(
            $"{OrgPublications(organizationId)}/{publicationId}/posts/{postId}/publish?published={(published ? "true" : "false")}",
            new { }, token);

    public Task<bool> DeletePostAsync(
        Guid organizationId, Guid publicationId, Guid postId, CancellationToken token = default)
        => _api.DeleteAsync($"{OrgPublications(organizationId)}/{publicationId}/posts/{postId}", token);

    // ── Reading, as a visitor ────────────────────────────────────────────────

    public Task<LoadResult<PublicPublicationRecord>> GetPublicPublicationsAsync(
        CancellationToken token = default)
            => _api.GetAnonymousListAsync<PublicPublicationRecord>("/api/public/publications", token);

    public Task<PublicPublicationDetailRecord?> GetPublicPublicationAsync(
        string urlName, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicPublicationDetailRecord>(
            $"/api/public/publications/{Uri.EscapeDataString(urlName)}", token);

    public Task<PublicPublicationPostRecord?> GetPublicPublicationPostAsync(
        string urlName, string postUrlName, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicPublicationPostRecord>(
            $"/api/public/publications/{Uri.EscapeDataString(urlName)}/{Uri.EscapeDataString(postUrlName)}",
            token);

    // ── Subscribing ──────────────────────────────────────────────────────────

    public Task<LoadResult<MySubscriptionRecord>> GetMySubscriptionsAsync(
        CancellationToken token = default)
            => _api.GetListAsync<MySubscriptionRecord>("/api/me/publication-subscriptions", token);

    public async Task<bool> IsSubscribedAsync(string urlName, CancellationToken token = default)
        => await _api.GetAsync<bool?>(
            $"/api/me/publication-subscriptions/{Uri.EscapeDataString(urlName)}", token) ?? false;

    public Task<bool> SubscribeAsync(string urlName, CancellationToken token = default)
        => _api.PostVoidAsync(
            $"/api/me/publication-subscriptions/{Uri.EscapeDataString(urlName)}", new { }, token);

    public Task<bool> UnsubscribeAsync(string urlName, CancellationToken token = default)
        => _api.DeleteAsync($"/api/me/publication-subscriptions/{Uri.EscapeDataString(urlName)}", token);
}
