using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Fills in link cards: a pasted or edited address becomes a Twitter/X-style card when a preview can be made,
/// and keeps its site and address when it cannot (R34).
/// </summary>
/// <remarks>
/// <para>The card appears at once with its address, and the preview arrives afterwards as a change that is not
/// an undo step - so pasting a link is still one Ctrl+Z.</para>
///
/// <para>Cards that only got their host (signed out, offline, the page refused) are asked again at most once a
/// day, and only when the board is opened by somebody signed in. At most ten are asked at once, well inside the
/// API's thirty a minute.</para>
///
/// <para>A view-only board is left as it is: filling in a card would be a change to a board the person may not
/// change.</para>
/// </remarks>
public sealed class LinkPreviewResolver(CanvasStore store, ILinkPreviewProvider previews, BoardAccess access, ICanvasSignInState? signIn = null, ILogger<LinkPreviewResolver>? log = null)
{
    /// <summary>How often a card that only has its host is asked again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(1);

    private const int AtOnce = 10;

    private readonly HashSet<Guid> _asking = [];
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Resolves every card that has never been asked; with <paramref name="includeStale"/>, also day-old host-only cards.</summary>
    /// <returns>How many cards changed.</returns>
    public async Task<int> ResolveAsync(bool includeStale = false, CancellationToken ct = default)
    {
        if (!access.CanEdit) return 0;

        var now = UtcNow();
        var signedIn = signIn?.IsSignedIn ?? false;
        var due = store.Document.Nodes
            .Where(n => n.Data is LinkData link && !string.IsNullOrWhiteSpace(link.Url) && !_asking.Contains(n.Id)
                && (link.Tier == LinkPreviewTier.None
                    || includeStale && signedIn && link.Tier == LinkPreviewTier.HostOnly
                        && (link.FetchedAtUtc is not { } at || now - at >= RetryAfter)))
            .Select(n => (n.Id, Url: ((LinkData)n.Data).Url))
            .ToList();
        if (due.Count == 0) return 0;

        foreach (var (id, _) in due) _asking.Add(id);
        using var gate = new SemaphoreSlim(AtOnce);
        var changed = 0;
        try
        {
            await Task.WhenAll(due.Select(async item =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var card = await previews.GetAsync(item.Url, ct);
                    if (Apply(item.Id, item.Url, card, now)) Interlocked.Increment(ref changed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "A link card could not be resolved.");
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        finally
        {
            foreach (var (id, _) in due) _asking.Remove(id);
        }

        return changed;
    }

    /// <summary>Writes the card onto the block, unless the block was removed or its address changed meanwhile.</summary>
    private bool Apply(Guid nodeId, string askedUrl, LinkPreviewCard card, DateTime now)
    {
        if (store.FindNode(nodeId) is not { Data: LinkData current } || current.Url != askedUrl) return false;

        return store.SetNodeDataWithoutHistory(nodeId, data =>
        {
            if (data is not LinkData link) return;
            link.Title = card.Title;
            link.Description = card.Description;
            link.SiteName = card.SiteName;
            link.ImageSourceUrl = card.ImageSourceUrl;
            link.ImageUrl = card.ImageUrl;
            link.Tier = card.Tier;
            link.FetchedAtUtc = now;
        });
    }
}
