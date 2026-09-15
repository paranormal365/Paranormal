using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.LinkPreviews;
using Microsoft.EntityFrameworkCore;

namespace Ben.Web.Tests;

/// <summary>
/// Link previews without a network: every address reads as a page with a known title, and every fetch is counted.
/// </summary>
/// <remarks>Given a database, the previews it makes are stored there, so rows that refer to them satisfy their keys.</remarks>
public sealed class FakeLinkPreviews(IDbContextFactory<BenDataContext>? db = null) : ILinkPreviewService
{
    public readonly List<string> Fetched = [];
    private readonly Dictionary<string, StoredLinkPreview> _kept = [];

    public Task<StoredLinkPreview?> FindAsync(string url, CancellationToken ct) =>
        Task.FromResult(_kept.GetValueOrDefault(url));

    public async Task<StoredLinkPreview?> GetOrFetchAsync(string url, Guid userId, bool refresh, CancellationToken ct)
    {
        var known = _kept.GetValueOrDefault(url);
        if (!refresh && known is not null) return known;

        Fetched.Add(url);
        var host = new Uri(url).Host;
        var row = known ?? new StoredLinkPreview { Id = Guid.NewGuid(), Url = url, UrlHash = LinkPreviewService.HashOf(url), Domain = host };
        row.Title = $"Page at {host}";
        row.Fetched = true;
        row.FetchedUtc = DateTime.UtcNow;
        row.ExpiresUtc = DateTime.UtcNow.AddDays(7);

        if (db is not null && known is null)
        {
            await using var context = await db.CreateDbContextAsync(ct);
            context.LinkPreviews.Add(row);
            await context.SaveChangesAsync(ct);
        }

        _kept[url] = row;
        return row;
    }
}
