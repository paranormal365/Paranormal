using Ben.Data.Common.Helpers;

namespace Ben.Data.WebApi.Services.LinkPreviews;

/// <summary>Makes the preview cards for the links in something just posted, without making the poster wait.</summary>
public interface ILinkPreviewWarmer
{
    /// <summary>
    /// Starts fetching previews for up to <see cref="LinkPreviewWarmer.MaxLinksPerPost"/> web links found in
    /// <paramref name="text"/> (plain) and <paramref name="html"/> (link targets), and returns at once.
    /// </summary>
    void WarmFrom(string? text, string? html, Guid userId);
}

/// <summary>
/// The posting side of link previews: after a group message, case message or feed post is saved, its links' cards are
/// made in the background, so the post never waits on somebody else's server and the card is ready when a reader opens
/// it (beta feedback, 2026-09-14). The iPhone app gets its cards this way; the website also asks while composing.
/// </summary>
/// <remarks>
/// Fire-and-forget through its own scope, the same shape as the metadata extraction after an upload. A failure is logged
/// and nothing else: a missing card falls back to the link's host, which is what every card showed before this.
/// </remarks>
public sealed class LinkPreviewWarmer(IServiceScopeFactory scopes, ILogger<LinkPreviewWarmer> log) : ILinkPreviewWarmer
{
    public const int MaxLinksPerPost = 3;

    /// <summary>For code that posts where no previews are wanted — and for tests of posting that are not about them.</summary>
    public static ILinkPreviewWarmer None { get; } = new NoWarmer();

    public void WarmFrom(string? text, string? html, Guid userId)
    {
        var links = LinksIn(text, html);
        if (links.Count == 0 || userId == Guid.Empty) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var previews = scope.ServiceProvider.GetRequiredService<ILinkPreviewService>();
                foreach (var link in links)
                    await previews.GetOrFetchAsync(link, userId, refresh: false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Link previews for a new post could not be made.");
            }
        });
    }

    /// <summary>The distinct web links in a post, first ones first, at most <see cref="MaxLinksPerPost"/>.</summary>
    public static IReadOnlyList<string> LinksIn(string? text, string? html) => PostLinks.In(text, html, MaxLinksPerPost);

    private sealed class NoWarmer : ILinkPreviewWarmer
    {
        public void WarmFrom(string? text, string? html, Guid userId) { }
    }
}
