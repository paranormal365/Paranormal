using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The server-side cart (storefront S3.2), on a real database: the merge, the caps, the sellable
/// rule, the money rows every surface shows, and that a refused write leaves nothing behind.
/// </summary>
public sealed class StoreCartServiceTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private readonly FakeClock _clock = new(StoreTestData.Now);

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _sqlite.DisposeAsync();

    private static string NewToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static StoreCartCaller Guest(string token) => StoreCartCaller.From(null, token);

    private async Task<T> WithAsync<T>(Func<StoreCartService, BenDataContext, Task<T>> act)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await act(new StoreCartService(db, _clock), db);
    }

    private async Task<StoreProductVariant> VariantAsync(int onHand = 12, decimal price = 59.99m, decimal? was = null)
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var v = StoreTestData.Variant(db, admin, onHand: onHand, price: price);
        v.CompareAtPrice = was;
        await db.SaveChangesAsync();
        return v;
    }

    private async Task SetAsync(string key, string value)
    {
        await using var db = await _sqlite.NewContextAsync();
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = key, Value = value, DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> CartRowsAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreCarts.CountAsync();
    }

    // ── Adding ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Adding_more_than_on_hand_answers_Only_n_left()
    {
        var v = await VariantAsync(onHand: 3);
        var token = NewToken();

        var write = await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 5));

        Assert.Equal(StoreCartOutcome.Conflict, write.Outcome);
        Assert.Equal("Only 3 left.", write.View!.Notice);
        Assert.Equal(3, Assert.Single(write.View.Lines).Quantity);
        Assert.True(write.View.CanCheckout);
    }

    [Fact]
    public async Task Adding_again_adds_to_the_line_and_caps_at_one_hundred()
    {
        var v = await VariantAsync(onHand: 500);
        var token = NewToken();

        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 60));
        var write = await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 60));

        Assert.Equal(StoreCartOutcome.Conflict, write.Outcome);
        Assert.Equal("You can buy up to 100 at a time.", write.View!.Notice);
        Assert.Equal(100, Assert.Single(write.View.Lines).Quantity);
    }

    [Fact]
    public async Task A_cart_holds_at_most_fifty_different_items()
    {
        var token = NewToken();
        for (var i = 0; i < StoreCartRules.MaxDistinctLines; i++)
        {
            var each = await VariantAsync();
            Assert.Equal(StoreCartOutcome.Ok, (await WithAsync((s, _) => s.AddAsync(Guest(token), each.Id, 1))).Outcome);
        }
        var oneMore = await VariantAsync();

        var write = await WithAsync((s, _) => s.AddAsync(Guest(token), oneMore.Id, 1));

        Assert.Equal(StoreCartOutcome.Conflict, write.Outcome);
        Assert.Equal(StoreCartSentences.TooManyLines, write.View!.Notice);
        Assert.Equal(StoreCartRules.MaxDistinctLines, write.View.Lines.Count);
    }

    [Fact]
    public async Task Prices_come_from_the_variant_not_the_request()
    {
        var v = await VariantAsync(price: 59.99m);
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 2));

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(x => x.Id == v.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 49.50m));

        var view = await WithAsync((s, _) => s.ViewAsync(Guest(token)));
        Assert.Equal(49.50m, view.Lines[0].UnitPrice);
        Assert.Equal(99.00m, view.Lines[0].LineTotal);
        Assert.Equal(99.00m, view.Subtotal);
    }

    [Fact]
    public async Task A_refused_write_creates_no_cart_row()
    {
        var soldOut = await VariantAsync(onHand: 0);
        var token = NewToken();

        var nothingLeft = await WithAsync((s, _) => s.AddAsync(Guest(token), soldOut.Id, 1));
        var noSuchThing = await WithAsync((s, _) => s.AddAsync(Guest(token), Guid.NewGuid(), 1));
        var zero = await WithAsync((s, _) => s.AddAsync(Guest(token), soldOut.Id, 0));

        Assert.Equal(StoreCartOutcome.Conflict, nothingLeft.Outcome);
        Assert.Equal("Sold out.", nothingLeft.Sentence);
        Assert.Equal(StoreCartOutcome.NotFound, noSuchThing.Outcome);
        Assert.Equal(StoreCartSentences.NoLongerAvailable, noSuchThing.Sentence);
        Assert.Equal(StoreCartOutcome.BadRequest, zero.Outcome);
        Assert.Equal(0, await CartRowsAsync());
    }

    [Fact]
    public async Task A_made_up_token_is_nobody()
    {
        var v = await VariantAsync();

        var write = await WithAsync((s, _) => s.AddAsync(StoreCartCaller.From(null, "short-and-made-up"), v.Id, 1));

        Assert.Equal(StoreCartOutcome.BadRequest, write.Outcome);
        Assert.Equal(StoreCartSentences.NoCart, write.Sentence);
        Assert.Equal(0, await CartRowsAsync());
    }

    [Fact]
    public async Task The_database_keeps_only_the_tokens_hash()
    {
        var v = await VariantAsync();
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        await using var db = await _sqlite.NewContextAsync();
        var cart = await db.StoreCarts.SingleAsync();
        Assert.Equal(64, cart.GuestTokenHash!.Length);
        Assert.NotEqual(token, cart.GuestTokenHash);
        Assert.Equal(StoreCartCaller.Hash(token), cart.GuestTokenHash);
    }

    // ── The sellable rule ───────────────────────────────────────────────────

    [Fact]
    public async Task A_cart_never_holds_an_inactive_variant()
    {
        var v = await VariantAsync();
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(x => x.Id == v.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false));

        var view = await WithAsync((s, _) => s.ViewAsync(Guest(token)));
        Assert.Equal(StoreCartSentences.NoLongerAvailable, view.Lines[0].Problem);
        Assert.False(view.CanCheckout);
        Assert.Equal(StoreCartSentences.FixTheLines, view.WhyNotCheckout);

        var again = await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));
        Assert.Equal(StoreCartOutcome.NotFound, again.Outcome);
    }

    [Fact]
    public async Task A_cart_never_holds_a_product_whose_category_is_hidden()
    {
        var v = await VariantAsync();
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        await using (var db = await _sqlite.NewContextAsync())
        {
            var categoryId = await db.StoreProducts.Where(p => p.Id == v.ProductId).Select(p => p.CategoryId).SingleAsync();
            await db.StoreCategories.Where(c => c.Id == categoryId).ExecuteUpdateAsync(x => x.SetProperty(c => c.IsActive, false));
        }

        var view = await WithAsync((s, _) => s.ViewAsync(Guest(token)));
        Assert.Equal(StoreCartSentences.NoLongerAvailable, view.Lines[0].Problem);
        Assert.False(view.Lines[0].IsPurchasable);
        Assert.False(view.CanCheckout);
    }

    [Fact]
    public async Task The_buyers_own_reservation_does_not_read_as_sold_out()
    {
        var v = await VariantAsync(onHand: 1);
        var token = NewToken();
        var added = await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        // Their checkout now holds the last one.
        await using (var db = await _sqlite.NewContextAsync())
        {
            await db.StoreProductVariants.Where(x => x.Id == v.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.StockReserved, 1));
            var order = StoreTestData.Order(db);
            order.StoreCartId = added.View!.CartId;
            db.StoreOrderItems.Add(new StoreOrderItem
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductId = v.ProductId, VariantId = v.Id, ProductName = "K-II",
                Sku = v.Sku, UnitPrice = v.Price, Quantity = 1, LineTotal = v.Price, DateCreated = StoreTestData.Now,
            });
            await db.SaveChangesAsync();
        }

        var mine = await WithAsync((s, _) => s.ViewAsync(Guest(token)));
        Assert.Equal(1, mine.Lines[0].Available);
        Assert.Null(mine.Lines[0].Problem);

        // Anybody else sees it gone.
        var other = NewToken();
        var theirs = await WithAsync((s, _) => s.AddAsync(Guest(other), v.Id, 1));
        Assert.Equal("Sold out.", theirs.Sentence);
    }

    // ── Money ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_cart_quotes_no_shipping()
    {
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");

        var view = await WithAsync((s, _) => s.ViewAsync(Guest(NewToken())));

        Assert.Null(view.CartId);
        Assert.Null(view.Shipping);
        Assert.Equal(0m, view.TotalBeforeTax);
        Assert.False(view.CanCheckout);
        Assert.Null(view.WhyNotCheckout);
    }

    [Theory]
    [InlineData("74.99", 7.95, false)]
    [InlineData("75.00", 0, true)]
    public async Task Shipping_is_flat_below_threshold_and_free_at_or_above(string price, double expected, bool free)
    {
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
        await SetAsync(SiteSettingKeys.StoreFreeShippingThresholdUsd, "75");
        var v = await VariantAsync(price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture));
        var token = NewToken();

        var view = (await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1))).View!;

        Assert.Equal((decimal)expected, view.Shipping);
        Assert.Equal(free, view.ShippingIsFree);
    }

    [Fact]
    public async Task Free_shipping_is_judged_after_the_discount()
    {
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
        await SetAsync(SiteSettingKeys.StoreFreeShippingThresholdUsd, "75");
        var v = await VariantAsync(price: 80m);
        string code;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            code = StoreTestData.Coupon(db, admin, percentOff: 10).Code;
            await db.SaveChangesAsync();
        }
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        var view = (await WithAsync((s, _) => s.ApplyCouponAsync(Guest(token), code))).View!;

        Assert.Equal(8.00m, view.Discount);
        Assert.Equal(7.95m, view.Shipping);   // $72 after the code is under $75
        Assert.Equal(79.95m, view.TotalBeforeTax);
    }

    [Fact]
    public async Task Total_equals_sum_of_shown_lines_to_the_cent()
    {
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
        var a = await VariantAsync(price: 19.99m, was: 24.99m);
        var b = await VariantAsync(price: 0.33m);
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), a.Id, 3));
        var view = (await WithAsync((s, _) => s.AddAsync(Guest(token), b.Id, 7))).View!;

        var first = view.Lines.Single(l => l.VariantId == a.Id);
        var second = view.Lines.Single(l => l.VariantId == b.Id);
        Assert.Equal(59.97m, first.LineTotal);
        Assert.Equal(15.00m, first.YouSave);
        Assert.Equal(2.31m, second.LineTotal);
        Assert.Null(second.YouSave);
        Assert.Equal(view.Lines.Sum(l => l.LineTotal), view.Subtotal);
        Assert.Equal(view.Subtotal - view.Discount + view.Shipping, view.TotalBeforeTax);
        Assert.Equal(10, view.Count);
    }

    [Fact]
    public async Task Checkout_paused_is_a_sentence_on_the_view()
    {
        await SetAsync(SiteSettingKeys.StoreCheckoutEnabled, "false");
        var v = await VariantAsync();
        var token = NewToken();

        var view = (await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1))).View!;

        Assert.False(view.CheckoutEnabled);
        Assert.False(view.CanCheckout);
        Assert.Equal("The store isn't taking orders at the moment.", view.WhyNotCheckout);
        Assert.Single(view.Lines);   // the cart is kept; only ordering is paused
    }

    // ── Codes ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_code_that_is_not_there_is_refused_with_the_buyers_sentence()
    {
        var v = await VariantAsync();
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        var write = await WithAsync((s, _) => s.ApplyCouponAsync(Guest(token), "NOPE-NOT-A-CODE"));

        Assert.Equal(StoreCartOutcome.BadRequest, write.Outcome);
        Assert.Equal(StoreCouponMath.NotRecognised, write.Sentence);
    }

    [Fact]
    public async Task A_code_is_typed_in_any_case_and_comes_off_the_products_only()
    {
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
        var v = await VariantAsync(price: 39.99m);
        string code;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            code = StoreTestData.Coupon(db, admin, StoreCouponKind.Fixed, amountOff: 50m).Code;
            await db.SaveChangesAsync();
        }
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        var view = (await WithAsync((s, _) => s.ApplyCouponAsync(Guest(token), code.ToLowerInvariant()))).View!;

        Assert.Equal(code, view.CouponCode);
        Assert.Equal(39.99m, view.Discount);
        Assert.Equal(7.95m, view.TotalBeforeTax);

        var removed = (await WithAsync((s, _) => s.ClearCouponAsync(Guest(token)))).View!;
        Assert.Null(removed.CouponCode);
        Assert.Equal(47.94m, removed.TotalBeforeTax);
    }

    [Fact]
    public async Task A_code_on_an_empty_cart_is_refused_and_makes_no_row()
    {
        var write = await WithAsync((s, _) => s.ApplyCouponAsync(Guest(NewToken()), "GHOST10"));

        Assert.Equal(StoreCartSentences.AddSomethingFirst, write.Sentence);
        Assert.Equal(0, await CartRowsAsync());
    }

    // ── Signing in ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_in_merges_the_guest_cart_and_removes_it()
    {
        var a = await VariantAsync();
        var b = await VariantAsync();
        AppUser member;
        await using (var db = await _sqlite.NewContextAsync())
        {
            member = StoreTestData.Person(db, "member");
            await db.SaveChangesAsync();
        }
        var token = NewToken();

        // Saved on the account earlier, on another device.
        await WithAsync((s, _) => s.AddAsync(new StoreCartCaller(member.Id, null), a.Id, 2));
        // Shopped for as a visitor on this one.
        await WithAsync((s, _) => s.AddAsync(Guest(token), a.Id, 1));
        await WithAsync((s, _) => s.AddAsync(Guest(token), b.Id, 4));

        var merged = await WithAsync((s, _) => s.ViewAsync(StoreCartCaller.From(member.Id, token)));
        var again = await WithAsync((s, _) => s.ViewAsync(StoreCartCaller.From(member.Id, token)));

        Assert.Equal(3, merged.Lines.Single(l => l.VariantId == a.Id).Quantity);
        Assert.Equal(4, merged.Lines.Single(l => l.VariantId == b.Id).Quantity);
        Assert.Equal(7, again.Count);   // looking twice does not add twice
        Assert.Equal(1, await CartRowsAsync());

        var visitorNow = await WithAsync((s, _) => s.ViewAsync(Guest(token)));
        Assert.True(visitorNow.IsEmpty);
    }

    [Fact]
    public async Task Signing_in_with_no_saved_cart_adopts_the_guest_cart()
    {
        var a = await VariantAsync();
        AppUser member;
        await using (var db = await _sqlite.NewContextAsync())
        {
            member = StoreTestData.Person(db, "member");
            await db.SaveChangesAsync();
        }
        var token = NewToken();
        var guestCart = (await WithAsync((s, _) => s.AddAsync(Guest(token), a.Id, 2))).View!.CartId;

        var view = await WithAsync((s, _) => s.ViewAsync(StoreCartCaller.From(member.Id, token)));

        Assert.Equal(guestCart, view.CartId);
        await using var check = await _sqlite.NewContextAsync();
        var cart = await check.StoreCarts.SingleAsync();
        Assert.Equal(member.Id, cart.AppUserId);
        Assert.Null(cart.GuestTokenHash);
    }

    [Fact]
    public async Task An_empty_guest_cart_leaves_the_saved_cart_alone()
    {
        var a = await VariantAsync();
        AppUser member;
        await using (var db = await _sqlite.NewContextAsync())
        {
            member = StoreTestData.Person(db, "member");
            await db.SaveChangesAsync();
        }
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(new StoreCartCaller(member.Id, null), a.Id, 2));
        await WithAsync((s, _) => s.AddAsync(Guest(token), a.Id, 1));
        await WithAsync((s, _) => s.RemoveAsync(Guest(token), a.Id));

        var view = await WithAsync((s, _) => s.ViewAsync(StoreCartCaller.From(member.Id, token)));

        Assert.Equal(2, Assert.Single(view.Lines).Quantity);
        Assert.Equal(1, await CartRowsAsync());
    }

    [Fact]
    public async Task Two_requests_merging_at_once_add_the_guest_lines_once()
    {
        // Both requests have read the guest cart; the competitor then merges completely before the
        // first one opens its transaction. (The test database shares one connection, so the two
        // cannot hold transactions at once — the window is the moment before the second begins.)
        var race = new RunBeforeTransaction();
        var sqlite = await SqliteTestDb.CreateAsync(race);
        await using var _ = sqlite;
        Guid variantId, memberId;
        await using (var db = await sqlite.NewContextAsync())
        {
            var admin = StoreTestData.Person(db);
            var member = StoreTestData.Person(db, "member");
            variantId = StoreTestData.Variant(db, admin, onHand: 50).Id;
            memberId = member.Id;
            await db.SaveChangesAsync();
        }
        var token = NewToken();
        await using (var db = await sqlite.NewContextAsync())
        {
            var s = new StoreCartService(db, _clock);
            await s.AddAsync(new StoreCartCaller(memberId, null), variantId, 2);
            await s.AddAsync(Guest(token), variantId, 3);
        }

        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            await new StoreCartService(other, _clock).ViewAsync(StoreCartCaller.From(memberId, token));
        };
        await using (var db = await sqlite.NewContextAsync())
            await new StoreCartService(db, _clock).ViewAsync(StoreCartCaller.From(memberId, token));

        await using var check = await sqlite.NewContextAsync();
        Assert.Equal(5, await check.StoreCartItems.Where(i => i.VariantId == variantId).SumAsync(i => i.Quantity));
        Assert.Equal(1, await check.StoreCarts.CountAsync());
    }

    // ── Changing a line ─────────────────────────────────────────────────────

    [Fact]
    public async Task Setting_a_quantity_caps_it_and_zero_removes_the_line()
    {
        var v = await VariantAsync(onHand: 4);
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 1));

        var capped = await WithAsync((s, _) => s.SetQuantityAsync(Guest(token), v.Id, 9));
        Assert.Equal(StoreCartOutcome.Conflict, capped.Outcome);
        Assert.Equal("Only 4 left.", capped.View!.Notice);
        Assert.Equal(4, capped.View.Lines[0].Quantity);

        var gone = await WithAsync((s, _) => s.SetQuantityAsync(Guest(token), v.Id, 0));
        Assert.Equal(StoreCartOutcome.Ok, gone.Outcome);
        Assert.True(gone.View!.IsEmpty);

        var notThere = await WithAsync((s, _) => s.RemoveAsync(Guest(token), v.Id));
        Assert.Equal(StoreCartOutcome.NotFound, notThere.Outcome);
    }

    [Fact]
    public async Task A_line_over_what_is_left_is_marked_until_it_is_fixed()
    {
        var v = await VariantAsync(onHand: 5);
        var token = NewToken();
        await WithAsync((s, _) => s.AddAsync(Guest(token), v.Id, 5));

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(x => x.Id == v.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.StockOnHand, 2));

        var view = await WithAsync((s, _) => s.ViewAsync(Guest(token)));
        Assert.Equal("Only 2 left.", view.Lines[0].Problem);
        Assert.False(view.CanCheckout);

        var fixedView = (await WithAsync((s, _) => s.SetQuantityAsync(Guest(token), v.Id, 2))).View!;
        Assert.Null(fixedView.Lines[0].Problem);
        Assert.True(fixedView.CanCheckout);
    }

    private sealed class RunBeforeTransaction : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public Func<Task>? Competitor;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
            System.Data.Common.DbConnection connection, Microsoft.EntityFrameworkCore.Diagnostics.TransactionStartingEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction> result, CancellationToken ct = default)
        {
            if (Competitor is { } competitor)
            {
                Competitor = null;
                await competitor();
            }
            return result;
        }
    }

    private sealed class FakeClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
