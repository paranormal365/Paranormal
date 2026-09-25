using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ben.Service.RepositoryService.GenericInterfaces;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>An item's parts list and cost basis (store sellers, backlog 251, P4).</summary>
public sealed class StorePartsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!;
    private Guid _categoryId;
    private string _root = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _root = Path.Combine(Path.GetTempPath(), "store-parts-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _ivan = StoreTestData.Person(db, "ivan");
        StoreTestData.StoreImageType(db, _admin);
        _categoryId = StoreTestData.Category(db, _admin, "Trigger Objects").Id;
        var seller = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Seller, NormalizedName = "SELLER" };
        db.Roles.Add(seller);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _hazel.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _ivan.Id, RoleId = seller.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StoreImageStorage Images() => new(TestMedia.StorageOnDisk(_root), new MediaSanitizationService(), TestMedia.IngestToDisk(_root));

    private SellerStoreProductEditController Seller(AppUser who) => new(_sqlite.Factory, Images(), new CmsMarkupSanitizer(),
        new StoreSellerAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), NullLogger<StoreSellerAlerts>.Instance))
    {
        ControllerContext = StoreTestData.SignedInAs(who.Id),
    };

    private AdminStoreProductController Admin() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object, Images(), new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;
    private static string Said<T>(ActionResult<T> r) => (string)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;

    private async Task<Guid> DraftAsync() => Ok(await Seller(_hazel).Create(new CreateSellerItemRequest("REM Pod", _categoryId), default)).Item.Id;

    private static SaveStorePartsRequest RemPodParts(DateTime? expected, int? antennas = 7) => new(
    [
        new(null, "REM antenna kit", StorePartPriceBasis.PerPiece, 12.50m, 1, 1m, null, "https://parts.example/antenna", antennas),
        new(null, "9V battery clip", StorePartPriceBasis.PerPack, 4.99m, 10, 1m, null, null, 23),
        new(null, "Weatherproof enclosure", StorePartPriceBasis.PerPiece, 18.00m, 1, 1m, null, null, 4),
    ], 3.50m, "Solder and the box", expected);

    [Fact]
    public async Task A_seller_lists_parts_and_reads_the_cost_of_a_unit()
    {
        var id = await DraftAsync();
        var none = Ok(await Seller(_hazel).Parts(id, default));
        Assert.Equal((0m, (int?)null), (none.CostBasisPerUnit, none.BuildableFromOnHand));

        var saved = Ok(await Seller(_hazel).SaveParts(id, RemPodParts(none.EditedAt), default));

        Assert.Equal(34.50m, saved.CostBasisPerUnit);
        Assert.Equal(4, saved.BuildableFromOnHand);   // four enclosures
        Assert.Equal(0.499m, saved.Parts.Single(p => p.Name == "9V battery clip").CostPerUnit);
        Assert.Equal(new Dictionary<Guid, decimal> { [id] = 34.50m }, await StoreCostBasisReader.ReadAsync(await _sqlite.NewContextAsync(), [id]));
    }

    [Fact]
    public async Task The_history_says_what_a_unit_now_costs_and_a_recount_alone_says_so()
    {
        var id = await DraftAsync();
        var first = Ok(await Seller(_hazel).SaveParts(id, RemPodParts(Ok(await Seller(_hazel).Parts(id, default)).EditedAt), default));

        var again = first.Parts.Select(p => new SaveStorePartRequest(p.Id, p.Name, p.PriceBasis, p.Price, p.PiecesPerPack, p.QuantityPerUnit,
            p.InfoUrl, p.BuyUrl, p.OnHand is { } n ? n - 1 : null)).ToList();
        Ok(await Seller(_hazel).SaveParts(id, new SaveStorePartsRequest(again, first.OtherCostPerUnit, first.OtherCostNote, first.EditedAt), default));

        var lines = Ok(await Admin().History(id, null, null, default)).Where(h => h.Area == StoreProductChangeArea.Parts).Select(h => h.Summary).ToList();
        Assert.Equal(["Counted the parts on hand.", "Changed the parts list — a unit now costs $34.50 to make (was $0.00)."], lines);
    }

    [Theory]
    [InlineData("", 1.0, 1, "Each part needs a name.")]
    [InlineData("Clip", -1.0, 1, "Clip: a price can't be negative.")]
    [InlineData("Clip", 0.00001, 1, "Clip: a price goes to the hundredth of a cent at most — 0.0699.")]
    [InlineData("Clip", 1.0, 0, "Clip: a pack holds at least one piece.")]
    public async Task A_part_that_makes_no_sense_is_refused_in_words(string name, double price, int pack, string said)
    {
        var id = await DraftAsync();
        var request = new SaveStorePartsRequest([new(null, name, StorePartPriceBasis.PerPack, (decimal)price, pack, 1m, null, null, null)], 0m, null, null);
        Assert.Equal(said, Said(await Seller(_hazel).SaveParts(id, request, default)));
    }

    [Fact]
    public async Task A_link_must_be_a_web_address()
    {
        var id = await DraftAsync();
        var request = new SaveStorePartsRequest([new(null, "Clip", StorePartPriceBasis.PerPiece, 1m, 1, 1m, "javascript:alert(1)", null, null)], 0m, null, null);
        Assert.StartsWith("Clip: a link starts with https://", Said(await Seller(_hazel).SaveParts(id, request, default)));
    }

    [Fact]
    public async Task Another_seller_neither_reads_nor_changes_the_list()
    {
        var id = await DraftAsync();
        Assert.IsType<NotFoundResult>((await Seller(_ivan).Parts(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Seller(_ivan).SaveParts(id, RemPodParts(null), default)).Result);
    }

    [Fact]
    public async Task The_store_keeps_the_list_too_and_a_stale_save_is_refused()
    {
        var id = await DraftAsync();
        var opened = Ok(await Admin().Parts(id, default)).EditedAt;
        Ok(await Seller(_hazel).SaveParts(id, RemPodParts(opened), default));

        var late = await Admin().SaveParts(id, RemPodParts(opened, antennas: 1), default);
        Assert.Equal(StoreProductEditor.StaleEdit, Said(late));
    }
}
