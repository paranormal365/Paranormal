using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// A small store to open the screens on (storefront S1.12): five shelves, seven products with the
/// shapes every store screen has to handle, and one discount code.
/// </summary>
/// <remarks>
/// <para><b>Each product is a case.</b> A featured meter with an old price and specifications; a
/// new arrival down to its last two; a spirit box whose Camo is sold out and has its own picture; a
/// recorder in two colours; a product that exists but is hidden; a field bag in four variants that
/// every buying browser test purchases (and no other fixture reads the stock of); and a probe with
/// exactly one unit, which only the refusal test touches.</para>
///
/// <para><b>Pictures are drawn, not downloaded</b> — 1200×900 JPEGs made here with SkiaSharp and
/// kept in the row (<c>FileData</c>), each with its metadata row, as ownerless store images that
/// never expire. A seed that fetched stock photos would depend on somebody else's server and
/// somebody else's licence.</para>
///
/// <para><b>Development only</b>, behind <c>SeedData:DevData:Enabled</c>. Every block is
/// idempotent on its own fixed id, so a database that has some of this gains only what it lacks.
/// Tests drive <see cref="SeedCoreAsync"/> directly.</para>
/// </remarks>
internal static class StoreDemoSeeder
{
    private static Guid Id(int n) => new($"a1000000-0000-0000-0000-{n:D12}");

    internal static readonly Guid CouponId = Id(90);
    internal const string CouponCode = "GHOST10";

    private sealed record Shelf(int N, string Name, string Slug, string Description, SKColor Colour);

