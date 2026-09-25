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
/// Store discount codes: each refusal says why, the list says what is wrong with a code, and a
/// code an order was bought with is frozen (storefront S1.5).
/// </summary>
public sealed class AdminStoreCouponControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private Guid _admin;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db).Id;
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private AdminStoreCouponController Controller() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object)
    {
        ControllerContext = StoreTestData.SignedInAs(_admin),
    };

    private static SaveStoreCouponRequest Code(
        string code = "ghost10", int? percent = 10, decimal? amount = null, decimal? minimum = null,
        DateTime? starts = null, DateTime? ends = null, int? max = null, bool active = true)
        => new(code, "", StoreCouponKind.Percent, percent, amount, minimum, starts, ends, max, 1, active);

    private static string Refusal<T>(ActionResult<T> result)
        => (string)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    private static StoreCouponAdminRecord Ok(ActionResult<StoreCouponAdminRecord> result)
        => (StoreCouponAdminRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;

    [Fact]
    public async Task A_code_is_stored_upper_case_and_named_after_itself()
    {
        var saved = Ok(await Controller().Create(Code(" ghost10 "), default));
        Assert.Equal(("GHOST10", "GHOST10", StoreCouponKind.Percent, (string?)null), (saved.Code, saved.Name, saved.Kind, saved.Problem));

        var dollars = Ok(await Controller().Create(Code("FIVE-OFF", percent: null, amount: 5m), default));
        Assert.Equal((StoreCouponKind.Fixed, (int?)null, (decimal?)5m), (dollars.Kind, dollars.PercentOff, dollars.AmountOff));
    }

    [Theory]
    [InlineData("  ", 10, null, null, "A coupon needs a code for people to type.")]
    [InlineData("G1", 10, null, null, "A code is 3–64 letters, digits or dashes.")]
    [InlineData("GHOST 10", 10, null, null, "A code is 3–64 letters, digits or dashes.")]
    [InlineData("BOTH", 10, 5.0, null, "Set a percentage or an amount, not both.")]
    [InlineData("PCT", 101, null, null, "Percent off is 1 to 100.")]
    [InlineData("PCT", 0, null, null, "Percent off is 1 to 100.")]
    [InlineData("AMT", null, -1.0, null, "An amount off is more than $0.00.")]
    [InlineData("MIN", 10, null, -1.0, "A minimum order can't be negative.")]
    public async Task Each_bad_code_is_refused_with_its_sentence(string code, int? percent, double? amount, double? minimum, string sentence)
        => Assert.Equal(sentence, Refusal(await Controller().Create(
            Code(code, percent, (decimal?)amount, (decimal?)minimum), default)));

    [Fact]
    public async Task A_window_must_open_before_it_closes_and_a_code_is_used_once()
    {
        var at = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("The window closes before it opens.",
            Refusal(await Controller().Create(Code(starts: at, ends: at), default)));

        Ok(await Controller().Create(Code("GHOST10"), default));
        var again = await Controller().Create(Code("ghost10"), default);
        Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Equal("The code GHOST10 is already in use.", Refusal(again));
    }

    [Fact]
    public async Task The_list_says_what_stops_a_code_working()
    {
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin);
            var usedUp = StoreTestData.Coupon(db, admin, maxRedemptions: 2);
            usedUp.RedemptionCount = 2;
            usedUp.Code = "USEDUP";
            var expired = StoreTestData.Coupon(db, admin);
            expired.Code = "EXPIRED";
            expired.EndsUtc = DateTime.UtcNow.AddDays(-1);
            var nothing = StoreTestData.Coupon(db, admin, kind: StoreCouponKind.Fixed, amountOff: 0m);
            nothing.Code = "NOTHING";
            var backwards = StoreTestData.Coupon(db, admin);
            backwards.Code = "BACKWARDS";
            backwards.StartsUtc = DateTime.UtcNow.AddDays(5);
            backwards.EndsUtc = DateTime.UtcNow.AddDays(4);
            await db.SaveChangesAsync();
        }

        var problems = ((IEnumerable<StoreCouponAdminRecord>)((OkObjectResult)(await Controller().GetAll(default)).Result!).Value!)
            .ToDictionary(c => c.Code, c => c.Problem);

        Assert.Equal("Used up.", problems["USEDUP"]);
        Assert.Equal("Expired.", problems["EXPIRED"]);
        Assert.Equal("Takes nothing off.", problems["NOTHING"]);
        Assert.Equal("Expires before it starts.", problems["BACKWARDS"]);
    }

    [Fact]
    public async Task A_code_an_order_used_is_retired_not_renamed_or_deleted()
    {
        var coupon = Ok(await Controller().Create(Code("GHOST10"), default));
        await using (var db = await _sqlite.NewContextAsync())
        {
            StoreTestData.Order(db, StoreOrderStatus.Paid).CouponId = coupon.Id;
            StoreTestData.Order(db, StoreOrderStatus.Delivered).CouponId = coupon.Id;
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, Ok(await Controller().GetById(coupon.Id, default)).OrderCount);
        Assert.Equal("GHOST10 has been used on 2 orders; make a new code instead of renaming this one.",
            Refusal(await Controller().Update(coupon.Id, Code("GHOST15"), default)));
        var refused = Assert.IsType<BadRequestObjectResult>(await Controller().Delete(coupon.Id, default));
        Assert.Equal("GHOST10 has been used on 2 orders. Retire it instead — an order keeps the code it was bought with.", refused.Value);

        var retired = Ok(await Controller().Update(coupon.Id, Code("GHOST10", active: false), default));
        Assert.False(retired.IsActive);
    }

    [Fact]
    public async Task An_unused_code_can_be_renamed_and_deleted()
    {
        var coupon = Ok(await Controller().Create(Code("GHOST10"), default));
        Assert.Equal("GHOST15", Ok(await Controller().Update(coupon.Id, Code("GHOST15", percent: 15), default)).Code);
        Assert.IsType<NoContentResult>(await Controller().Delete(coupon.Id, default));
    }
}
