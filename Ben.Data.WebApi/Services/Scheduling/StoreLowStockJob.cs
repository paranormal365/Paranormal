using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// The morning's low stock (storefront S5.6): one bell and one letter to every SuperAdmin, listing
/// the live variants at or under the store's low-stock number — once a day, only while the store is on.
/// </summary>
/// <remarks>
/// Runs on the shared five-minute timer and decides for itself whether today's note has gone: the bell
/// it sends is its own record ("Store stock: …" today), so a restart or a second instance sends nothing twice.
/// </remarks>
public sealed class StoreLowStockJob(
    IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages, StoreOrderMailer mailer,
    TimeProvider? clock = null) : IScheduledJob
{
    public const string SubjectPrefix = "Store stock: ";

    /// <summary>The note goes after this hour (UTC) — the morning in the US.</summary>
    public const int FromHourUtc = 13;

    public string Name => "store-low-stock";

    public async Task RunAsync(CancellationToken ct)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (now.Hour < FromHourUtc) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        if (!settings.Enabled) return;

        var today = now.Date;
        if (await db.UserMessages.AnyAsync(m => m.MessageSubject.StartsWith(SubjectPrefix) && m.DateCreated >= today, ct)) return;

        var threshold = settings.LowStockThreshold;
        var low = await StoreCatalogue.LiveProducts(db).SelectMany(p => p.Variants)
            .Where(v => v.IsActive && v.StockOnHand - v.StockReserved <= threshold)
            .OrderBy(v => v.StockOnHand - v.StockReserved).ThenBy(v => v.Sku)
            .Select(v => new { Product = v.Product.Name, v.Name, v.Sku, Left = v.StockOnHand - v.StockReserved })
            .ToListAsync(ct);
        if (low.Count == 0) return;

        var admins = await StoreAlerts.SuperAdminIdsAsync(db, ct);
        if (admins.Count == 0) return;

        var lines = string.Join("; ", low.Take(10).Select(l => $"{l.Product}{(l.Name is null ? "" : $" ({l.Name})")} — {Math.Max(0, l.Left)} left"));
        await messages.SendAsync($"{SubjectPrefix}{low.Count} running low",
            $"{lines}{(low.Count > 10 ? $"; and {low.Count - 10} more" : "")}. /admin/store/stock?low=true", admins, admins[0], ct);

        var emails = await db.AppUsers.AsNoTracking().Where(u => admins.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Email, u.DisplayName }).ToListAsync(ct);
        await mailer.QueueLowStockAsync(db, low.Select(l => (l.Product, l.Name, l.Sku, Math.Max(0, l.Left))).ToList(),
            emails.Select(e => (e.Email!, e.DisplayName)).ToList(), ct);
        await db.SaveChangesAsync(ct);
    }
}
