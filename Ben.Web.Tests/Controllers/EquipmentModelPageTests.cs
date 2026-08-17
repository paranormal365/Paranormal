using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The make/model page: everything owners have contributed about one product, pooled (item #55,
/// phase 6b).
/// </summary>
/// <remarks>
/// <para>Two rules carry the privacy weight here. Photos are pooled <b>anonymously</b> — the shape
/// cannot carry an owner or an item id, so nothing is available to leak. And the click-through is
/// resolved <b>per viewer</b>: an item is linked only when that particular caller may open it.</para>
///
/// <para>These are separable and both matter. A stranger seeing a photo of a private item is
/// intended (the owner left it in the pool); a stranger learning <i>which item</i> it is, or being
/// able to open it, is not.</para>
/// </remarks>
public class EquipmentModelPageTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FellowMemberId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid ModelId,
        Guid PublicItemId,
        Guid PrivateItemId,
        Guid SharedItemId);

    private static EquipmentCatalogController Build(IDbContextFactory<BenDataContext> f, Guid? userId)
    {
        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer");

        return new EquipmentCatalogController(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    /// <summary>
    /// One model with three copies: one listed publicly, one strictly private, one shared into a
    /// group the fellow member belongs to. Each has a photo left in the pool.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 { (OwnerId, "The Owner"), (FellowMemberId, "Fellow Member"), (StrangerId, "Stranger") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        foreach (var userId in new[] { OwnerId, FellowMemberId })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });

        var categoryId = Guid.NewGuid(); var brandId = Guid.NewGuid(); var modelId = Guid.NewGuid();
        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Zoom", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "H1n", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        Guid AddItem(string name, bool publiclyListed, string? website, Guid owner)
        {
            var itemId = Guid.NewGuid();
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = itemId, OwnerAppUserId = owner, EquipmentModelId = modelId,
                DisplayName = name, IncludeInGlobalCatalog = publiclyListed, WebsiteUrl = website,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = owner,
                FileName = "p.jpg", StoredFileName = "p.jpg", ContentType = "image/jpeg",
                FileSize = 3, StoragePath = $"fake/{itemId}.jpg",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.EquipmentItemPhotos.Add(new EquipmentItemPhoto
            {
                Id = Guid.NewGuid(), EquipmentItemId = itemId, UploadFileId = fileId,
                IsPrimary = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            return itemId;
        }

        var publicItemId  = AddItem("Listed publicly", true,  "https://example.com/h1n", OwnerId);
        var privateItemId = AddItem("Kept private",    false, "https://example.com/h1n", OwnerId);
        var sharedItemId  = AddItem("Shared with group", false, "https://review.example/h1n", OwnerId);

        db.EquipmentItemShares.Add(new EquipmentItemShare
        {
            Id = Guid.NewGuid(), EquipmentItemId = sharedItemId, OrganizationId = OrgId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, modelId, publicItemId, privateItemId, sharedItemId);
    }

    private static async Task<EquipmentModelPageRecord> GetPageAsync(World w, Guid? viewer)
    {
        var result = await Build(w.Factory, viewer).GetModelPage(w.ModelId, default);
        return Assert.IsType<EquipmentModelPageRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ── Anonymity of the pool ────────────────────────────────────────────────

    /// <summary>
    /// The pooled-photo shape must not be able to carry an owner or an item id. Asserted against
    /// the type: a filter written wrongly later still has nothing to expose.
    /// </summary>
    [Fact]
    public void CatalogPhotoRecord_CarriesNoOwnerNoItemAndNoFile()
    {
        var props = typeof(CatalogPhotoRecord).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(props, n => n.Contains("Owner", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(props, n => n.Contains("UploadFile", StringComparison.OrdinalIgnoreCase));
        // LinkedItemId is the one deliberate exception, and it is per-viewer — so no *plain* item id.
        Assert.DoesNotContain(props, n => n == "EquipmentItemId");
    }

    [Fact]
    public async Task PhotosFromEveryCopyArePooled_IncludingPrivateOnes()
    {
        var w = await SeedAsync();
        var page = await GetPageAsync(w, viewer: null);

        // Three items, one photo each — the pool is what makes a model page worth visiting.
        Assert.Equal(3, page.Photos.Count);
    }

    [Fact]
    public async Task AnExcludedPhotoIsKeptOutOfThePool()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var photo = await db.EquipmentItemPhotos
                .FirstAsync(p => p.EquipmentItemId == w.PrivateItemId);
            photo.ExcludeFromCatalog = true;
            await db.SaveChangesAsync();
        }

        var page = await GetPageAsync(w, viewer: null);
        Assert.Equal(2, page.Photos.Count);
    }

    // ── Click-through is resolved per viewer ─────────────────────────────────

    [Fact]
    public async Task AStrangerCanOpenOnlyThePubliclyListedCopy()
    {
        var w = await SeedAsync();
        var page = await GetPageAsync(w, StrangerId);

        var linked = page.Photos.Where(p => p.LinkedItemId is not null).Select(p => p.LinkedItemId!.Value).ToList();
        Assert.Equal([w.PublicItemId], linked);
    }

    [Fact]
    public async Task AnAnonymousVisitorLearnsNoItemIdBeyondThePublicOne()
    {
        var w = await SeedAsync();
        var page = await GetPageAsync(w, viewer: null);

        var linked = page.Photos.Where(p => p.LinkedItemId is not null).Select(p => p.LinkedItemId!.Value).ToList();
        Assert.Equal([w.PublicItemId], linked);
        // The private item's id must not reach an anonymous payload by any route.
        Assert.DoesNotContain(w.PrivateItemId, linked);
    }

    [Fact]
    public async Task AGroupMemberCanAlsoOpenTheCopySharedWithTheirGroup()
    {
        var w = await SeedAsync();
        var page = await GetPageAsync(w, FellowMemberId);

        var linked = page.Photos.Where(p => p.LinkedItemId is not null).Select(p => p.LinkedItemId!.Value).ToHashSet();
        Assert.Contains(w.PublicItemId, linked);
        Assert.Contains(w.SharedItemId, linked);
        Assert.DoesNotContain(w.PrivateItemId, linked);   // shared ≠ everything of that owner's
    }

    [Fact]
    public async Task TheOwnerCanOpenAllOfTheirOwn()
    {
        var w = await SeedAsync();
        var page = await GetPageAsync(w, OwnerId);

        var linked = page.Photos.Where(p => p.LinkedItemId is not null).Select(p => p.LinkedItemId!.Value).ToHashSet();
        Assert.Contains(w.PrivateItemId, linked);
        Assert.Equal(3, linked.Count);
    }

    // ── Links and counts ─────────────────────────────────────────────────────

    [Fact]
    public async Task LinksArePooledAndDeduplicated()
    {
        var w = await SeedAsync();
        var page = await GetPageAsync(w, viewer: null);

        // Two items share one URL, a third has its own — two distinct links, not three.
        Assert.Equal(2, page.WebsiteLinks.Count);
        Assert.Contains("https://example.com/h1n", page.WebsiteLinks);
        Assert.Contains("https://review.example/h1n", page.WebsiteLinks);
    }

    [Fact]
    public async Task RetiredCopiesAreLeftOutEntirely()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.PublicItemId);
            item.IsRetired = true;
            await db.SaveChangesAsync();
        }

        var page = await GetPageAsync(w, viewer: null);
        Assert.Equal(2, page.ItemCount);
        Assert.Equal(2, page.Photos.Count);
    }

    [Fact]
    public async Task AnUnapprovedModelIsNotPublicButItsProposerCanSeeIt()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var model = await db.EquipmentModels.SingleAsync(m => m.Id == w.ModelId);
            model.IsApproved = false;
            model.ProposedByAppUserId = OwnerId;
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(w.Factory, StrangerId).GetModelPage(w.ModelId, default)).Result);
        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId).GetModelPage(w.ModelId, default)).Result);
    }
}
