using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Who is asking for a cart: an account, a browser, or both (storefront S3.2).
/// </summary>
/// <param name="GuestToken">
/// The 43-character token from the <c>ben.cart</c> cookie, forwarded by the website as
/// <c>X-Ben-Cart</c>. Anything else — a different length, other characters — is dropped by
/// <see cref="From"/>, so a made-up header is a visitor with no cart, not a new row.
/// </param>
public sealed partial record StoreCartCaller(Guid? UserId, string? GuestToken)
{
    public const int TokenLength = StoreCartRules.TokenLength;

    public static StoreCartCaller From(Guid? userId, string? header)
        => new(userId is { } id && id != Guid.Empty ? id : null, IsWellFormed(header) ? header : null);

    public static bool IsWellFormed(string? token) => token is { Length: TokenLength } && TokenShape().IsMatch(token);

    public bool IsNobody => UserId is null && GuestToken is null;

    /// <summary>The database keeps only this: the token's SHA-256, as 64 hex characters.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenShape();
}

/// <summary>How a cart change went, for the controller to turn into a status.</summary>
public enum StoreCartOutcome { Ok, BadRequest, NotFound, Conflict }

/// <param name="View">The cart after the change — present for Ok and Conflict (a 409 still draws the cart).</param>
/// <param name="Sentence">The refusal for BadRequest and NotFound.</param>
public sealed record StoreCartWrite(StoreCartOutcome Outcome, StoreCartView? View, string? Sentence)
{
    public static StoreCartWrite Refused(StoreCartOutcome outcome, string sentence) => new(outcome, null, sentence);
}

