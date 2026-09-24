using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store's categories: every refusal says why, a hide says what it hid, and a category's
/// picture is a site-owned file nobody's account can take with it (storefront S1.3).
/// </summary>
public sealed class AdminStoreCategoryControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "store-cat-" + Guid.NewGuid().ToString("N"));
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        StoreTestData.StoreImageType(db, _admin);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlite.DisposeAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    private AdminStoreCategoryController Controller()
    {
        var storage = TestMedia.StorageOnDisk(_storageRoot);
        return new(_sqlite.Factory, new Mock<IAuditLogService>().Object,
            new StoreImageStorage(storage, new MediaSanitizationService(), TestMedia.IngestToDisk(_storageRoot)))
        {
            ControllerContext = StoreTestData.SignedInAs(_admin.Id),
        };
    }

    private static SaveStoreCategoryRequest Save(string name, string? slug = null, bool active = true)
        => new(name, slug, null, active, IsNew: false);

    private static string Refusal<T>(ActionResult<T> result)
        => (string)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    [Fact]
    public async Task A_new_category_gets_an_address_from_its_name_and_goes_last()
    {
        var first = Assert.IsType<OkObjectResult>((await Controller().Create(Save("EMF Meters"), default)).Result);
        var second = Assert.IsType<OkObjectResult>((await Controller().Create(Save("Spirit Boxes"), default)).Result);

        var a = (StoreCategoryAdminRecord)first.Value!;
        var b = (StoreCategoryAdminRecord)second.Value!;
        Assert.Equal(("emf-meters", "spirit-boxes"), (a.Slug, b.Slug));
        Assert.True(b.SortOrder > a.SortOrder);
    }

    [Fact]
    public async Task Each_refusal_says_what_is_wrong()
    {
        await Controller().Create(Save("EMF Meters"), default);

        Assert.Equal("A category needs a name.", Refusal(await Controller().Create(Save("  "), default)));
        Assert.Equal("There's already a category called emf meters.",
            Refusal(await Controller().Create(Save("emf meters", slug: "other"), default)));

        var taken = await Controller().Create(Save("Meters", slug: "EMF Meters"), default);
        Assert.IsType<ConflictObjectResult>(taken.Result);
        Assert.Equal("There is already a category at /store/c/emf-meters.", Refusal(taken));
    }

    [Fact]
    public async Task Reordering_needs_every_category_once()
    {
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            StoreTestData.Category(db, admin, "One", sortOrder: 0);
            StoreTestData.Category(db, admin, "Two", sortOrder: 1);
            StoreTestData.Category(db, admin, "Three", sortOrder: 2);
            await db.SaveChangesAsync();
        }
        var ids = ((IEnumerable<StoreCategoryAdminRecord>)((OkObjectResult)(await Controller().GetAll(default)).Result!).Value!)
            .Select(c => c.Id).ToList();

        Assert.Equal("That isn't the full list of categories.",
            Refusal(await Controller().Reorder(new ReorderRequest(ids.Take(2).ToList()), default)));
        Assert.Equal("That isn't the full list of categories.",
            Refusal(await Controller().Reorder(new ReorderRequest([ids[0], ids[0], ids[1]]), default)));

        var reversed = Enumerable.Reverse(ids).ToList();
        var ok = Assert.IsType<OkObjectResult>((await Controller().Reorder(new ReorderRequest(reversed), default)).Result);
        Assert.Equal(reversed, ((IEnumerable<StoreCategoryAdminRecord>)ok.Value!).Select(c => c.Id));
    }

    /// <summary>The count the confirm dialog shows before saving is the count the save took off.</summary>
    [Fact]
    public async Task Hiding_a_category_reports_the_live_products_the_list_promised()
    {
        Guid categoryId;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            var category = StoreTestData.Category(db, admin, "Spirit Boxes");
            StoreTestData.Product(db, admin, category, active: true);
            StoreTestData.Product(db, admin, category, active: true);
            StoreTestData.Product(db, admin, category, active: false);
            await db.SaveChangesAsync();
            categoryId = category.Id;
        }

        var listed = ((IEnumerable<StoreCategoryAdminRecord>)((OkObjectResult)(await Controller().GetAll(default)).Result!).Value!)
            .Single(c => c.Id == categoryId);
        Assert.Equal((3, 2), (listed.ProductCount, listed.LiveProductCount));

        var saved = (StoreCategorySaveResult)((OkObjectResult)(await Controller()
            .Update(categoryId, Save("Spirit Boxes", active: false), default)).Result!).Value!;

        Assert.Equal(listed.LiveProductCount, saved.HiddenProducts);
        Assert.Equal(0, saved.Category.LiveProductCount);

        // Hiding it again hides nothing more, and the products kept their own switches.
        var again = (StoreCategorySaveResult)((OkObjectResult)(await Controller()
            .Update(categoryId, Save("Spirit Boxes", active: false), default)).Result!).Value!;
        Assert.Equal(0, again.HiddenProducts);
        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal(2, await check.StoreProducts.CountAsync(p => p.CategoryId == categoryId && p.IsActive));
    }

    [Fact]
    public async Task A_category_with_products_or_the_last_one_is_not_removed()
    {
        Guid full, empty;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            var holding = StoreTestData.Category(db, admin, "Recorders");
            StoreTestData.Product(db, admin, holding);
            StoreTestData.Product(db, admin, holding);
            full = holding.Id;
            empty = StoreTestData.Category(db, admin, "Bags").Id;
            await db.SaveChangesAsync();
        }

        var refused = Assert.IsType<BadRequestObjectResult>(await Controller().Delete(full, default));
        Assert.Equal("Recorders still holds 2 products. Move them to another category first.", refused.Value);

        Assert.IsType<NoContentResult>(await Controller().Delete(empty, default));

        await using (var db = await _sqlite.NewContextAsync())
        {
            await db.StoreProducts.ExecuteDeleteAsync();
        }
        var last = Assert.IsType<BadRequestObjectResult>(await Controller().Delete(full, default));
        Assert.Equal("Recorders is the last category. Add another before removing it.", last.Value);
    }

    [Fact]
    public async Task A_category_picture_is_a_site_owned_file_that_never_expires()
    {
        var category = (StoreCategoryAdminRecord)((OkObjectResult)(await Controller().Create(Save("EMF Meters"), default)).Result!).Value!;

        var result = await Controller().SetImage(category.Id, StoreTestData.Upload(StoreTestData.Jpeg(3000, 2000)), default);
        var withPicture = (StoreCategoryAdminRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;

        await using var db = await _sqlite.NewContextAsync();
        var file = await db.UploadFiles.AsNoTracking().SingleAsync(f => f.Id == withPicture.ImageUploadFileId);
        Assert.Equal((null, null, null, _admin.Id), (file.ExpiresAtUtc, file.AppUserId, file.OwnerOrganizationId, file.CreatedByAppUserId));
        Assert.Equal(Ben.Data.WebApi.SeedData.UploadFileTypeSeeder.StoreImageFileTypeId, file.UploadFileTypeId);
        Assert.True(file.IsPublic);

        var metadata = await db.UploadFileMetadata.AsNoTracking().SingleAsync(m => m.UploadFileId == file.Id);
        Assert.Equal((1600, 1067), (metadata.WidthPixels, metadata.HeightPixels));
        Assert.True(File.Exists(Path.Combine(_storageRoot, file.StoragePath!)));
        Assert.True(File.Exists(Path.Combine(_storageRoot, file.StoragePath + ".thumb.jpg")));
    }

    [Fact]
    public async Task Replacing_a_picture_removes_the_old_file()
    {
        var category = (StoreCategoryAdminRecord)((OkObjectResult)(await Controller().Create(Save("EMF Meters"), default)).Result!).Value!;
        var first = (StoreCategoryAdminRecord)((OkObjectResult)(await Controller()
            .SetImage(category.Id, StoreTestData.Upload(StoreTestData.Jpeg()), default)).Result!).Value!;
        string oldPath;
        await using (var db = await _sqlite.NewContextAsync())
            oldPath = (await db.UploadFiles.SingleAsync(f => f.Id == first.ImageUploadFileId)).StoragePath!;

        await Controller().SetImage(category.Id, StoreTestData.Upload(StoreTestData.Jpeg()), default);

        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal(1, await check.UploadFiles.CountAsync());
        Assert.False(await check.UploadFiles.AnyAsync(f => f.Id == first.ImageUploadFileId));
        Assert.False(File.Exists(Path.Combine(_storageRoot, oldPath)));
    }

    [Fact]
    public async Task An_upload_that_is_not_a_readable_picture_is_refused_and_stores_nothing()
    {
        var category = (StoreCategoryAdminRecord)((OkObjectResult)(await Controller().Create(Save("EMF Meters"), default)).Result!).Value!;

        Assert.Equal("There was no picture in that upload.", Refusal(await Controller().SetImage(category.Id, null, default)));
        Assert.Equal("A category picture is a photograph — JPEG, PNG or similar.",
            Refusal(await Controller().SetImage(category.Id, StoreTestData.Upload([1, 2, 3], "application/pdf", "manual.pdf"), default)));
        Assert.Equal("That picture could not be read.",
            Refusal(await Controller().SetImage(category.Id, StoreTestData.Upload([1, 2, 3, 4, 5]), default)));

        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(0, await db.UploadFiles.CountAsync());
    }
}
