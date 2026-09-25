using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>A seller's own items, and nobody else's (store sellers, backlog 251, P1).</summary>
public sealed class SellerStoreProductControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!;
    private StoreProduct _onSale = null!, _draft = null!, _offSale = null!, _ivans = null!, _sites = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _ivan = StoreTestData.Person(db, "ivan");
        var shelf = StoreTestData.Category(db, _admin, "Trigger Objects");

        _onSale = Owned(StoreTestData.Product(db, _admin, shelf), _hazel);
        _onSale.UnitsSold = 4;
        _onSale.LastSoldUtc = StoreTestData.Now;
        _draft = Owned(StoreTestData.Product(db, _admin, shelf, active: false, price: 0m), _hazel);
        _offSale = Owned(StoreTestData.Product(db, _admin, shelf, active: false), _hazel);
        _offSale.UnitsSold = 1;   // sold once, so it has been on sale
        _ivans = Owned(StoreTestData.Product(db, _admin, shelf), _ivan);
        _sites = StoreTestData.Product(db, _admin, shelf);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private static StoreProduct Owned(StoreProduct product, AppUser seller)
    {
        product.SellerAppUserId = seller.Id;
        return product;
    }

    private SellerStoreProductController As(AppUser who) => new(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(who.Id) };

    [Fact]
    public async Task A_seller_sees_their_own_items_and_nobody_elses()
    {
        var rows = (IEnumerable<SellerProductListRecord>)((OkObjectResult)(await As(_hazel).GetMine(default)).Result!).Value!;

        Assert.Equal(new[] { _draft.Id, _offSale.Id, _onSale.Id }.Order(), rows.Select(r => r.Id).Order());
        Assert.DoesNotContain(rows, r => r.Id == _ivans.Id || r.Id == _sites.Id);
    }

    [Fact]
    public async Task Each_item_says_where_it_stands_and_an_unpriced_draft_has_no_price()
    {
        var rows = ((IEnumerable<SellerProductListRecord>)((OkObjectResult)(await As(_hazel).GetMine(default)).Result!).Value!).ToDictionary(r => r.Id);

        Assert.Equal(StoreSellerItemStatus.OnSale, rows[_onSale.Id].Status);
        Assert.Equal(StoreSellerItemStatus.Draft, rows[_draft.Id].Status);
        Assert.Equal(StoreSellerItemStatus.OffSale, rows[_offSale.Id].Status);
        Assert.Null(rows[_draft.Id].MinPrice);
        Assert.Equal((59.99m, 4, StoreTestData.Now), (rows[_onSale.Id].MinPrice!.Value, rows[_onSale.Id].UnitsSold, rows[_onSale.Id].LastSoldUtc!.Value));
    }

    [Fact]
    public async Task The_counts_are_the_callers_alone()
    {
        var hazels = (SellerWorkspaceSummary)((OkObjectResult)(await As(_hazel).Summary(default)).Result!).Value!;
        var ivans = (SellerWorkspaceSummary)((OkObjectResult)(await As(_ivan).Summary(default)).Result!).Value!;

        Assert.Equal(new SellerWorkspaceSummary(Drafts: 1, OnSale: 1, OffSale: 1), hazels);
        Assert.Equal(new SellerWorkspaceSummary(Drafts: 0, OnSale: 1, OffSale: 0), ivans);
    }

    [Fact]
    public async Task Somebody_who_sells_nothing_has_an_empty_list()
    {
        var rows = (IEnumerable<SellerProductListRecord>)((OkObjectResult)(await As(_admin).GetMine(default)).Result!).Value!;
        Assert.Empty(rows);
    }
}