/// <summary>
/// The server-side cart (storefront S3.2): finding it, merging a guest's into an account's, and
/// every change a shopper can make.
/// </summary>
/// <remarks>
/// <para><b>Prices are never stored.</b> Every look re-reads the variant, so a price change or a
/// hidden shelf shows at once; a line that can no longer be bought says so in one of three
/// sentences and blocks checkout, but stays in the cart for the buyer to see and remove.</para>
///
/// <para><b>Sellable is one predicate</b>: the variant, its product and its category all active —
/// the same rule as <see cref="StoreCatalogue.LiveProducts"/>. A line whose shelf was hidden after
/// it was added is not left buyable through the variant flag alone.</para>
///
/// <para><b>No row until a write validates.</b> Reads never create a cart, and a write checks the
/// variant and the quantity before it creates one — a script sending made-up tokens fills nothing.</para>
///
/// <para><b>The merge lives here</b>, not in a sign-in hook, because Microsoft and Apple sign-ins
/// never pass through the local login page: whenever an account and a browser token arrive together
/// and the browser still has a cart of its own, that cart is folded into the account's and deleted.
/// The delete comes first and its row count referees, so two requests merging at once add the
/// guest's lines once.</para>
/// </remarks>
public sealed class StoreCartService(BenDataContext db, TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    // ── Finding the cart ─────────────────────────────────────────────────────

    /// <summary>
    /// The caller's cart, merging a guest cart into the account's first; null when there is none
    /// and <paramref name="create"/> is false.
    /// </summary>
    public async Task<StoreCart?> ResolveAsync(StoreCartCaller caller, bool create, CancellationToken ct = default)
    {
        if (caller.IsNobody) return null;
        var hash = caller.GuestToken is { } token ? StoreCartCaller.Hash(token) : null;

        if (caller.UserId is { } userId && hash is not null)
            await MergeAsync(userId, hash, ct);

        var cart = caller.UserId is { } owner
            ? await db.StoreCarts.FirstOrDefaultAsync(c => c.AppUserId == owner, ct)
            : await db.StoreCarts.FirstOrDefaultAsync(c => c.GuestTokenHash == hash && c.AppUserId == null, ct);
        if (cart is not null || !create) return cart;

        cart = new StoreCart
        {
            Id = Guid.NewGuid(),
            AppUserId = caller.UserId,
            GuestTokenHash = caller.UserId is null ? hash : null,
            LastActivityUtc = Now,
            DateCreated = Now,
        };
        db.StoreCarts.Add(cart);
        try
        {
            await db.SaveChangesAsync(ct);
            return cart;
        }
        catch (DbUpdateException)
        {
            // Two first writes at once: the other one made the cart. Use it.
            db.Entry(cart).State = EntityState.Detached;
            return caller.UserId is { } again
                ? await db.StoreCarts.FirstOrDefaultAsync(c => c.AppUserId == again, ct)
                : await db.StoreCarts.FirstOrDefaultAsync(c => c.GuestTokenHash == hash && c.AppUserId == null, ct);
        }
    }

    /// <summary>
    /// Folds this browser's cart into the account's. An account with no cart simply adopts the
    /// browser's; otherwise the browser's lines are added (capped at 100 a line), its code is kept
    /// when the account has none, and the browser's cart is deleted.
    /// </summary>
    private async Task MergeAsync(Guid userId, string hash, CancellationToken ct)
    {
        var guest = await db.StoreCarts.AsNoTracking()
            .Where(c => c.GuestTokenHash == hash && c.AppUserId == null)
            .Select(c => new { c.Id, c.CouponId, Items = c.Items.Select(i => new { i.VariantId, i.Quantity }).ToList() })
            .FirstOrDefaultAsync(ct);
        if (guest is null) return;

        var mine = await db.StoreCarts.AsNoTracking().Where(c => c.AppUserId == userId)
            .Select(c => new { c.Id, c.CouponId }).FirstOrDefaultAsync(ct);

        if (mine is null)
        {
            // Adopt: one conditional update. If another request adopted it first, or made the
            // account a cart meanwhile (the unique index refuses a second), the loser does nothing.
            try
            {
                await db.StoreCarts
                    .Where(c => c.Id == guest.Id && c.AppUserId == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.AppUserId, userId)
                        .SetProperty(c => c.GuestTokenHash, (string?)null)
                        .SetProperty(c => c.LastActivityUtc, Now), ct);
                return;
            }
            catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
            {
                // ExecuteUpdate throws the provider's exception, not DbUpdateException. The
                // account has a cart now; fall through and merge into it.
                mine = await db.StoreCarts.AsNoTracking().Where(c => c.AppUserId == userId)
                    .Select(c => new { c.Id, c.CouponId }).FirstOrDefaultAsync(ct);
                if (mine is null) return;
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The referee: whoever deletes the guest cart does the adding. Its lines were read above,
        // so the cascade taking them with it loses nothing.
        if (await db.StoreCarts.Where(c => c.Id == guest.Id && c.AppUserId == null).ExecuteDeleteAsync(ct) == 0)
            return;   // merged by another request; the transaction rolls back with nothing done

        var existing = await db.StoreCartItems.Where(i => i.CartId == mine.Id).ToDictionaryAsync(i => i.VariantId, ct);
        foreach (var line in guest.Items)
        {
            if (existing.TryGetValue(line.VariantId, out var item))
            {
                item.Quantity = Math.Min(StoreCartRules.MaxQuantityPerLine, item.Quantity + line.Quantity);
                item.DateUpdated = Now;
            }
            else
            {
                db.StoreCartItems.Add(new StoreCartItem
                {
                    Id = Guid.NewGuid(), CartId = mine.Id, VariantId = line.VariantId,
                    Quantity = Math.Min(StoreCartRules.MaxQuantityPerLine, line.Quantity), DateCreated = Now,
                });
            }
        }
        await db.SaveChangesAsync(ct);

        if (mine.CouponId is null && guest.CouponId is { } code)
            await db.StoreCarts.Where(c => c.Id == mine.Id && c.CouponId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CouponId, code), ct);
        await db.StoreCarts.Where(c => c.Id == mine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityUtc, Now), ct);

        await tx.CommitAsync(ct);
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    public async Task<StoreCartView> ViewAsync(StoreCartCaller caller, CancellationToken ct = default)
        => await ViewAsync(await ResolveAsync(caller, create: false, ct), caller.UserId, notice: null, ct);

    public async Task<int> CountAsync(StoreCartCaller caller, CancellationToken ct = default)
    {
        var cart = await ResolveAsync(caller, create: false, ct);
        return cart is null ? 0 : await db.StoreCartItems.Where(i => i.CartId == cart.Id).SumAsync(i => (int?)i.Quantity, ct) ?? 0;
    }

    private sealed record LineRow(
        StoreCartItem Item, StoreProductVariant Variant, StoreProduct Product, bool Sellable);

    /// <summary>The whole cart, priced now, with the one set of money rows every surface shows.</summary>
    private async Task<StoreCartView> ViewAsync(StoreCart? cart, Guid? userId, string? notice, CancellationToken ct)
    {
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var support = settings.SupportEmail ?? await SiteSettingsService.GetAsync(db, SiteSettingKeys.PublicContactEmail, ct);

        var rows = cart is null
            ? []
            : await db.StoreCartItems.AsNoTracking()
                .Where(i => i.CartId == cart.Id)
                .OrderBy(i => i.DateCreated).ThenBy(i => i.Id)
                .Select(i => new LineRow(i, i.Variant, i.Variant.Product,
                    i.Variant.IsActive && i.Variant.Product.IsActive && i.Variant.Product.Category.IsActive))
                .ToListAsync(ct);

        var held = cart is null ? new Dictionary<Guid, int>() : await HeldByOwnCheckoutAsync(cart.Id, ct);
        var pictures = await PicturesAsync(rows.Select(r => r.Product.Id).Distinct().ToList(), ct);

        var lines = rows.Select(r =>
        {
            var available = Math.Max(0, r.Variant.StockOnHand - r.Variant.StockReserved) + held.GetValueOrDefault(r.Variant.Id);
            var problem = !r.Sellable ? StoreCartSentences.NoLongerAvailable
                : available <= 0 ? StoreCartSentences.SoldOut
                : r.Item.Quantity > available ? StoreCartSentences.OnlyLeft(available)
                : null;
            var price = r.Variant.Price;
            var was = r.Variant.CompareAtPrice is { } c && c > price ? c : (decimal?)null;
            return new StoreCartLineView(
                r.Variant.Id, r.Product.Id, r.Product.Name, r.Variant.Name, r.Variant.Sku, r.Product.Slug,
                PictureFor(pictures, r.Product.Id, r.Variant.Id), price, was, r.Item.Quantity, available,
                StoreMoney.Round(price * r.Item.Quantity),
                was is { } old ? StoreMoney.Round((old - price) * r.Item.Quantity) : null,
                problem is null, problem);
        }).ToList();

        var subtotal = lines.Sum(l => l.LineTotal);

        string? couponCode = null, couponProblem = null;
        var discount = 0m;
        if (cart?.CouponId is { } couponId && await db.StoreCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == couponId, ct) is { } coupon)
        {
            couponCode = coupon.Code;
            // At the cart only the account is known; the per-email count waits for checkout.
            var prior = userId is { } u
                ? await StoreCouponReservations.PriorRedemptionsAsync(db, coupon.Id, emailNormalized: "\0", u, ct: ct)
                : 0;
            couponProblem = lines.Count == 0 ? null : StoreCouponMath.WhyNotRedeemable(coupon, subtotal, prior, Now);
            if (couponProblem is null) discount = StoreCouponMath.DiscountFor(coupon, subtotal);
        }

        decimal? shipping = null;
        var free = false;
        if (lines.Count > 0)
        {
            var afterDiscount = subtotal - discount;
            free = settings.ShippingFlatRate <= 0m
                || (settings.FreeShippingThreshold > 0m && afterDiscount >= settings.FreeShippingThreshold);
            shipping = free ? 0m : settings.ShippingFlatRate;
        }

        var hasProblem = lines.Any(l => !l.IsPurchasable);
        var why = lines.Count == 0 ? null
            : !settings.CheckoutEnabled ? StoreCartSentences.CheckoutPaused
            : hasProblem ? StoreCartSentences.FixTheLines
            : null;

        return new StoreCartView(
            cart?.Id, lines, lines.Sum(l => l.Quantity), subtotal, couponCode, discount, couponProblem,
            shipping, free, settings.FreeShippingThreshold, settings.LowStockThreshold, support,
            subtotal - discount + (shipping ?? 0m), settings.CheckoutEnabled,
            CanCheckout: lines.Count > 0 && why is null, why, notice);
    }

    /// <summary>
    /// What this cart's own unpaid checkout is holding, per variant. That stock is the buyer's:
    /// without crediting it back, the last unit would read "Sold out" to the person holding it.
    /// </summary>
    private async Task<Dictionary<Guid, int>> HeldByOwnCheckoutAsync(Guid cartId, CancellationToken ct)
        => await db.StoreOrderItems.AsNoTracking()
            .Where(i => i.Order.StoreCartId == cartId
                     && i.Order.Status == StoreOrderStatus.PendingPayment
                     && i.Order.ReservationReleasedUtc == null)
            .GroupBy(i => i.VariantId)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(g => g.Key, g => g.Quantity, ct);

    private sealed record Picture(Guid ProductId, Guid? VariantId, Guid UploadFileId, int SortOrder);

    private async Task<List<Picture>> PicturesAsync(List<Guid> productIds, CancellationToken ct)
        => productIds.Count == 0 ? [] : await db.StoreProductImages.AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId))
            .Select(i => new Picture(i.ProductId, i.VariantId, i.UploadFileId, i.SortOrder))
            .ToListAsync(ct);

    /// <summary>The variant's own picture when it has one, else the product's first.</summary>
    private static Guid? PictureFor(List<Picture> pictures, Guid productId, Guid variantId)
        => (pictures.Where(p => p.VariantId == variantId).OrderBy(p => p.SortOrder).FirstOrDefault()
            ?? pictures.Where(p => p.ProductId == productId && p.VariantId == null).OrderBy(p => p.SortOrder).FirstOrDefault()
            ?? pictures.Where(p => p.ProductId == productId).OrderBy(p => p.SortOrder).FirstOrDefault())?.UploadFileId;

    // ── Changing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="quantity"/> of a variant. Asking for more than can be bought adds what
    /// can be and answers Conflict with "Only n left."; nothing left at all adds nothing.
    /// </summary>
    public async Task<StoreCartWrite> AddAsync(StoreCartCaller caller, Guid variantId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.ChooseHowMany);
        if (caller.IsNobody) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.NoCart);

        var variant = await SellableVariantAsync(variantId, ct);
        if (variant is null) return StoreCartWrite.Refused(StoreCartOutcome.NotFound, StoreCartSentences.NoLongerAvailable);

        // Validate before any row exists: a refused add must not leave an empty cart behind.
        var existingCart = await ResolveAsync(caller, create: false, ct);
        var inCart = existingCart is null ? 0
            : await db.StoreCartItems.Where(i => i.CartId == existingCart.Id && i.VariantId == variantId)
                .Select(i => (int?)i.Quantity).FirstOrDefaultAsync(ct) ?? 0;
        var held = existingCart is null ? 0 : (await HeldByOwnCheckoutAsync(existingCart.Id, ct)).GetValueOrDefault(variantId);
        var available = Math.Max(0, variant.StockOnHand - variant.StockReserved) + held;

        var wanted = inCart + quantity;
        var allowed = Math.Min(Math.Min(wanted, available), StoreCartRules.MaxQuantityPerLine);
        var notice = allowed >= wanted ? null
            : available <= 0 ? StoreCartSentences.SoldOut
            : available <= StoreCartRules.MaxQuantityPerLine && allowed == available ? StoreCartSentences.OnlyLeft(available)
            : StoreCartSentences.AtMost(StoreCartRules.MaxQuantityPerLine);

        if (inCart == 0 && allowed > 0 && existingCart is not null
            && await db.StoreCartItems.CountAsync(i => i.CartId == existingCart.Id, ct) >= StoreCartRules.MaxDistinctLines)
            return new StoreCartWrite(StoreCartOutcome.Conflict,
                await ViewAsync(existingCart, caller.UserId, StoreCartSentences.TooManyLines, ct), StoreCartSentences.TooManyLines);

        if (allowed <= inCart)
        {
            // Nothing more can be added. Say so over the cart as it stands; create nothing.
            return new StoreCartWrite(StoreCartOutcome.Conflict,
                await ViewAsync(existingCart, caller.UserId, notice, ct), notice);
        }

        var cart = existingCart ?? await ResolveAsync(caller, create: true, ct);
        if (cart is null) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.NoCart);
        await SetLineAsync(cart.Id, variantId, allowed, ct);
        await TouchAsync(cart.Id, ct);

        var view = await ViewAsync(cart, caller.UserId, notice, ct);
        return new StoreCartWrite(notice is null ? StoreCartOutcome.Ok : StoreCartOutcome.Conflict, view, notice);
    }

    /// <summary>Sets a line's quantity; 0 removes it. More than can be bought is capped, with Conflict.</summary>
    public async Task<StoreCartWrite> SetQuantityAsync(StoreCartCaller caller, Guid variantId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 0) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.ChooseHowMany);
        if (quantity == 0) return await RemoveAsync(caller, variantId, ct);
        if (caller.IsNobody) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.NoCart);

        var cart = await ResolveAsync(caller, create: false, ct);
        var inCart = cart is null ? null
            : await db.StoreCartItems.Where(i => i.CartId == cart.Id && i.VariantId == variantId)
                .Select(i => (int?)i.Quantity).FirstOrDefaultAsync(ct);
        if (cart is null || inCart is null) return StoreCartWrite.Refused(StoreCartOutcome.NotFound, StoreCartSentences.NotInCart);

        var variant = await SellableVariantAsync(variantId, ct);
        if (variant is null)
            return new StoreCartWrite(StoreCartOutcome.Conflict,
                await ViewAsync(cart, caller.UserId, StoreCartSentences.NoLongerAvailable, ct), StoreCartSentences.NoLongerAvailable);

        var held = (await HeldByOwnCheckoutAsync(cart.Id, ct)).GetValueOrDefault(variantId);
        var available = Math.Max(0, variant.StockOnHand - variant.StockReserved) + held;
        var allowed = Math.Min(Math.Min(quantity, available), StoreCartRules.MaxQuantityPerLine);
        var notice = allowed >= quantity ? null
            : available <= 0 ? StoreCartSentences.SoldOut
            : available <= StoreCartRules.MaxQuantityPerLine && allowed == available ? StoreCartSentences.OnlyLeft(available)
            : StoreCartSentences.AtMost(StoreCartRules.MaxQuantityPerLine);

        // Sold out entirely: leave the line as it is, marked, for the buyer to remove.
        if (allowed > 0) await SetLineAsync(cart.Id, variantId, allowed, ct);
        await TouchAsync(cart.Id, ct);

        var view = await ViewAsync(cart, caller.UserId, notice, ct);
        return new StoreCartWrite(notice is null ? StoreCartOutcome.Ok : StoreCartOutcome.Conflict, view, notice);
    }

    public async Task<StoreCartWrite> RemoveAsync(StoreCartCaller caller, Guid variantId, CancellationToken ct = default)
    {
        if (caller.IsNobody) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.NoCart);
        var cart = await ResolveAsync(caller, create: false, ct);
        if (cart is null
            || await db.StoreCartItems.Where(i => i.CartId == cart.Id && i.VariantId == variantId).ExecuteDeleteAsync(ct) == 0)
            return StoreCartWrite.Refused(StoreCartOutcome.NotFound, StoreCartSentences.NotInCart);

        await TouchAsync(cart.Id, ct);
        return new StoreCartWrite(StoreCartOutcome.Ok, await ViewAsync(cart, caller.UserId, null, ct), null);
    }

    /// <summary>
    /// Puts a discount code on the cart. A code that cannot be used on this cart now is refused
    /// with the buyer's sentence and not applied.
    /// </summary>
    public async Task<StoreCartWrite> ApplyCouponAsync(StoreCartCaller caller, string? code, CancellationToken ct = default)
    {
        var clean = CouponCodeGenerator.Normalise(code);
        if (clean.Length == 0) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.EnterACode);
        if (caller.IsNobody) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.NoCart);

        var cart = await ResolveAsync(caller, create: false, ct);
        if (cart is null || !await db.StoreCartItems.AnyAsync(i => i.CartId == cart.Id, ct))
            return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.AddSomethingFirst);

        var coupon = await db.StoreCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == clean, ct);
        var before = await ViewAsync(cart, caller.UserId, null, ct);
        var prior = coupon is not null && caller.UserId is { } u
            ? await StoreCouponReservations.PriorRedemptionsAsync(db, coupon.Id, emailNormalized: "\0", u, ct: ct)
            : 0;
        if (StoreCouponMath.WhyNotRedeemable(coupon, before.Subtotal, prior, Now) is { } why)
            return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, why);

        await db.StoreCarts.Where(c => c.Id == cart.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CouponId, coupon!.Id).SetProperty(c => c.LastActivityUtc, Now), ct);
        cart.CouponId = coupon!.Id;
        return new StoreCartWrite(StoreCartOutcome.Ok, await ViewAsync(cart, caller.UserId, null, ct), null);
    }

    public async Task<StoreCartWrite> ClearCouponAsync(StoreCartCaller caller, CancellationToken ct = default)
    {
        if (caller.IsNobody) return StoreCartWrite.Refused(StoreCartOutcome.BadRequest, StoreCartSentences.NoCart);
        var cart = await ResolveAsync(caller, create: false, ct);
        if (cart?.CouponId is null) return StoreCartWrite.Refused(StoreCartOutcome.NotFound, StoreCartSentences.NoCode);

        await db.StoreCarts.Where(c => c.Id == cart.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CouponId, (Guid?)null).SetProperty(c => c.LastActivityUtc, Now), ct);
        cart.CouponId = null;
        return new StoreCartWrite(StoreCartOutcome.Ok, await ViewAsync(cart, caller.UserId, null, ct), null);
    }

    /// <summary>Empties a cart after its order is paid (S4). The row stays for the next visit.</summary>
    public async Task ClearAsync(Guid cartId, CancellationToken ct = default)
    {
        await db.StoreCartItems.Where(i => i.CartId == cartId).ExecuteDeleteAsync(ct);
        await db.StoreCarts.Where(c => c.Id == cartId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CouponId, (Guid?)null).SetProperty(c => c.LastActivityUtc, Now), ct);
    }

    private Task<StoreProductVariant?> SellableVariantAsync(Guid variantId, CancellationToken ct)
        => db.StoreProductVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == variantId && v.IsActive && v.Product.IsActive && v.Product.Category.IsActive, ct);

    /// <summary>Sets a line to exactly <paramref name="quantity"/>, adding it if it is not there.</summary>
    private async Task SetLineAsync(Guid cartId, Guid variantId, int quantity, CancellationToken ct)
    {
        var updated = await db.StoreCartItems.Where(i => i.CartId == cartId && i.VariantId == variantId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, quantity).SetProperty(i => i.DateUpdated, Now), ct);
        if (updated > 0) return;

        var item = new StoreCartItem { Id = Guid.NewGuid(), CartId = cartId, VariantId = variantId, Quantity = quantity, DateCreated = Now };
        db.StoreCartItems.Add(item);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The same line added twice at once: the other request inserted it. Set it instead.
            db.Entry(item).State = EntityState.Detached;
            await db.StoreCartItems.Where(i => i.CartId == cartId && i.VariantId == variantId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, quantity).SetProperty(i => i.DateUpdated, Now), ct);
        }
    }

    private Task TouchAsync(Guid cartId, CancellationToken ct)
        => db.StoreCarts.Where(c => c.Id == cartId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityUtc, Now).SetProperty(c => c.DateUpdated, Now), ct);
}