    private static readonly Shelf[] Shelves =
    [
        new(1, "EMF Meters", "emf-meters", "Meters that read electromagnetic fields — the first tool out of the bag.", new SKColor(33, 76, 120)),
        new(2, "Spirit Boxes", "spirit-boxes", "Radio-sweep devices for EVP and real-time sessions.", new SKColor(92, 45, 110)),
        new(3, "Audio Recorders", "audio-recorders", "Recorders that catch what the room says when nobody is talking.", new SKColor(30, 100, 80)),
        new(4, "Trigger Objects", "trigger-objects", "Devices that alarm when something disturbs them.", new SKColor(130, 60, 30)),
        new(5, "Field Accessories", "field-accessories", "Bags, cases and the small things that make a night go smoothly.", new SKColor(70, 70, 70)),
    ];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        if (!config.GetValue<bool>("SeedData:DevData:Enabled")) return;
        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var owner = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(ownerEmail);
        if (owner is null) return;

        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>().CreateDbContextAsync();
        await SeedCoreAsync(db, owner.Id, default);
    }

    /// <summary>The seed itself, for the host and for tests.</summary>
    internal static async Task SeedCoreAsync(BenDataContext db, Guid ownerId, CancellationToken ct)
    {
        if (!await db.UploadFileTypes.AnyAsync(t => t.Id == UploadFileTypeSeeder.StoreImageFileTypeId, ct))
        {
            Console.WriteLine("[StoreDemoSeeder] The Store Image file type is missing — skipping the demo store.");
            return;
        }
        var now = DateTime.UtcNow;

        foreach (var shelf in Shelves)
        {
            if (await db.StoreCategories.AnyAsync(c => c.Id == Id(shelf.N), ct)) continue;
            var picture = Picture(db, Id(10 + shelf.N), $"{shelf.Slug}.jpg", shelf.Name, shelf.Colour, ownerId, now);
            db.StoreCategories.Add(new StoreCategory
            {
                Id = Id(shelf.N), Name = shelf.Name, Slug = shelf.Slug, Description = shelf.Description,
                ImageUploadFileId = picture, SortOrder = shelf.N, IsActive = true,
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
        }
        await db.SaveChangesAsync(ct);

        var models = await db.EquipmentModels.AsNoTracking()
            .Where(m => m.Name == "K-II EMF Meter" || m.Name == "REM-Pod" || m.Name == "P-SB7 Spirit Box" || m.Name == "Boo Buddy")
            .ToDictionaryAsync(m => m.Name, m => m.Id, ct);
        Guid? Model(string name) => models.TryGetValue(name, out var id) ? id : null;

        // ── K-II: featured, an old price, specifications ─────────────────────
        await ProductAsync(db, ownerId, now, n: 21, shelf: 1, "K-II EMF Meter", "k-ii-emf-meter", Model("K-II EMF Meter"),
            "Five LEDs, instant response — the meter most people picture when they hear EMF.",
            "<p>The K-II lights one to five LEDs as the field around it changes. There is no logging and no screen to read in the dark — just a fast, unmistakable signal that something nearby has changed.</p><p>Runs on one 9V battery.</p>",
            featured: true, newUntil: null, active: true,
            options: [],
            variants: [new("KII-EMF", [], 59.99m, 69.99m, 12, Default: true)],
            specs: [("Detection", "Range", "50 Hz – 20 kHz"), ("Detection", "Scale", "Five LEDs, 0–20 mG"), ("Power", "Battery", "One 9V, included")]);

        // ── REM Pod: new, down to its last two ───────────────────────────────
        await ProductAsync(db, ownerId, now, n: 22, shelf: 4, "REM Pod", "rem-pod", Model("REM-Pod"),
            "Radiates its own field and sounds when anything disturbs it.",
            "<p>A telescoping antenna radiates a small field; anything that disturbs it sets off the lights and the alarm. Temperature changes are called out too.</p>",
            featured: false, newUntil: now.AddDays(60), active: true,
            options: [],
            variants: [new("REM-POD", [], 189m, null, 2, Default: true)],
            specs: [("Detection", "Sensors", "EM field, temperature"), ("Power", "Battery", "Four AA")]);

        // ── P-SB7: a swatch option; Camo sold out, with its own picture ───────
        await ProductAsync(db, ownerId, now, n: 23, shelf: 2, "P-SB7 Spirit Box", "p-sb7-spirit-box", Model("P-SB7 Spirit Box"),
            "The standard radio-sweep box, at the speed you choose.",
            "<p>Sweeps AM or FM at an adjustable rate, with a line out for a speaker or a recorder.</p>",
            featured: false, newUntil: null, active: true,
            options: [new("Colour", StoreOptionKind.Swatch, [("Black", "#1b1b1b"), ("Camo", "#5b6b3a")])],
            variants: [new("PSB7-BLACK", ["Black"], 79.99m, null, 8, Default: true), new("PSB7-CAMO", ["Camo"], 84.99m, null, 0, Default: false, OwnPicture: true)],
            specs: [("Radio", "Bands", "AM and FM"), ("Radio", "Sweep", "50–350 ms")]);

        // ── H1n: two colours, both in stock ──────────────────────────────────
        await ProductAsync(db, ownerId, now, n: 24, shelf: 3, "H1n Handy Recorder", "h1n-handy-recorder", null,
            "Stereo recorder small enough to leave running on a shelf.",
            "<p>Records to microSD at up to 96 kHz, with a one-touch start and a low-cut filter for rooms with a hum.</p>",
            featured: false, newUntil: null, active: true,
            options: [new("Colour", StoreOptionKind.Swatch, [("Black", "#1b1b1b"), ("Grey", "#8a8f94")])],
            variants: [new("H1N-BLACK", ["Black"], 99.99m, null, 5, Default: true), new("H1N-GREY", ["Grey"], 99.99m, null, 3, Default: false)],
            specs: [("Recording", "Formats", "WAV up to 96 kHz / 24-bit, MP3")]);

        // ── Boo Buddy: exists, but hidden ────────────────────────────────────
        await ProductAsync(db, ownerId, now, n: 25, shelf: 4, "Boo Buddy", "boo-buddy", Model("Boo Buddy"),
            "A trigger-object bear that speaks when its sensors trip.",
            "<p>Made for cases involving children. Not on sale yet.</p>",
            featured: false, newUntil: null, active: false,
            options: [],
            variants: [new("BOO-BUDDY", [], 64.99m, null, 4, Default: true)],
            specs: []);

        // ── Field bag: four variants; the purchase product of every buying test ──
        await ProductAsync(db, ownerId, now, n: 26, shelf: 5, "Investigator's Field Bag", "investigators-field-bag", null,
            "A padded shoulder bag with room for a meter, a recorder and a night's batteries.",
            "<p>Padded dividers, a water-resistant base and a pocket that fits a phone in a glove.</p>",
            featured: false, newUntil: null, active: true,
            options: [new("Colour", StoreOptionKind.Swatch, [("Black", "#1b1b1b"), ("Olive", "#556b2f")]), new("Size", StoreOptionKind.Pill, [("Standard", null), ("Large", null)])],
            variants:
            [
                new("BAG-BLK-STD", ["Black", "Standard"], 39m, null, 20, Default: true),
                new("BAG-BLK-LRG", ["Black", "Large"], 49m, null, 20, Default: false),
                new("BAG-OLV-STD", ["Olive", "Standard"], 39m, null, 20, Default: false),
                new("BAG-OLV-LRG", ["Olive", "Large"], 49m, null, 20, Default: false),
            ],
            specs: [("Size", "Standard", "30 × 22 × 12 cm"), ("Size", "Large", "38 × 26 × 15 cm")]);

        // ── One unit, for the refusal test alone ─────────────────────────────
        await ProductAsync(db, ownerId, now, n: 27, shelf: 5, "Single-Unit Probe", "single-unit-probe", null,
            "A test product with exactly one in stock.",
            "<p>Kept at one unit so a second buyer can be refused.</p>",
            featured: false, newUntil: null, active: true,
            options: [],
            variants: [new("SINGLE-UNIT-PROBE", [], 9.99m, null, 1, Default: true)],
            specs: []);

        if (!await db.StoreCoupons.AnyAsync(c => c.Id == CouponId, ct))
        {
            db.StoreCoupons.Add(new StoreCoupon
            {
                Id = CouponId, Code = CouponCode, Name = "Ten percent off (demo)", Kind = StoreCouponKind.Percent,
                PercentOff = 10, MaxRedemptionsPerBuyer = null, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed record OptionSeed(string Name, StoreOptionKind Kind, (string Value, string? Hex)[] Values);

    private sealed record VariantSeed(string Sku, string[] Values, decimal Price, decimal? Was, int Stock, bool Default, bool OwnPicture = false);

    private static async Task ProductAsync(
        BenDataContext db, Guid ownerId, DateTime now, int n, int shelf, string name, string slug, Guid? modelId,
        string shortDescription, string html, bool featured, DateTime? newUntil, bool active,
        OptionSeed[] options, VariantSeed[] variants, (string Group, string Name, string Value)[] specs)
    {
        var productId = Id(n);
        if (await db.StoreProducts.AnyAsync(p => p.Id == productId)) return;
        var colour = Shelves.Single(s => s.N == shelf).Colour;

        db.StoreProducts.Add(new StoreProduct
        {
            Id = productId, CategoryId = Id(shelf), EquipmentModelId = modelId, Name = name, Slug = slug,
            ShortDescription = shortDescription, LongDescriptionHtml = html, IsActive = active, IsFeatured = featured,
            NewUntilUtc = newUntil, SortOrder = n, DateCreated = now, CreatedByAppUserId = ownerId,
        });

        // Options and their values, with ids derived from the product's so a re-run is stable.
        var valueIds = new Dictionary<string, Guid>();
        for (var o = 0; o < options.Length; o++)
        {
            var optionId = new Guid($"a1000000-0000-0000-{n:D4}-{o + 1:D12}");
            db.StoreProductOptions.Add(new StoreProductOption
            {
                Id = optionId, ProductId = productId, Name = options[o].Name, Kind = options[o].Kind, SortOrder = o,
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
            for (var v = 0; v < options[o].Values.Length; v++)
            {
                var valueId = new Guid($"a1000000-0000-{o + 1:D4}-{n:D4}-{v + 1:D12}");
                valueIds[options[o].Values[v].Value] = valueId;
                db.StoreProductOptionValues.Add(new StoreProductOptionValue
                {
                    Id = valueId, OptionId = optionId, Value = options[o].Values[v].Value, SwatchHex = options[o].Values[v].Hex,
                    SortOrder = v, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
                });
            }
        }

        db.StoreProductImages.Add(new StoreProductImage
        {
            Id = new Guid($"a1000000-0000-0000-{n:D4}-{100:D12}"), ProductId = productId,
            UploadFileId = Picture(db, new Guid($"a1000000-0000-0000-{n:D4}-{200:D12}"), $"{slug}.jpg", name, colour, ownerId, now),
            SortOrder = 0, AltText = name, DateCreated = now, CreatedByAppUserId = ownerId,
        });

        for (var i = 0; i < variants.Length; i++)
        {
            var seed = variants[i];
            var variantId = new Guid($"a1000000-0000-0000-{n:D4}-{300 + i:D12}");
            var chosen = seed.Values.Select(v => valueIds[v]).ToList();
            db.StoreProductVariants.Add(new StoreProductVariant
            {
                Id = variantId, ProductId = productId, Sku = seed.Sku, OptionSignature = StorePriceCaches.Signature(chosen),
                Price = seed.Price, CompareAtPrice = seed.Was, StockOnHand = seed.Stock, IsActive = true, IsDefault = seed.Default,
                SortOrder = i, DateCreated = now, CreatedByAppUserId = ownerId,
            });
            foreach (var valueId in chosen)
                db.StoreProductVariantOptionValues.Add(new StoreProductVariantOptionValue
                {
                    Id = Guid.NewGuid(), VariantId = variantId, OptionValueId = valueId, DateCreated = now,
                });
            if (seed.Stock > 0)
                db.StoreStockMovements.Add(new StoreStockMovement
                {
                    Id = Guid.NewGuid(), VariantId = variantId, Delta = seed.Stock, QuantityAfter = seed.Stock,
                    Reason = StoreStockReason.Received, Note = "Demo stock", ActorAppUserId = ownerId, OccurredUtc = now,
                });
            if (seed.OwnPicture)
                db.StoreProductImages.Add(new StoreProductImage
                {
                    Id = new Guid($"a1000000-0000-0000-{n:D4}-{400 + i:D12}"), ProductId = productId, VariantId = variantId,
                    UploadFileId = Picture(db, new Guid($"a1000000-0000-0000-{n:D4}-{500 + i:D12}"), $"{slug}-{seed.Sku.ToLowerInvariant()}.jpg",
                        $"{name} — {string.Join(" / ", seed.Values)}", Darker(colour), ownerId, now),
                    SortOrder = 1 + i, AltText = $"{name}, {string.Join(" / ", seed.Values)}", DateCreated = now, CreatedByAppUserId = ownerId,
                });
        }

        var order = 0;
        foreach (var (group, specName, value) in specs)
            db.StoreProductSpecs.Add(new StoreProductSpec
            {
                Id = Guid.NewGuid(), ProductId = productId, GroupName = group, Name = specName, Value = value,
                SortOrder = order++, DateCreated = now, CreatedByAppUserId = ownerId,
            });

        await db.SaveChangesAsync();
        await StorePriceCaches.RecomputeAsync(db, productId);
    }

    private static SKColor Darker(SKColor c) => new((byte)(c.Red * 0.6), (byte)(c.Green * 0.6), (byte)(c.Blue * 0.6));

    /// <summary>Draws a 1200×900 card with the name on it and files it as a store image, bytes in the row.</summary>
    private static Guid Picture(BenDataContext db, Guid id, string fileName, string caption, SKColor colour, Guid ownerId, DateTime now)
    {
        const int width = 1200, height = 900;
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var shade = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                [colour, Darker(colour)], SKShaderTileMode.Clamp);
            using var fill = new SKPaint { Shader = shade };
            canvas.DrawRect(0, 0, width, height, fill);

            using var font = new SKFont(SKTypeface.Default, 72);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText(caption, width / 2f, height / 2f + 24, SKTextAlign.Center, font, ink);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 82);
        var bytes = jpeg.ToArray();

        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = UploadFileTypeSeeder.StoreImageFileTypeId, AppUserId = null, OwnerOrganizationId = null,
            FileName = fileName, StoredFileName = fileName, ContentType = "image/jpeg", FileSize = bytes.LongLength,
            FileData = bytes, IsPublic = true, ExpiresAtUtc = null, DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = id, MediaKind = "Image", WidthPixels = width, HeightPixels = height, ExtractedAtUtc = now,
        });
        return id;
    }
}
