using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Readable addresses for the equipment catalog — <c>/equipment/zoom/h1n</c> (backlog item #89).
/// </summary>
/// <remarks>
/// <para>The last page in the readable-URL work still wearing a GUID, and the one Ben raised first:
/// <i>"we use the GUID for many of the IDs. That is not human readable."</i></para>
///
/// <para><b>This slug is regenerated on rename</b>, unlike every other one here. A case, an event
/// and an organization freeze theirs, because somebody chose it and shared it. The catalog is the
/// site's own vocabulary and its rename path exists to correct mistakes — a page for a make fixed
/// from "Sansung" to "Samsung" that still answered only to <c>/equipment/sansung</c> would keep the
/// error in the most visible place there is.</para>
/// </remarks>
public sealed class EquipmentCatalogSlugTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<EquipmentBrand> AddBrandAsync(
        IDbContextFactory<BenDataContext> factory, string name)
    {
        await using var db = await factory.CreateDbContextAsync();

        var brand = new EquipmentBrand
        {
            Id = Guid.NewGuid(), Name = name, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        };

        db.EquipmentBrands.Add(brand);
        await EquipmentCatalogSlugs.AssignAsync(db, brand, default);
        await db.SaveChangesAsync();
        return brand;
    }

    private static async Task<EquipmentModel> AddModelAsync(
        IDbContextFactory<BenDataContext> factory, Guid brandId, string name)
    {
        await using var db = await factory.CreateDbContextAsync();

        var model = new EquipmentModel
        {
            Id = Guid.NewGuid(), EquipmentBrandId = brandId, EquipmentCategoryId = Guid.NewGuid(),
            Name = name, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        };

        db.EquipmentModels.Add(model);
        await EquipmentCatalogSlugs.AssignAsync(db, model, default);
        await db.SaveChangesAsync();
        return model;
    }

    // ── The ordinary case ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Zoom", "zoom")]
    [InlineData("Olympus", "olympus")]
    [InlineData("Generic / Unbranded", "generic-unbranded")]
    [InlineData("FLIR Systems", "flir-systems")]
    public async Task A_make_gets_a_readable_address(string name, string expected)
    {
        var factory = CreateFactory();
        var brand = await AddBrandAsync(factory, name);

        Assert.Equal(expected, brand.UrlName);
    }

    [Theory]
    [InlineData("H1n", "h1n")]
    [InlineData("Tascam DR-40X", "tascam-dr-40x")]
    [InlineData("EMF Meter", "emf-meter")]
    public async Task A_model_gets_a_readable_address(string name, string expected)
    {
        var factory = CreateFactory();
        var brand = await AddBrandAsync(factory, "Zoom");
        var model = await AddModelAsync(factory, brand.Id, name);

        Assert.Equal(expected, model.UrlName);
    }

    // ── Uniqueness ───────────────────────────────────────────────────────────

    /// <summary>
    /// Two makes whose names differ only in punctuation slug the same way, and the second takes a
    /// suffix rather than colliding on the index.
    /// </summary>
    [Fact]
    public async Task Two_makes_that_slug_alike_do_not_collide()
    {
        var factory = CreateFactory();

        var first  = await AddBrandAsync(factory, "Zoom");
        var second = await AddBrandAsync(factory, "Zoom!");

        Assert.Equal("zoom", first.UrlName);
        Assert.NotEqual(first.UrlName, second.UrlName);
        Assert.StartsWith("zoom", second.UrlName);
    }

    /// <summary>
    /// Two makes may each have an "X1". The addresses are scoped to the make, exactly as the names
    /// are, so neither is forced to take a suffix it did not need.
    /// </summary>
    [Fact]
    public async Task The_same_model_name_under_two_makes_keeps_the_same_segment()
    {
        var factory = CreateFactory();

        var zoom   = await AddBrandAsync(factory, "Zoom");
        var tascam = await AddBrandAsync(factory, "Tascam");

        var zoomX1   = await AddModelAsync(factory, zoom.Id, "X1");
        var tascamX1 = await AddModelAsync(factory, tascam.Id, "X1");

        Assert.Equal("x1", zoomX1.UrlName);
        Assert.Equal("x1", tascamX1.UrlName);
    }

    /// <summary>Two models with the same name under one make still get separate addresses.</summary>
    [Fact]
    public async Task Two_models_alike_under_one_make_do_not_collide()
    {
        var factory = CreateFactory();
        var brand = await AddBrandAsync(factory, "Zoom");

        var first  = await AddModelAsync(factory, brand.Id, "X1");
        var second = await AddModelAsync(factory, brand.Id, "X-1");

        Assert.NotEqual(first.UrlName, second.UrlName);
    }

    /// <summary>
    /// A name with nothing sluggable still yields an address. An unreadable one beats an
    /// unreachable page, and refusing the name outright would be worse than either.
    /// </summary>
    [Fact]
    public async Task A_name_with_nothing_sluggable_still_gets_an_address()
    {
        var factory = CreateFactory();
        var brand = await AddBrandAsync(factory, "日本語");

        Assert.False(string.IsNullOrWhiteSpace(brand.UrlName));
    }

    // ── Renaming ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The deliberate difference from every other slug in the app: correcting the name corrects the
    /// address, so the typo does not survive in the URL.
    /// </summary>
    [Fact]
    public async Task Correcting_a_make_corrects_its_address()
    {
        var factory = CreateFactory();
        var brand = await AddBrandAsync(factory, "Sansung");

        Assert.Equal("sansung", brand.UrlName);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.EquipmentBrands.FirstAsync(b => b.Id == brand.Id);
            tracked.Name = "Samsung";
            await EquipmentCatalogSlugs.AssignAsync(db, tracked, default);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("samsung", (await check.EquipmentBrands.FirstAsync(b => b.Id == brand.Id)).UrlName);
    }

    /// <summary>
    /// Re-assigning without a name change keeps the address it already had, rather than taking a
    /// suffix by colliding with itself.
    /// </summary>
    [Fact]
    public async Task Reassigning_an_unchanged_name_keeps_the_same_address()
    {
        var factory = CreateFactory();
        var brand = await AddBrandAsync(factory, "Zoom");

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.EquipmentBrands.FirstAsync(b => b.Id == brand.Id);
            await EquipmentCatalogSlugs.AssignAsync(db, tracked, default);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("zoom", (await check.EquipmentBrands.FirstAsync(b => b.Id == brand.Id)).UrlName);
    }
}
