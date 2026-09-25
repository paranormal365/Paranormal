using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Controllers.Store;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Favourites and reviews (storefront S6.1): only a buyer who kept what they bought may review it; a
/// review waits for approval, every time; helpful votes count once; hearts only for what is on sale;
/// and guest orders join the account whose confirmed email placed them.
/// </summary>
public sealed class StoreReviewRulesTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _buyer = null!, _other = null!;
    private StoreProduct _product = null!;
    private StoreCategory _category = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _buyer = StoreTestData.Person(db, "buyer");
        _buyer.Email = "buyer@example.com";
        _buyer.EmailConfirmed = true;
        _buyer.DisplayName = "Bea Buyer";
        _other = StoreTestData.Person(db, "other");
        _category = StoreTestData.Category(db, _admin, "Meters");
        _product = StoreTestData.Product(db, _admin, _category);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private MyStoreEngagementController Me(AppUser who) => new(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(who.Id) };

    private PublicStoreController Store(AppUser? who = null) => new(
        _sqlite.Factory, new StorePaymentSetup(false, true, true), Options.Create(new StripeOptions { SecretKey = "sk_test_x", PublishableKey = "pk_test_x" }))
    {
        ControllerContext = who is null ? new ControllerContext { HttpContext = new DefaultHttpContext() } : StoreTestData.SignedInAs(who.Id),
    };

    /// <summary>An order of one unit of the product, in the shape asked.</summary>
    private async Task<StoreOrder> OrderAsync(AppUser? buyer, bool paid = true, StoreOrderStatus status = StoreOrderStatus.Delivered,
        int refunded = 0, string email = "buyer@example.com")
    {
        await using var db = await _sqlite.NewContextAsync();
        var owner = buyer is null ? null : await db.AppUsers.SingleAsync(u => u.Id == buyer.Id);
        var order = StoreTestData.Order(db, status, owner, email);
        order.PaidUtc = paid ? DateTime.UtcNow : null;
        var variant = await db.StoreProductVariants.FirstAsync(v => v.ProductId == _product.Id);
        db.StoreOrderItems.Add(new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = _product.Id, VariantId = variant.Id, ProductName = _product.Name,
            Sku = variant.Sku, UnitPrice = 10m, Quantity = 1, LineTotal = 10m, QuantityRefunded = refunded, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return order;
    }

    private static SubmitStoreReviewRequest Good(int stars = 5) => new(stars, "Does what it says", "Picked up the fridge hum straight away.");

    // ── Who may review ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_buyer_who_kept_it_may_review_and_it_waits_for_approval()
    {
        await OrderAsync(_buyer);
        var result = await Me(_buyer).Review(_product.Id, Good(), default);

        var mine = (MyStoreReviewRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;
        Assert.Equal(StoreReviewStatus.Pending, mine.Status);
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(0, (await db.StoreProducts.SingleAsync(p => p.Id == _product.Id)).ReviewCount);
    }

    [Theory]
    [InlineData("unpaid")]
    [InlineData("cancelled-but-paid")]
    [InlineData("fully-refunded")]
    [InlineData("someone-elses")]
    public async Task Only_somebody_who_bought_and_kept_it_may_review(string shape)
    {
        _ = shape switch
        {
            "unpaid" => await OrderAsync(_buyer, paid: false, status: StoreOrderStatus.PendingPayment),
            "cancelled-but-paid" => await OrderAsync(_buyer, status: StoreOrderStatus.Cancelled),
            "fully-refunded" => await OrderAsync(_buyer, refunded: 1),
            _ => await OrderAsync(_other, email: "other@example.com"),
        };

        var result = await Me(_buyer).Review(_product.Id, Good(), default);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal((403, StoreReviewSentences.OnlyBuyers), (refused.StatusCode, refused.Value));
        var state = (StoreProductViewerState)Assert.IsType<OkObjectResult>((await Me(_buyer).State(_product.Id, default)).Result).Value!;
        Assert.False(state.CanReview);
    }

    [Fact]
    public async Task A_guest_order_with_the_confirmed_email_joins_the_account_and_counts()
    {
        var guest = await OrderAsync(null, email: "BUYER@example.com");

        var state = (StoreProductViewerState)Assert.IsType<OkObjectResult>((await Me(_buyer).State(_product.Id, default)).Result).Value!;

        Assert.True(state.CanReview);
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(_buyer.Id, (await db.StoreOrders.SingleAsync(o => o.Id == guest.Id)).BuyerAppUserId);
    }

    [Fact]
    public async Task An_unconfirmed_email_joins_nothing()
    {
        await using (var db = await _sqlite.NewContextAsync())
            await db.AppUsers.Where(u => u.Id == _buyer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, false));
        var guest = await OrderAsync(null);

        await using var read = await _sqlite.NewContextAsync();
        Assert.Equal(0, await StoreGuestOrders.AttachAsync(read, _buyer.Id));
        Assert.Null((await read.StoreOrders.SingleAsync(o => o.Id == guest.Id)).BuyerAppUserId);
    }

    [Theory]
    [InlineData(0, "Title", "Three good words", StoreReviewSentences.PickStars)]
    [InlineData(5, " ", "Three good words", StoreReviewSentences.NeedsTitle)]
    [InlineData(5, "Title", "Meh", StoreReviewSentences.NeedsWords)]
    public async Task A_review_as_typed_is_refused_in_words(int stars, string title, string body, string sentence)
    {
        await OrderAsync(_buyer);
        var result = await Me(_buyer).Review(_product.Id, new SubmitStoreReviewRequest(stars, title, body), default);
        Assert.Equal(sentence, Assert.IsType<BadRequestObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task Editing_an_approved_review_takes_it_back_to_the_queue_and_off_the_stars()
    {
        await OrderAsync(_buyer);
        await Me(_buyer).Review(_product.Id, Good(4), default);
        await using (var db = await _sqlite.NewContextAsync())
        {
            await db.StoreReviews.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StoreReviewStatus.Approved));
            await StoreRatingCaches.RecomputeAsync(db, _product.Id);
            Assert.Equal(1, (await db.StoreProducts.SingleAsync(p => p.Id == _product.Id)).ReviewCount);
        }

        await Me(_buyer).Review(_product.Id, Good(2), default);

        await using var read = await _sqlite.NewContextAsync();
        var review = await read.StoreReviews.SingleAsync();
        Assert.Equal((StoreReviewStatus.Pending, 2), (review.Status, review.Rating));
        Assert.Equal(0, (await read.StoreProducts.SingleAsync(p => p.Id == _product.Id)).ReviewCount);
    }

    // ── Reading and voting ───────────────────────────────────────────────────

    private async Task<Guid> ApprovedReviewAsync(AppUser author, int helpful = 0, int stars = 5, int daysAgo = 0)
    {
        var order = await OrderAsync(author, email: $"{author.Id:N}@example.com");
        await using var db = await _sqlite.NewContextAsync();
        var review = new StoreReview
        {
            Id = Guid.NewGuid(), ProductId = _product.Id, AuthorAppUserId = author.Id, OrderId = order.Id, Rating = stars,
            Title = $"{stars} stars", Body = "Words about it", Status = StoreReviewStatus.Approved, HelpfulCount = helpful,
            DateCreated = DateTime.UtcNow.AddDays(-daysAgo), CreatedByAppUserId = author.Id,
        };
        db.StoreReviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    [Fact]
    public async Task Reviews_read_most_popular_first_and_only_approved()
    {
        var old = await ApprovedReviewAsync(_other, helpful: 3, stars: 2, daysAgo: 30);
        var recent = await ApprovedReviewAsync(_admin, helpful: 0, stars: 5);
        await OrderAsync(_buyer);
        await Me(_buyer).Review(_product.Id, Good(), default);   // pending: not shown

        var popular = (StoreReviewPage)Assert.IsType<OkObjectResult>((await Store().Reviews(_product.Slug, null, null, default)).Result).Value!;
        var newest = (StoreReviewPage)Assert.IsType<OkObjectResult>((await Store().Reviews(_product.Slug, "newest", null, default)).Result).Value!;

        Assert.Equal([old, recent], popular.Reviews.Select(r => r.Id));
        Assert.Equal((2, StoreReviewSorts.Popular), (popular.Total, popular.Sort));
        Assert.Equal([recent, old], newest.Reviews.Select(r => r.Id));
    }

    [Fact]
    public async Task A_helpful_vote_counts_once_and_never_for_your_own()
    {
        var review = await ApprovedReviewAsync(_other);

        await Me(_buyer).Helpful(review, default);
        var twice = (StoreHelpfulVoteResult)Assert.IsType<OkObjectResult>((await Me(_buyer).Helpful(review, default)).Result).Value!;
        var own = await Me(_other).Helpful(review, default);

        Assert.Equal((1, true), (twice.HelpfulCount, twice.IVotedHelpful));
        Assert.Equal(StoreReviewSentences.NotOwnVote, Assert.IsType<BadRequestObjectResult>(own.Result).Value);
        var page = (StoreReviewPage)Assert.IsType<OkObjectResult>((await Store(_buyer).Reviews(_product.Slug, null, null, default)).Result).Value!;
        Assert.True(page.Reviews.Single().IVotedHelpful);

        var undone = (StoreHelpfulVoteResult)Assert.IsType<OkObjectResult>((await Me(_buyer).NotHelpful(review, default)).Result).Value!;
        Assert.Equal(0, undone.HelpfulCount);
    }

    // ── Favourites ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_heart_counts_once_and_a_hidden_products_heart_drops_off_the_list()
    {
        await Me(_buyer).AddFavourite(_product.Id, default);
        var twice = (StoreFavouriteCount)Assert.IsType<OkObjectResult>((await Me(_buyer).AddFavourite(_product.Id, default)).Result).Value!;
        Assert.Equal(1, twice.Count);

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreCategories.Where(c => c.Id == _category.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));

        var list = (IEnumerable<StoreProductCard>)Assert.IsType<OkObjectResult>((await Me(_buyer).Favourites(default)).Result).Value!;
        var count = (StoreFavouriteCount)Assert.IsType<OkObjectResult>((await Me(_buyer).FavouriteCount(default)).Result).Value!;
        Assert.Empty(list);
        Assert.Equal(0, count.Count);
        Assert.IsType<NotFoundObjectResult>((await Me(_other).AddFavourite(_product.Id, default)).Result);
    }
}
