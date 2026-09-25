using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// An item's history (store sellers, backlog 251, P2): the line each change leaves, and the plain
/// sentences those lines say.
/// </summary>
/// <remarks>
/// <para><b>Added, not saved.</b> <see cref="Record"/> puts the line on the context, so it goes in
/// the same <c>SaveChanges</c> as the edit — a refused edit leaves no line, and a saved one cannot
/// lose its line. Stock is the exception that proves it: the stock helper saves as it goes, so
/// <see cref="AdjustStockAsync"/> wraps the adjustment and its line in one transaction.</para>
///
/// <para><b>Nothing changed, nothing said.</b> A save that altered nothing writes no line, so the
/// history is a list of changes and not a list of button presses.</para>
///
/// <para>EveryProductWriteLeavesHistoryTests reads the item controllers and fails when a write
/// endpoint does not come through here.</para>
/// </remarks>
public static class StoreProductHistory
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>What a seller's history calls the store's staff: never a name.</summary>
    public const string StoreActorName = "The store";

    /// <summary>Adds one line to the context. Nothing is saved here.</summary>
    public static void Record(
        BenDataContext db, Guid productId, StoreProductChangeArea area, string summary,
        Guid? actorAppUserId, StoreChangeActor role, DateTime now)
    {
        var text = summary.Trim();
        if (text.Length == 0) return;
        if (text.Length > StoreProductChange.MaxSummaryLength) text = text[..(StoreProductChange.MaxSummaryLength - 1)] + "…";
        db.StoreProductChanges.Add(new StoreProductChange
        {
            Id = Guid.NewGuid(), ProductId = productId, Area = area, Summary = text,
            ActorAppUserId = actorAppUserId, ActorRole = role, OccurredUtc = now,
        });
    }

    /// <summary>Adds the lines a list of sentences makes, one per area that has any.</summary>
    public static void Record(
        BenDataContext db, Guid productId, IEnumerable<(StoreProductChangeArea Area, string Sentence)> sentences,
        Guid? actorAppUserId, StoreChangeActor role, DateTime now)
    {
        foreach (var area in sentences.GroupBy(s => s.Area))
            Record(db, productId, area.Key, string.Join(" ", area.Select(s => s.Sentence)), actorAppUserId, role, now);
    }

    // ── details ──────────────────────────────────────────────────────────────

    /// <summary>The parts of an item a details save can change, as they stood.</summary>
    public sealed record DetailsSnapshot(
        string Name, string Slug, Guid CategoryId, Guid? EquipmentModelId, string? ShortDescription,
        string? LongDescriptionHtml, bool IsFeatured, DateTime? NewUntilUtc, string? StripeTaxCode, int SortOrder,
        string Specs, Guid? SellerAppUserId)
    {
        public static DetailsSnapshot Of(StoreProduct p, IEnumerable<StoreProductSpec> specs) => new(
            p.Name, p.Slug, p.CategoryId, p.EquipmentModelId, p.ShortDescription, p.LongDescriptionHtml, p.IsFeatured,
            p.NewUntilUtc, p.StripeTaxCode, p.SortOrder,
            string.Join("\n", specs.OrderBy(s => s.SortOrder).Select(s => $"{s.GroupName}\t{s.Name}\t{s.Value}")),
            p.SellerAppUserId);
    }

    /// <summary>
    /// The sentences between two versions of an item's details. <paramref name="names"/> turns the
    /// ids that changed into what a reader knows them by.
    /// </summary>
    public static IReadOnlyList<(StoreProductChangeArea Area, string Sentence)> DescribeDetails(
        DetailsSnapshot before, DetailsSnapshot after, IReadOnlyDictionary<Guid, string> names)
    {
        string Named(Guid id) => names.TryGetValue(id, out var n) ? n : "somebody no longer listed";
        var said = new List<(StoreProductChangeArea, string)>();
        void Say(string s) => said.Add((StoreProductChangeArea.Details, s));

        if (before.Name != after.Name) Say($"Renamed it from “{before.Name}” to “{after.Name}”.");
        if (before.Slug != after.Slug) Say($"Moved its web address to /store/p/{after.Slug}.");
        if (before.CategoryId != after.CategoryId) Say($"Moved it from {Named(before.CategoryId)} to {Named(after.CategoryId)}.");
        if (before.EquipmentModelId != after.EquipmentModelId)
            Say(after.EquipmentModelId is { } model ? $"Linked it to {Named(model)} in the equipment catalogue." : "Unlinked it from the equipment catalogue.");
        if (before.ShortDescription != after.ShortDescription) Say("Changed the short description.");
        if (before.LongDescriptionHtml != after.LongDescriptionHtml) Say("Changed the description.");
        if (before.Specs != after.Specs) Say("Changed the specifications.");
        if (before.IsFeatured != after.IsFeatured) Say(after.IsFeatured ? "Featured it." : "Stopped featuring it.");
        if (before.NewUntilUtc != after.NewUntilUtc)
            Say(after.NewUntilUtc is { } until ? $"Marked it new until {until.ToString("MM/dd/yyyy", Us)}." : "Stopped marking it new.");
        if (before.StripeTaxCode != after.StripeTaxCode)
            Say(after.StripeTaxCode is { } code ? $"Set its tax code to {code}." : "Cleared its tax code.");
        if (before.SortOrder != after.SortOrder) Say($"Moved it to place {after.SortOrder + 1} on its shelf.");

        if (before.SellerAppUserId != after.SellerAppUserId)
            said.Add((StoreProductChangeArea.Seller, (before.SellerAppUserId, after.SellerAppUserId) switch
            {
                (null, { } to) => $"Gave it to {Named(to)} to sell.",
                ({ } from, null) => $"Took it back from {Named(from)}; the store sells it now.",
                ({ } from, { } to) => $"Moved it from {Named(from)} to {Named(to)} to sell.",
                _ => "Changed who sells it.",
            }));
        return said;
    }

    // ── variants ─────────────────────────────────────────────────────────────

    /// <summary>A variant as a save found it.</summary>
    public sealed record VariantSnapshot(
        string Sku, string Label, decimal Price, decimal? CompareAtPrice, bool IsActive, bool IsDefault, string OptionSignature)
    {
        public static VariantSnapshot Of(StoreProductVariant v) => new(
            v.Sku, StorePriceCaches.Label(v.Name), v.Price, v.CompareAtPrice, v.IsActive, v.IsDefault, v.OptionSignature);
    }

    /// <summary>
    /// The sentences between two versions of one variant. Price lines are their own area: the
    /// store's pricing is the store's, and a seller's history leaves them out.
    /// </summary>
    public static IReadOnlyList<(StoreProductChangeArea Area, string Sentence)> DescribeVariant(VariantSnapshot before, VariantSnapshot after)
    {
        var said = new List<(StoreProductChangeArea, string)>();
        var label = before.Label;
        if (before.Sku != after.Sku) said.Add((StoreProductChangeArea.Variants, $"Changed {label}’s SKU from {before.Sku} to {after.Sku}."));
        if (before.OptionSignature != after.OptionSignature) said.Add((StoreProductChangeArea.Variants, $"Changed the choices {label} is made of."));
        if (before.IsActive != after.IsActive) said.Add((StoreProductChangeArea.Variants, after.IsActive ? $"Switched {label} on." : $"Switched {label} off."));
        if (!before.IsDefault && after.IsDefault) said.Add((StoreProductChangeArea.Variants, $"Made {label} the one shown first."));
        if (before.Price != after.Price)
            said.Add((StoreProductChangeArea.Price, $"Changed {label}’s price from {StoreMoney.Format(before.Price)} to {StoreMoney.Format(after.Price)}."));
        if (before.CompareAtPrice != after.CompareAtPrice)
            said.Add((StoreProductChangeArea.Price, after.CompareAtPrice is { } was
                ? $"Showed {label} as reduced from {StoreMoney.Format(was)}."
                : $"Stopped showing {label} as reduced."));
        return said;
    }

    /// <summary>A variant just added: its line, and — the store's alone — its price.</summary>
    public static IReadOnlyList<(StoreProductChangeArea Area, string Sentence)> DescribeNewVariant(StoreProductVariant v, int initialStock)
    {
        var said = new List<(StoreProductChangeArea, string)>
        {
            (StoreProductChangeArea.Variants, initialStock > 0 ? $"Added variant {v.Sku}, with {initialStock} in stock." : $"Added variant {v.Sku}."),
        };
        if (v.Price > 0m) said.Add((StoreProductChangeArea.Price, $"Priced {v.Sku} at {StoreMoney.Format(v.Price)}."));
        if (v.CompareAtPrice is { } was) said.Add((StoreProductChangeArea.Price, $"Showed {v.Sku} as reduced from {StoreMoney.Format(was)}."));
        return said;
    }

    /// <summary>What an option list says, for the line a save of the options leaves.</summary>
    public static string DescribeOptions(IEnumerable<(string Name, IEnumerable<string> Values)> options)
    {
        var parts = options.Select(o => $"{o.Name} ({string.Join(", ", o.Values)})").ToList();
        return parts.Count == 0 ? "Removed every option." : $"Set the options to {string.Join("; ", parts)}.";
    }

    // ── stock ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts stock and leaves one line per item touched, all together or not at all. Returns the
    /// stock helper's refusal, or null.
    /// </summary>
    public static async Task<string?> AdjustStockAsync(
        BenDataContext db, IReadOnlyList<StoreStockChange> changes, StoreStockReason reason, string? note,
        Guid actorAppUserId, StoreChangeActor role, DateTime now, CancellationToken ct = default)
    {
        var ids = changes.Select(c => c.VariantId).ToList();
        var variants = await db.StoreProductVariants.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.ProductId, v.Name, v.StockOnHand }).ToDictionaryAsync(v => v.Id, ct);

        var relational = db.Database.IsRelational();
        await using var tx = relational && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        var refusal = await StoreStock.AdjustManyAsync(db, changes, reason, note, actorAppUserId, now, ct);
        if (refusal is not null) return refusal;

        foreach (var product in changes.Where(c => variants.ContainsKey(c.VariantId)).GroupBy(c => variants[c.VariantId].ProductId))
        {
            var lines = product.Select(c =>
            {
                var v = variants[c.VariantId];
                return DescribeStock(StorePriceCaches.Label(v.Name), c.Delta, c.SetTo, v.StockOnHand, reason);
            }).Where(s => s is not null).ToList();
            if (lines.Count == 0) continue;
            var sentence = string.Join(" ", lines) + (string.IsNullOrWhiteSpace(note) ? "" : $" Note: {note.Trim()}");
            Record(db, product.Key, StoreProductChangeArea.Stock, sentence, actorAppUserId, role, now);
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return null;
    }

    /// <summary>One variant's stock change in words, or null when a count found what was there.</summary>
    public static string? DescribeStock(string label, int? delta, int? setTo, int onHandBefore, StoreStockReason reason)
    {
        if (setTo is { } count)
            return count == onHandBefore ? null : $"Counted {label}: {count} on the shelf (was {onHandBefore}).";
        if (delta is not { } d || d == 0) return null;
        return (reason, d > 0) switch
        {
            (StoreStockReason.Received, true) => $"Received {d} of {label}.",
            (StoreStockReason.Damaged, _) => $"Wrote off {Math.Abs(d)} of {label} as damaged.",
            (_, true) => $"Added {d} to {label} (a correction).",
            _ => $"Took {Math.Abs(d)} off {label} (a correction).",
        };
    }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// An item's history, newest first. For a seller (<paramref name="forSeller"/> set to them):
    /// no price lines, and the store's staff are "The store", not their names.
    /// </summary>
    public static async Task<List<StoreProductChangeRecord>> ReadAsync(
        BenDataContext db, Guid productId, Guid? forSeller, CancellationToken ct = default)
    {
        var query = db.StoreProductChanges.AsNoTracking().Where(c => c.ProductId == productId);
        if (forSeller is not null) query = query.Where(c => c.Area != StoreProductChangeArea.Price);

        var rows = await query
            .OrderByDescending(c => c.OccurredUtc).ThenByDescending(c => c.Id)
            .Select(c => new
            {
                c.Id, c.Area, c.Summary, c.ActorRole, c.ActorAppUserId, c.OccurredUtc,
                Name = c.ActorAppUser == null ? null : c.ActorAppUser.DisplayName ?? c.ActorAppUser.Email,
            })
            .ToListAsync(ct);

        return rows.Select(c => new StoreProductChangeRecord(
            c.Id, c.Area, c.Summary, c.ActorRole,
            forSeller is { } me
                ? c.ActorRole == StoreChangeActor.Store ? StoreActorName : c.ActorAppUserId == me ? "You" : c.Name ?? "A former seller"
                : c.Name ?? "Somebody no longer listed",
            c.OccurredUtc)).ToList();
    }
}
