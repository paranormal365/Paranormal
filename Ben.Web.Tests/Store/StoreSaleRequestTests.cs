using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// A seller's item going on sale (store sellers, backlog 251, P3): the seller makes a draft and
/// asks, with what they want a unit; the store prices it and approves, or declines saying why; the
/// seller can take it off sale, and delete only a draft that never went on sale. A seller never
/// sets a price.
/// </summary>
public sealed class StoreSaleRequestTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!;
    private Guid _categoryId;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "store-sale-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _admin.DisplayName = "Ada Admin";
        _hazel = StoreTestData.Person(db, "hazel");
        _hazel.DisplayName = "Hazel Marsh";
        _ivan = StoreTestData.Person(db, "ivan");
        StoreTestData.StoreImageType(db, _admin);
        _categoryId = StoreTestData.Category(db, _admin, "Trigger Objects").Id;
        var seller = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Seller, NormalizedName = "SELLER" };
        var super = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin, NormalizedName = "SUPERADMIN" };
        db.Roles.AddRange(seller, super);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _hazel.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _ivan.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _admin.Id, RoleId = super.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private StoreImageStorage Images() => new(TestMedia.StorageOnDisk(_storageRoot), new MediaSanitizationService(), TestMedia.IngestToDisk(_storageRoot));
    private StoreSellerAlerts Alerts() => new(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), NullLogger<StoreSellerAlerts>.Instance);

    private SellerStoreProductEditController Seller(AppUser who) => new(_sqlite.Factory, Images(), new CmsMarkupSanitizer(), Alerts())
    {
        ControllerContext = StoreTestData.SignedInAs(who.Id),
    };

    private SellerStoreProductController SellerReads(AppUser who) => new(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(who.Id) };

    private AdminStoreProductController Admin() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object, Images(), new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private AdminStoreSaleRequestController Queue() => new(_sqlite.Factory, Images(), new CmsMarkupSanitizer(), Alerts())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;
    private static string Said<T>(ActionResult<T> result) => (string)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    /// <summary>Hazel's draft with a picture — ready but for its price.</summary>
    private async Task<SellerItemRecord> DraftAsync(AppUser? who = null, string name = "Hand-Built REM Pod")
    {
        var seller = Seller(who ?? _hazel);
        var item = Ok(await seller.Create(new CreateSellerItemRequest(name, _categoryId), default));
        return Ok(await seller.AddImage(item.Item.Id, StoreTestData.Upload(StoreTestData.Jpeg()), "front", null, default));
    }

    private async Task PriceAsync(StoreProductAdminRecord item, decimal price = 149m)
    {
        var v = item.Variants.Single();
        Ok(await Admin().UpdateVariant(item.Id, v.Id, new SaveStoreVariantRequest(v.Sku, price, null, true, true, 0, []), default));
    }

    private async Task<List<string>> BellsAsync(AppUser who)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.UserMessageTos.Where(t => t.ToAppUserId == who.Id).Select(t => t.UserMessage.MessageSubject!).ToListAsync();
    }

    // ── making and asking ────────────────────────────────────────────────────

    [Fact]
    public async Task A_sellers_draft_is_theirs_hidden_and_unpriced()
    {
        var item = await DraftAsync();

        Assert.Equal((_hazel.Id, false, StoreSellerItemStatus.Draft), (item.Item.SellerAppUserId, item.Item.IsActive, item.Status));
        Assert.Equal(0m, item.Item.Variants.Single().Price);
        Assert.Contains(Ok(await SellerReads(_hazel).GetMine(default)), r => r.Id == item.Item.Id);
        Assert.IsType<NotFoundResult>((await SellerReads(_ivan).GetById(item.Item.Id, default)).Result);
    }

    [Fact]
    public async Task Asking_opens_one_request_tells_the_admins_and_a_second_ask_is_refused()
    {
        var item = await DraftAsync();

        var asked = Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(95m, "Three ready."), default));
        Assert.Equal((StoreSaleRequestStatus.Open, 95m), (asked.Request!.Status, asked.Request.SellerAskingPrice));
        Assert.Contains("Hazel Marsh asks to put Hand-Built REM Pod on sale", await BellsAsync(_admin));

        var again = await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(90m, null), default);
        Assert.Equal(StoreProductSale.AlreadyAsked, Said(again));
        Assert.IsType<ConflictObjectResult>(again.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(9.999)]
    public async Task An_asking_price_is_dollars_and_cents_above_nothing(decimal ask)
    {
        var item = await DraftAsync();
        Assert.IsType<BadRequestObjectResult>((await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(ask, null), default)).Result);
    }

    [Fact]
    public async Task Nobody_asks_for_somebody_elses_item()
    {
        var item = await DraftAsync();
        Assert.IsType<NotFoundResult>((await Seller(_ivan).RequestSale(item.Item.Id, new SellerSaleRequest(95m, null), default)).Result);
    }

    [Fact]
    public async Task A_seller_can_take_their_request_back()
    {
        var item = await DraftAsync();
        Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(95m, null), default));

        var back = Ok(await Seller(_hazel).WithdrawSale(item.Item.Id, default));
        Assert.Null(back.Request);
        Assert.Empty(Ok(await Queue().GetAll(null, default)));
        Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(90m, null), default));   // and ask again
    }

    // ── the store's answer ───────────────────────────────────────────────────

    [Fact]
    public async Task The_queue_says_what_an_item_still_needs_and_approval_waits_for_it()
    {
        var item = await DraftAsync();
        Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(95m, null), default));

        var waiting = Assert.Single(Ok(await Queue().GetAll(null, default)));
        Assert.Equal(["Price the default variant first — it's still $0.00."], waiting.Problems);

        var early = await Queue().Approve(waiting.Id, default);
        Assert.Equal("Price the default variant first — it's still $0.00.", Said(early));
        Assert.Single(Ok(await Queue().GetAll(null, default)));   // still waiting
    }

    [Fact]
    public async Task Approval_puts_it_on_sale_and_fixes_what_the_seller_is_paid()
    {
        var item = await DraftAsync();
        Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(95m, null), default));
        await PriceAsync(item.Item);
        var waiting = Assert.Single(Ok(await Queue().GetAll(null, default)));
        Assert.Empty(waiting.Problems);

        var approved = Ok(await Queue().Approve(waiting.Id, default));

        Assert.Equal(StoreSaleRequestStatus.Approved, approved.Status);
        var now = Ok(await SellerReads(_hazel).GetById(item.Item.Id, default));
        Assert.Equal((true, StoreSellerItemStatus.OnSale, 95m), (now.Item.IsActive, now.Status, now.SellerAskPerUnit));
        Assert.NotNull(now.Item.FirstOnSaleUtc);
        Assert.Contains("Hand-Built REM Pod is on sale", await BellsAsync(_hazel));
        Assert.Equal(StoreProductSale.AlreadyDecided, Said(await Queue().Approve(waiting.Id, default)));
    }

    [Fact]
    public async Task A_decline_says_why_and_the_seller_reads_it()
    {
        var item = await DraftAsync();
        Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(95m, null), default));
        var waiting = Assert.Single(Ok(await Queue().GetAll(null, default)));

        Assert.IsType<BadRequestObjectResult>((await Queue().Decline(waiting.Id, new DeclineSaleRequest("  "), default)).Result);
        Ok(await Queue().Decline(waiting.Id, new DeclineSaleRequest("A clearer photo of the front, please."), default));

        var hers = Ok(await SellerReads(_hazel).GetById(item.Item.Id, default));
        Assert.Equal((StoreSaleRequestStatus.Declined, "A clearer photo of the front, please."), (hers.Request!.Status, hers.Request.DecisionNote));
        Assert.False(hers.Item.IsActive);
        Assert.Contains("Hand-Built REM Pod isn't going on sale yet", await BellsAsync(_hazel));
    }

    [Fact]
    public async Task The_store_cannot_put_a_sellers_item_on_sale_around_the_request()
    {
        var item = await DraftAsync();
        await PriceAsync(item.Item);

        Assert.Equal(AdminStoreProductController.SellersItemGoesOnSaleByRequest, Said(await Admin().Activate(item.Item.Id, default)));
    }

    // ── off sale, and deleting ───────────────────────────────────────────────

    [Fact]
    public async Task A_seller_takes_their_item_off_sale_and_the_store_hears()
    {
        var item = await OnSaleAsync();

        var off = Ok(await Seller(_hazel).TakeOffSale(item.Id, default));

        Assert.Equal((false, StoreSellerItemStatus.OffSale), (off.Item.IsActive, off.Status));
        Assert.Contains("Hazel Marsh took Hand-Built REM Pod off sale", await BellsAsync(_admin));
        Assert.IsType<BadRequestObjectResult>((await Seller(_hazel).TakeOffSale(item.Id, default)).Result);
    }

    [Fact]
    public async Task Only_a_draft_that_was_never_on_sale_can_be_deleted()
    {
        var draft = await DraftAsync(name: "Pocket EMF Logger");
        Assert.IsType<NoContentResult>(await Seller(_hazel).Delete(draft.Item.Id, default));

        var once = await OnSaleAsync();
        Ok(await Seller(_hazel).TakeOffSale(once.Id, default));
        Assert.Equal("Only a draft that has never been on sale can be deleted. Take it off sale instead.",
            Assert.IsType<BadRequestObjectResult>(await Seller(_hazel).Delete(once.Id, default)).Value);
    }

    private async Task<StoreProductAdminRecord> OnSaleAsync()
    {
        var item = await DraftAsync();
        Ok(await Seller(_hazel).RequestSale(item.Item.Id, new SellerSaleRequest(95m, null), default));
        await PriceAsync(item.Item);
        Ok(await Queue().Approve(Assert.Single(Ok(await Queue().GetAll(null, default))).Id, default));
        return Ok(await Admin().GetById(item.Item.Id, default));
    }

    // ── never the price ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_sellers_new_variant_starts_off_and_unpriced_and_cannot_be_switched_on()
    {
        var item = await DraftAsync();
        var withColour = Ok(await Seller(_hazel).SaveOptions(item.Item.Id, new SaveStoreOptionsRequest(
            [new SaveStoreOptionRequest(null, "Colour", StoreOptionKind.Pill, [new(null, "Black", null, true), new(null, "Red", null, true)])]), default));
        var red = withColour.Item.Options.Single().Values.Single(v => v.Value == "Red").Id;

        // One by hand — asked to be on, it arrives off and at $0.00…
        var added = Ok(await Seller(_hazel).CreateVariant(item.Item.Id, new SaveSellerVariantRequest("REM-RED", IsActive: true, false, 1, [red]), default));
        Assert.Equal((0m, false), added.Item.Variants.Where(v => v.Sku == "REM-RED").Select(v => (v.Price, v.IsActive)).Single());

        // …and the rest made from the choices, the same way.
        var made = Ok(await Seller(_hazel).GenerateVariants(item.Item.Id, default));
        Assert.All(made.Item.Variants, v => Assert.Equal((0m, false), (v.Price, v.IsActive)));
        var black = made.Item.Variants.First();
        var on = await Seller(_hazel).UpdateVariant(item.Item.Id, black.Id,
            new SaveSellerVariantRequest(black.Sku, IsActive: true, true, 0, black.OptionValueIds), default);
        Assert.Equal($"The store prices {black.Label} before it can be switched on.", Said(on));
    }

    [Fact]
    public async Task A_sellers_save_keeps_the_stores_price()
    {
        var item = await OnSaleAsync();
        var v = item.Variants.Single();

        Ok(await Seller(_hazel).UpdateVariant(item.Id, v.Id, new SaveSellerVariantRequest("HM-REM-2", true, true, 0, []), default));

        var after = Ok(await Admin().GetById(item.Id, default)).Variants.Single();
        Assert.Equal(("HM-REM-2", 149m), (after.Sku, after.Price));
    }

    [Fact]
    public void No_seller_request_can_carry_a_store_field()
    {
        // The store's fields are not in the seller's requests at all, so no client — ours or
        // anybody's — can send one (Ben: "not the admin part or pricing").
        string[] storeOnly = ["Price", "CompareAtPrice", "Slug", "IsFeatured", "StripeTaxCode", "SellerAppUserId", "NewUntilUtc", "EquipmentModelId"];
        foreach (var type in new[] { typeof(CreateSellerItemRequest), typeof(SaveSellerItemRequest), typeof(SaveSellerVariantRequest), typeof(SellerSaleRequest) })
            Assert.DoesNotContain(type.GetProperties(), p => storeOnly.Contains(p.Name));
    }

    // ── stale edits ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_save_over_somebody_elses_newer_save_is_refused_in_words()
    {
        var item = await DraftAsync();
        var opened = item.Item.EditedAt;

        // The store's staff save first…
        var mine = Ok(await Admin().GetById(item.Item.Id, default));
        Ok(await Admin().Update(item.Item.Id, new SaveStoreProductRequest(_categoryId, null, "REM Pod (store's words)", null, null, null,
            false, null, null, 0, [], _hazel.Id, mine.EditedAt), default));

        // …and the seller, still looking at what they opened, saves second.
        var late = await Seller(_hazel).Update(item.Item.Id, new SaveSellerItemRequest(_categoryId, "REM Pod (Hazel's words)", null, null, [], opened), default);

        Assert.IsType<ConflictObjectResult>(late.Result);
        Assert.Equal(StoreProductEditor.StaleEdit, Said(late));
        Assert.Equal("REM Pod (store's words)", Ok(await Admin().GetById(item.Item.Id, default)).Name);
    }

    [Fact]
    public async Task A_sellers_changes_read_as_theirs_in_the_history()
    {
        var item = await DraftAsync();
        Ok(await Seller(_hazel).Update(item.Item.Id,
            new SaveSellerItemRequest(_categoryId, "REM Pod Mk II", null, null, [], item.Item.EditedAt), default));

        var hers = Ok(await SellerReads(_hazel).History(item.Item.Id, null, null, default));
        Assert.All(hers, h => Assert.Equal(("You", StoreChangeActor.Seller), (h.ActorName, h.ActorRole)));
        var store = Ok(await Admin().History(item.Item.Id, null, null, default));
        Assert.Contains(store, h => h.ActorName == "Hazel Marsh" && h.Summary.StartsWith("Renamed it"));
    }
}
