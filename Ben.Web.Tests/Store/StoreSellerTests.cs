using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The Seller role and an item's seller (storefront, Ben 09/24/2026): only a holder of the role
/// can be named, somebody who has lost it stays on what they had, and a seller who leaves the
/// site leaves their items behind as the site's own.
/// </summary>
public sealed class StoreSellerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _seller = null!, _member = null!;
    private Guid _categoryId, _sellerRoleId;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _seller = StoreTestData.Person(db, "maker");
        _seller.DisplayName = "Edna Maker";
        _member = StoreTestData.Person(db, "member");
        _categoryId = StoreTestData.Category(db, _admin, "Trigger objects").Id;
        _sellerRoleId = Guid.NewGuid();
        db.Roles.Add(new IdentityRole<Guid> { Id = _sellerRoleId, Name = RoleNames.Seller, NormalizedName = "SELLER" });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _seller.Id, RoleId = _sellerRoleId });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private AdminStoreProductController Controller() => new(
        _sqlite.Factory, new Mock<IAuditLogService>().Object,
        new StoreImageStorage(TestMedia.StorageOnDisk(Path.GetTempPath()), new MediaSanitizationService(), TestMedia.IngestToDisk(Path.GetTempPath())),
        new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;

    private SaveStoreProductRequest Save(StoreProductAdminRecord p, Guid? seller)
        => new(_categoryId, null, p.Name, null, p.ShortDescription, p.LongDescriptionHtml, false, null, null, 0, [], seller);

    private async Task<StoreProductAdminRecord> NewAsync()
        => Ok(await Controller().Create(new CreateStoreProductRequest("Cat-ball", _categoryId), default));

    [Fact]
    public async Task The_sellers_list_is_the_Seller_role_and_nobody_else()
    {
        var sellers = Ok(await Controller().Sellers(default)).ToList();

        var only = Assert.Single(sellers);
        Assert.Equal(_seller.Id, only.Id);
        Assert.Equal("Edna Maker", only.Name);
    }

    [Fact]
    public async Task Only_a_holder_of_the_role_can_be_named_the_seller()
    {
        var p = await NewAsync();

        var refused = await Controller().Update(p.Id, Save(p, _member.Id), default);
        Assert.Equal(AdminStoreProductController.NotASeller, Assert.IsType<BadRequestObjectResult>(refused.Result).Value);

        var named = Ok(await Controller().Update(p.Id, Save(p, _seller.Id), default));
        Assert.Equal(_seller.Id, named.SellerAppUserId);
        Assert.Equal("Edna Maker", named.SellerName);

        var listed = Ok(await Controller().GetAll(null, null, null, null, null, default)).Single(r => r.Id == p.Id);
        Assert.Equal("Edna Maker", listed.SellerName);
    }

    [Fact]
    public async Task A_seller_who_loses_the_role_stays_on_the_item_they_had()
    {
        var p = Ok(await Controller().Update((await NewAsync()).Id, Save(await NewAsync(), _seller.Id), default));
        await using (var db = await _sqlite.NewContextAsync())
            await db.UserRoles.Where(r => r.UserId == _seller.Id).ExecuteDeleteAsync();

        // An unrelated save — the name — keeps them.
        var saved = Ok(await Controller().Update(p.Id, Save(p with { Name = "Cat-ball II" }, _seller.Id), default));
        Assert.Equal(_seller.Id, saved.SellerAppUserId);

        // Back to the site's own stock is always allowed.
        var cleared = Ok(await Controller().Update(p.Id, Save(p, null), default));
        Assert.Null(cleared.SellerAppUserId);
    }

    [Fact]
    public async Task Closing_the_sellers_account_leaves_the_item_as_the_sites_own()
    {
        var p = await NewAsync();
        Ok(await Controller().Update(p.Id, Save(p, _seller.Id), default));

        await using (var db = await _sqlite.NewContextAsync())
        {
            var user = await db.AppUsers.SingleAsync(u => u.Id == _seller.Id);
            await AccountClosureService.AnonymiseAsync(db, user, default);
        }

        await using var check = await _sqlite.NewContextAsync();
        var product = await check.StoreProducts.SingleAsync(x => x.Id == p.Id);
        Assert.Null(product.SellerAppUserId);
    }
}
