using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What a buyer may download from an order (store sellers, backlog 251, P11): the files for buyers
/// of every product on it — once it is paid, while it isn't cancelled, and not for a product whose
/// line was wholly refunded. Downloads otherwise last for ever (the plan's default).
/// </summary>
public static class StoreOrderDownloads
{
    public static async Task<List<StoreOrderDownloadRecord>> ForOrderAsync(BenDataContext db, StoreOrder order, CancellationToken ct)
    {
        if (order.PaidUtc is null || order.Status == StoreOrderStatus.Cancelled) return [];
        var kept = order.Items.Where(i => i.QuantityRefunded < i.Quantity).GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First().ProductName);
        if (kept.Count == 0) return [];
        var ids = kept.Keys.ToList();
        var files = await db.StoreProductFiles.AsNoTracking()
            .Where(f => ids.Contains(f.ProductId) && f.Audience == StoreFileAudience.Buyers)
            .OrderBy(f => f.ProductId).ThenBy(f => f.SortOrder).ToListAsync(ct);
        return files.Select(f => new StoreOrderDownloadRecord(f.Id, f.ProductId, kept[f.ProductId], f.Title, f.Kind, f.VersionLabel,
            f.FileName, f.SizeBytes, f.ManualHtml is not null)).ToList();
    }
}
