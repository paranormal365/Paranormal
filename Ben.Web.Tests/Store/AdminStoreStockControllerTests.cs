using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The stock page: a delivery is received all together or not at all, and the CSV going out is
/// one the CSV coming in can read (storefront S1.4).
/// </summary>
public sealed class AdminStoreStockControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private Guid _meter, _box, _bag, _admin;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        _admin = admin.Id;
        _meter = StoreTestData.Variant(db, admin, onHand: 12, sku: "KII-EMF").Id;
        _box = StoreTestData.Variant(db, admin, onHand: 8, reserved: 6, sku: "PSB7").Id;
        var bag = StoreTestData.Variant(db, admin, onHand: 2, sku: "BAG-L");
        _bag = bag.Id;
        await db.SaveChangesAsync();
        await db.StoreProducts.Where(p => p.Id == bag.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Investigator's Bag, Large"));
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private AdminStoreStockController Controller() => new(_sqlite.Factory)
    {
        ControllerContext = StoreTestData.SignedInAs(_admin),
    };

    private async Task<Dictionary<Guid, int>> OnHandAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreProductVariants.ToDictionaryAsync(v => v.Id, v => v.StockOnHand);
    }

    [Fact]
    public async Task One_line_that_cannot_apply_stops_the_whole_delivery()
    {
        var result = await Controller().Adjust(new BulkAdjustStockRequest(
            [new(_meter, 10, null), new(_box, -5, null), new(_bag, 3, null)], StoreStockReason.Correction, null), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Nothing was changed — PSB7 can't go below the 6 held by open checkouts.", refused.Value);
        Assert.Equal(new Dictionary<Guid, int> { [_meter] = 12, [_box] = 8, [_bag] = 2 }, await OnHandAsync());
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(0, await db.StoreStockMovements.CountAsync());
    }

    [Fact]
    public async Task A_good_delivery_moves_every_line_and_logs_each()
    {
        var ok = Assert.IsType<OkObjectResult>((await Controller().Adjust(new BulkAdjustStockRequest(
            [new(_meter, 10, null), new(_bag, null, 20)], StoreStockReason.Received, "Delivery 4471"), default)).Result);

        Assert.Equal(2, ((StoreStockAdjusted)ok.Value!).VariantsChanged);
        Assert.Equal(new Dictionary<Guid, int> { [_meter] = 22, [_box] = 8, [_bag] = 20 }, await OnHandAsync());
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(2, await db.StoreStockMovements.CountAsync(m => m.Note == "Delivery 4471"));
    }

    [Fact]
    public async Task A_csv_delivery_note_is_received_all_together()
    {
        var csv = "Sku,Delta\r\nkii-emf,5\r\n\"BAG-L\",10\r\n";
        var ok = Assert.IsType<OkObjectResult>((await Controller()
            .Import(StoreTestData.Upload(Encoding.UTF8.GetBytes(csv), "text/csv", "delivery.csv"), null, default)).Result);

        Assert.Equal(2, ((StoreStockAdjusted)ok.Value!).VariantsChanged);
        Assert.Equal(new Dictionary<Guid, int> { [_meter] = 17, [_box] = 8, [_bag] = 12 }, await OnHandAsync());
        await using var db = await _sqlite.NewContextAsync();
        Assert.All(await db.StoreStockMovements.ToListAsync(), m =>
            Assert.Equal((StoreStockReason.Received, "Imported from delivery.csv"), (m.Reason, m.Note)));
    }

    [Theory]
    [InlineData("KII-EMF,5\nNOPE,1\n", "Row 2: no variant has SKU NOPE.")]
    [InlineData("KII-EMF,five\n", "Row 1: five isn't a whole number.")]
    [InlineData("KII-EMF,1\nKII-EMF,2\n", "Row 2: KII-EMF is listed twice.")]
    [InlineData("KII-EMF,5\nPSB7,-3\n", "Nothing was changed — PSB7 can't go below the 6 held by open checkouts.")]
    public async Task A_csv_with_a_bad_row_changes_nothing_and_names_the_row(string csv, string sentence)
    {
        var result = await Controller().Import(StoreTestData.Upload(Encoding.UTF8.GetBytes(csv), "text/csv", "d.csv"), null, default);

        Assert.Equal(sentence, Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(new Dictionary<Guid, int> { [_meter] = 12, [_box] = 8, [_bag] = 2 }, await OnHandAsync());
    }

    [Fact]
    public async Task The_export_quotes_a_comma_and_reads_back()
    {
        var file = Assert.IsType<FileContentResult>(await Controller().Export(default));
        Assert.Equal("text/csv", file.ContentType);

        var rows = StoreCsv.Parse(Encoding.UTF8.GetString(file.FileContents));
        Assert.Equal(["Sku", "Product", "Variant", "OnHand", "Reserved", "Available", "Low"], rows[0]);
        Assert.Contains("\"Investigator's Bag, Large\"", Encoding.UTF8.GetString(file.FileContents));

        var bag = rows.Single(r => r[0] == "BAG-L");
        Assert.Equal(["BAG-L", "Investigator's Bag, Large", "Default", "2", "0", "2", "yes"], bag);
        var box = rows.Single(r => r[0] == "PSB7");
        Assert.Equal(("8", "6", "2", "yes"), (box[3], box[4], box[5], box[6]));
        Assert.Equal("no", rows.Single(r => r[0] == "KII-EMF")[6]);
    }

    [Fact]
    public async Task The_low_filter_uses_what_is_free_not_what_is_on_the_shelf()
    {
        var rows = (IEnumerable<StoreStockRow>)((OkObjectResult)(await Controller().GetAll(null, true, default)).Result!).Value!;
        Assert.Equal(["BAG-L", "PSB7"], rows.Select(r => r.Sku).Order());
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("-5", "-5")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void A_field_that_a_spreadsheet_would_run_is_kept_as_text(string value, string written)
        => Assert.Equal(written, StoreCsv.Field(value));
}
