using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The review queue: only approved reviews move a product's stars, a refusal says why, and the
/// shop replies only under what is published (storefront S1.6).
/// </summary>
public sealed class AdminStoreReviewControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private Guid _admin, _productId;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        _admin = admin.Id;
        _productId = StoreTestData.Product(db, admin, StoreTestData.Category(db, admin)).Id;
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private AdminStoreReviewController Controller() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object)
    {
        ControllerContext = StoreTestData.SignedInAs(_admin),
    };

    private async Task<Guid> ReviewAsync(int rating, string title = "Worked on the first night")
    {
        await using var db = await _sqlite.NewContextAsync();
        var buyer = StoreTestData.Person(db, "buyer");
        var order = StoreTestData.Order(db, StoreOrderStatus.Delivered, buyer);
        var review = new StoreReview
        {
            Id = Guid.NewGuid(), ProductId = _productId, AuthorAppUserId = buyer.Id, OrderId = order.Id,
            Rating = rating, Title = title, Body = "It lit up in the attic.", Status = StoreReviewStatus.Pending,
            DateCreated = StoreTestData.Now, CreatedByAppUserId = buyer.Id,
        };
        db.StoreReviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    private async Task<(decimal Average, int Count)> StarsAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        var p = await db.StoreProducts.AsNoTracking().SingleAsync(x => x.Id == _productId);
        return (p.AverageRating, p.ReviewCount);
    }

    private static string Refusal<T>(ActionResult<T> result)
        => (string)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    [Fact]
    public async Task Only_approved_reviews_count_toward_the_stars()
    {
        var five = await ReviewAsync(5);
        var three = await ReviewAsync(3);
        var one = await ReviewAsync(1);
        Assert.Equal((0m, 0), await StarsAsync());

        await Controller().Approve(five, default);
        await Controller().Approve(three, default);
        await Controller().Reject(one, new RejectStoreReviewRequest("It's about the courier, not the meter."), default);

        Assert.Equal((4.00m, 2), await StarsAsync());

        Assert.IsType<NoContentResult>(await Controller().Delete(three, default));
        Assert.Equal((5.00m, 1), await StarsAsync());
    }

    [Fact]
    public async Task A_refusal_needs_a_reason_and_keeps_it()
    {
        var id = await ReviewAsync(2);
        Assert.Equal("Give a reason — the reviewer is told why.",
            Refusal(await Controller().Reject(id, new RejectStoreReviewRequest("  "), default)));

        var rejected = (StoreReviewAdminRecord)((OkObjectResult)(await Controller()
            .Reject(id, new RejectStoreReviewRequest("Contains a phone number."), default)).Result!).Value!;
        Assert.Equal((StoreReviewStatus.Rejected, "Contains a phone number."), (rejected.Status, rejected.RejectionReason));
    }

    [Fact]
    public async Task The_shop_replies_only_under_a_published_review()
    {
        var id = await ReviewAsync(4);
        Assert.Equal("Approve the review before replying to it.",
            Refusal(await Controller().Reply(id, new ReplyToStoreReviewRequest("Thank you!"), default)));

        await Controller().Approve(id, default);
        Assert.Equal("A reply is 1,000 characters at most.",
            Refusal(await Controller().Reply(id, new ReplyToStoreReviewRequest(new string('x', 1001)), default)));
        var replied = (StoreReviewAdminRecord)((OkObjectResult)(await Controller()
            .Reply(id, new ReplyToStoreReviewRequest("Thank you!"), default)).Result!).Value!;
        Assert.Equal("Thank you!", replied.AdminReply);

        // Refusing it afterwards takes the reply down with it.
        var refused = (StoreReviewAdminRecord)((OkObjectResult)(await Controller()
            .Reject(id, new RejectStoreReviewRequest("Off topic."), default)).Result!).Value!;
        Assert.Null(refused.AdminReply);
    }

    [Fact]
    public async Task The_queue_is_worked_oldest_first()
    {
        var first = await ReviewAsync(4, "First");
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreReviews.Where(r => r.Id == first)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.DateCreated, StoreTestData.Now.AddDays(-2)));
        await ReviewAsync(5, "Second");

        var queue = (IEnumerable<StoreReviewAdminRecord>)((OkObjectResult)(await Controller()
            .GetAll(StoreReviewStatus.Pending, null, null, null, null, default)).Result!).Value!;
        Assert.Equal(["First", "Second"], queue.Select(r => r.Title));
    }
}
