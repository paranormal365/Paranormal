using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
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
            if (await db.StoreCategories.AnyAsync(c => c.Id == Id(shelf.N), ct))
            {
                // A shelf's picture sits under the store hero's own title, so it carries no words;
                // the first seeds drew the name in, and the hero read "EMF Meters" twice over. The
                // file name is the version: an older one is redrawn into the row (and its disk copy,
                // if a migration moved it there, let go). Changing bytes under an id breaks the
                // one-year cache promise, which is fine for a development seed and nothing else:
                // real pictures are replaced with new ids.
                var existing = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == Id(10 + shelf.N), ct);
                if (existing is not null && existing.FileName != ShelfPictureName(shelf))
                {
                    var drawn = Draw(shelf.Name, shelf.Colour, withName: false);
                    existing.FileData = drawn;
                    existing.FileSize = drawn.LongLength;
                    existing.FileName = existing.StoredFileName = ShelfPictureName(shelf);
                    existing.StoragePath = null;
                }
                continue;
            }
            var picture = Picture(db, Id(10 + shelf.N), ShelfPictureName(shelf), shelf.Name, shelf.Colour, ownerId, now, withName: false);
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

        await SeedOrdersAsync(db, now, ct);
        await SeedReviewsAsync(db, ownerId, now, ct);
        await SeedSellerDemoAsync(db, ownerId, now, ct);
    }

    // ── A member seller's items (store sellers, backlog 251) ─────────────────

    internal const string HazelEmail = "hazel.marsh@benco.dev";

    /// <summary>The demo seller's items, by fixed id.</summary>
    internal static class SeededSellerProducts
    {
        public static readonly Guid RemPod = Id(28), EmfLogger = Id(29);
    }

    /// <summary>
    /// Hazel Marsh (the roster's demo seller) makes two things: a hand-built REM pod on sale, and
    /// a pocket logger still a draft — so her workspace has one of each to show. Only when she is
    /// on this database; the store's own seven products are untouched.
    /// </summary>
    private static async Task SeedSellerDemoAsync(BenDataContext db, Guid ownerId, DateTime now, CancellationToken ct)
    {
        var hazel = await db.AppUsers.AsNoTracking().Where(u => u.NormalizedEmail == HazelEmail.ToUpperInvariant())
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (hazel is not { } seller) return;

        await ProductAsync(db, ownerId, now, n: 28, shelf: 4, "Hand-Built REM Pod", "hand-built-rem-pod", null,
            "A REM pod built by hand, one at a time, with a louder alarm and a longer range.",
            "<p>Each one is assembled and tested in a small workshop in Kentucky before it ships.</p>",
            featured: false, newUntil: null, active: true,
            options: [],
            variants: [new("HM-REMPOD", [], 149m, null, 3, Default: true)],
            specs: [("Power", "Battery", "Four AA")]);
        await ProductAsync(db, ownerId, now, n: 29, shelf: 1, "Pocket EMF Logger", "pocket-emf-logger", null,
            "A pocket meter that keeps a log of every reading.",
            "<p>Still being finished — not on sale yet.</p>",
            featured: false, newUntil: null, active: false,
            options: [],
            variants: [new("HM-EMFLOG", [], 0m, null, 0, Default: true)],
            specs: []);

        var given = await db.StoreProducts
            .Where(p => (p.Id == SeededSellerProducts.RemPod || p.Id == SeededSellerProducts.EmfLogger) && p.SellerAppUserId == null)
            .Select(p => p.Id).ToListAsync(ct);
        if (given.Count == 0) return;
        await db.StoreProducts.Where(p => given.Contains(p.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.SellerAppUserId, seller), ct);
        foreach (var id in given)
            StoreProductHistory.Record(db, id, StoreProductChangeArea.Seller, "Gave it to Hazel Marsh to sell.", ownerId, StoreChangeActor.Store, now.AddSeconds(1));
        await db.SaveChangesAsync(ct);
    }

    // ── Orders (storefront S4.12) ────────────────────────────────────────────

    internal const string SarahEmail = "sarah.mitchell@benco.dev";
    internal const string JamesEmail = "james.thornton@benco.dev";
    internal const string GuestEmail = "morgan.guest@example.com";
    internal const string EmmaEmail = "emma.rodriguez@benco.dev";

    /// <summary>The seeded orders' fixed ids, for the tests that open them.</summary>
    internal static class SeededOrders
    {
        public static readonly Guid SarahPaid = Id(101), SarahShipped = Id(102), SarahDelivered = Id(103), SarahRefunded = Id(104),
            Guest = Id(105), JamesDelivered = Id(106), SarahAbandoned = Id(107),
            // The K-II's reviewers bought one (S6.4): a review needs a paid order to hang on.
            JamesKii = Id(108), EmmaKii = Id(109);
    }

    private sealed record LineSeed(string Sku, int Quantity);

    private sealed record OrderSeed(
        Guid Id, string Email, StoreOrderStatus Status, LineSeed[] Lines, decimal Shipping, decimal TaxRate, bool TaxShipping,
        string State, string City, string Zip, int DaysAgo, bool Coupon = false, bool Refunded = false, bool Abandoned = false);

    /// <summary>
    /// Seven orders in the shapes the order pages have to draw (plan S4.12): Sarah's paid, shipped,
    /// delivered-with-a-code, refunded and abandoned checkouts; a guest's; James's delivered one —
    /// and two more (S6.4), James's and Emma's delivered K-IIs, which their reviews hang on.
    /// </summary>
    /// <remarks>
    /// <para><b>History, not sales.</b> Stock is not touched and no tax is filed: each paid order
    /// carries a <c>seed_</c> tax transaction id so StoreTaxRetryJob leaves it alone, and the
    /// abandoned one is already released so the expiry service does too.</para>
    ///
    /// <para><b>Numbers are the next free ones</b>, not #100001–#100007 as first planned: a database
    /// that has taken real (or test) orders already holds those numbers, and the number is unique.
    /// Tests find these orders by their fixed ids.</para>
    ///
    /// <para><b>The money adds up</b> the way the checkout's does: each line's tax is on its total
    /// less its discount, shipping's tax is its own figure, and Subtotal − Discount + Shipping + Tax =
    /// Total to the cent (StoreDemoSeederTests.Orders_balance_to_the_cent).</para>
    /// </remarks>
    private static async Task SeedOrdersAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        OrderSeed[] orders =
        [
            new(SeededOrders.SarahPaid, SarahEmail, StoreOrderStatus.Paid, [new("KII-EMF", 1)], 7.95m, 0.0925m, true, "TN", "Nashville", "37203", 1),
            new(SeededOrders.SarahShipped, SarahEmail, StoreOrderStatus.Shipped, [new("H1N-BLACK", 1)], 0m, 0.0925m, false, "TN", "Nashville", "37203", 6),
            new(SeededOrders.SarahDelivered, SarahEmail, StoreOrderStatus.Delivered, [new("BAG-OLV-STD", 2)], 7.95m, 0.0925m, true, "TN", "Nashville", "37203", 20, Coupon: true),
            new(SeededOrders.SarahRefunded, SarahEmail, StoreOrderStatus.Refunded, [new("PSB7-BLACK", 1)], 7.95m, 0.0925m, true, "TN", "Nashville", "37203", 35, Refunded: true),
            new(SeededOrders.Guest, GuestEmail, StoreOrderStatus.Paid, [new("KII-EMF", 1), new("BAG-BLK-STD", 1)], 0m, 0.06m, false, "KY", "Louisville", "40202", 2),
            new(SeededOrders.JamesDelivered, JamesEmail, StoreOrderStatus.Delivered, [new("BAG-BLK-LRG", 1)], 7.95m, 0.07m, false, "IN", "Indianapolis", "46204", 14),
            new(SeededOrders.SarahAbandoned, SarahEmail, StoreOrderStatus.PendingPayment, [new("KII-EMF", 1)], 7.95m, 0.0925m, true, "TN", "Nashville", "37203", 3, Abandoned: true),
            new(SeededOrders.JamesKii, JamesEmail, StoreOrderStatus.Delivered, [new("KII-EMF", 1)], 7.95m, 0.07m, false, "IN", "Indianapolis", "46204", 45),
            new(SeededOrders.EmmaKii, EmmaEmail, StoreOrderStatus.Delivered, [new("KII-EMF", 1)], 7.95m, 0.0625m, false, "TX", "Austin", "78701", 30),
        ];

        var skus = orders.SelectMany(o => o.Lines).Select(l => l.Sku).Distinct().ToList();
        var variants = await db.StoreProductVariants.AsNoTracking().Include(v => v.Product)
            .Where(v => skus.Contains(v.Sku)).ToDictionaryAsync(v => v.Sku, ct);
        var productIds = variants.Values.Select(v => v.ProductId).ToList();
        var pictures = (await db.StoreProductImages.AsNoTracking().Where(i => productIds.Contains(i.ProductId))
                .Select(i => new { i.ProductId, i.VariantId, i.UploadFileId, i.SortOrder }).ToListAsync(ct))
            .Select(i => (i.ProductId, i.VariantId, i.UploadFileId, i.SortOrder)).ToList();
        var emails = orders.Select(o => o.Email.ToUpperInvariant()).Distinct().ToList();
        var people = await db.AppUsers.AsNoTracking().Where(u => u.NormalizedEmail != null && emails.Contains(u.NormalizedEmail))
            .ToDictionaryAsync(u => u.NormalizedEmail!, ct);

        foreach (var seed in orders)
        {
            if (await db.StoreOrders.AnyAsync(o => o.Id == seed.Id, ct)) continue;
            if (seed.Lines.Any(l => !variants.ContainsKey(l.Sku))) continue;
            var buyer = seed.Email == GuestEmail ? null : people.GetValueOrDefault(seed.Email.ToUpperInvariant());
            if (seed.Email != GuestEmail && buyer is null) continue;   // that demo person is not on this database

            var placed = now.AddDays(-seed.DaysAgo);
            var order = BuildOrder(seed, buyer, variants, pictures, placed);
            db.StoreOrders.Add(order);
            await StoreOrderNumbers.SaveNumberedAsync(db, order, ct);

            if (seed.Coupon)
            {
                db.StoreCouponRedemptions.Add(new StoreCouponRedemption
                {
                    Id = Guid.NewGuid(), CouponId = CouponId, OrderId = order.Id, BuyerEmailNormalized = order.BuyerEmailNormalized,
                    BuyerAppUserId = buyer?.Id, DiscountAmount = order.DiscountAmount, RedeemedUtc = placed,
                });
                await db.StoreCoupons.Where(c => c.Id == CouponId)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.RedemptionCount, c => c.RedemptionCount + 1), ct);
                await db.SaveChangesAsync(ct);
            }
        }
    }

    // ── Reviews and favourites (storefront S6.4) ─────────────────────────────

    /// <summary>The seeded reviews' fixed ids, for the tests that find them.</summary>
    internal static class SeededReviews
    {
        public static readonly Guid JamesKii = Id(121), EmmaKii = Id(122), SarahKii = Id(123);
    }

    private sealed record ReviewSeed(Guid Id, string Email, Guid OrderId, int Rating, string Title, string Body, StoreReviewStatus Status, int DaysAgo, string? Reply = null);

    /// <summary>
    /// The K-II's reviews (plan S6.4): James's five stars and Emma's three, both published, so it
    /// shows 4.0 from two; Sarah's, still waiting for a moderator, so the review queue and "Waiting
    /// for approval" have something to show. Each hangs on the order the reviewer bought it with. A
    /// helpful vote on each published one, and two products in Sarah's favourites.
    /// </summary>
    /// <remarks>Only what is missing is added, and the product's stars are worked out from what is
    /// published, the same sum the moderators' verbs do (StoreRatingCaches).</remarks>
    private static async Task SeedReviewsAsync(BenDataContext db, Guid ownerId, DateTime now, CancellationToken ct)
    {
        var kii = await db.StoreProducts.AsNoTracking().Where(p => p.Slug == "k-ii-emf-meter").Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (kii is not { } productId) return;

        var emails = new[] { SarahEmail, JamesEmail, EmmaEmail }.Select(e => e.ToUpperInvariant()).ToList();
        var people = await db.AppUsers.AsNoTracking().Where(u => u.NormalizedEmail != null && emails.Contains(u.NormalizedEmail))
            .ToDictionaryAsync(u => u.NormalizedEmail!, u => u.Id, ct);
        Guid? Person(string email) => people.TryGetValue(email.ToUpperInvariant(), out var id) ? id : null;

        ReviewSeed[] reviews =
        [
            new(SeededReviews.JamesKii, JamesEmail, SeededOrders.JamesKii, 5, "Does one thing and does it fast",
                "Took it through the old county jail on a group walk. The lights jump the moment you pass the wiring, which is exactly why we sweep with it first — so we know where NOT to trust it later.",
                StoreReviewStatus.Approved, 40),
            new(SeededReviews.EmmaKii, EmmaEmail, SeededOrders.EmmaKii, 3, "Good meter, too twitchy near phones",
                "It reacts to everything: phones, radios, the car. Fine once the whole team puts phones in airplane mode, but nobody told us that. Battery lasted three nights.",
                StoreReviewStatus.Approved, 25,
                Reply: "Thanks, Emma — airplane mode is the right call. We've added a note about phones to the product description."),
            new(SeededReviews.SarahKii, SarahEmail, SeededOrders.SarahPaid, 5, "Our go-to first sweep",
                "Light, simple, and the team can read it from across a room in the dark.",
                StoreReviewStatus.Pending, 0),
        ];

        foreach (var seed in reviews)
        {
            if (Person(seed.Email) is not { } author) continue;                              // that demo person is not on this database
            if (await db.StoreReviews.AnyAsync(r => r.Id == seed.Id || (r.ProductId == productId && r.AuthorAppUserId == author), ct)) continue;
            if (!await db.StoreOrders.AnyAsync(o => o.Id == seed.OrderId && o.BuyerAppUserId == author, ct)) continue;

            var written = now.AddDays(-seed.DaysAgo);
            var approved = seed.Status == StoreReviewStatus.Approved;
            db.StoreReviews.Add(new StoreReview
            {
                Id = seed.Id, ProductId = productId, AuthorAppUserId = author, OrderId = seed.OrderId, Rating = seed.Rating,
                Title = seed.Title, Body = seed.Body, Status = seed.Status,
                ModeratedByAppUserId = approved ? ownerId : null, ModeratedUtc = approved ? written.AddDays(1) : null,
                AdminReply = seed.Reply, AdminReplyByAppUserId = seed.Reply is null ? null : ownerId,
                AdminRepliedUtc = seed.Reply is null ? null : written.AddDays(1),
                DateCreated = written, CreatedByAppUserId = author,
            });
        }
        await db.SaveChangesAsync(ct);

        // One helpful vote on each published review, from somebody else.
        foreach (var (reviewId, voterEmail) in new[] { (SeededReviews.JamesKii, SarahEmail), (SeededReviews.EmmaKii, JamesEmail) })
        {
            if (Person(voterEmail) is not { } voter) continue;
            if (!await db.StoreReviews.AnyAsync(r => r.Id == reviewId, ct)) continue;
            if (await db.StoreReviewVotes.AnyAsync(v => v.ReviewId == reviewId && v.AppUserId == voter, ct)) continue;
            db.StoreReviewVotes.Add(new StoreReviewVote { Id = Guid.NewGuid(), ReviewId = reviewId, AppUserId = voter, DateCreated = now });
            await db.SaveChangesAsync(ct);
            var count = await db.StoreReviewVotes.CountAsync(v => v.ReviewId == reviewId, ct);
            await db.StoreReviews.Where(r => r.Id == reviewId).ExecuteUpdateAsync(u => u.SetProperty(r => r.HelpfulCount, count), ct);
        }

        // Sarah keeps two products she has her eye on.
        if (Person(SarahEmail) is { } sarah)
        {
            var wanted = await db.StoreProducts.AsNoTracking().Where(p => p.Slug == "rem-pod" || p.Slug == "h1n-handy-recorder")
                .Select(p => p.Id).ToListAsync(ct);
            foreach (var id in wanted)
                if (!await db.StoreFavourites.AnyAsync(f => f.AppUserId == sarah && f.ProductId == id, ct))
                    db.StoreFavourites.Add(new StoreFavourite { Id = Guid.NewGuid(), AppUserId = sarah, ProductId = id, DateCreated = now });
            await db.SaveChangesAsync(ct);
        }

        await StoreRatingCaches.RecomputeAsync(db, productId, ct);
    }

    private static StoreOrder BuildOrder(OrderSeed seed, AppUser? buyer, IReadOnlyDictionary<string, StoreProductVariant> variants,
        List<(Guid ProductId, Guid? VariantId, Guid UploadFileId, int SortOrder)> pictures, DateTime placed)
    {
        var id = seed.Id;
        var n = (int)(id.ToByteArray()[^1]);
        // The name the site shows for them — the demo people have a display name and no first or
        // last name, so reading only those put an email address where the name goes on every order.
        var name = buyer is null ? "Morgan Guest"
            : !string.IsNullOrWhiteSpace(buyer.DisplayName) ? buyer.DisplayName!
            : $"{buyer.FirstName} {buyer.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = seed.Email;

        var items = seed.Lines.Select(l =>
        {
            var v = variants[l.Sku];
            var lineTotal = StoreMoney.Round(v.Price * l.Quantity);
            var discount = seed.Coupon ? StoreMoney.Round(lineTotal * 0.10m) : 0m;
            return new StoreOrderItem
            {
                Id = Guid.NewGuid(), OrderId = id, ProductId = v.ProductId, VariantId = v.Id, ProductName = v.Product.Name,
                VariantName = string.IsNullOrWhiteSpace(v.Name) ? null : v.Name, Sku = v.Sku, UnitPrice = v.Price,
                CompareAtPrice = v.CompareAtPrice is { } was && was > v.Price ? was : null, Quantity = l.Quantity, LineTotal = lineTotal, LineDiscount = discount,
                TaxAmount = StoreMoney.Round((lineTotal - discount) * seed.TaxRate), StripeTaxCode = v.Product.StripeTaxCode,
                QuantityRefunded = seed.Refunded ? l.Quantity : 0, QuantityRestocked = 0,
                ImageUploadFileId = pictures.Where(x => x.VariantId == v.Id).OrderBy(x => x.SortOrder).Select(x => (Guid?)x.UploadFileId).FirstOrDefault()
                                    ?? pictures.Where(x => x.ProductId == v.ProductId).OrderBy(x => x.SortOrder).Select(x => (Guid?)x.UploadFileId).FirstOrDefault(),
                DateCreated = placed,
            };
        }).ToList();

        var subtotal = items.Sum(i => i.LineTotal);
        var discountTotal = items.Sum(i => i.LineDiscount);
        var shippingTax = seed.TaxShipping ? StoreMoney.Round(seed.Shipping * seed.TaxRate) : 0m;
        var tax = items.Sum(i => i.TaxAmount) + shippingTax;
        var total = subtotal - discountTotal + seed.Shipping + tax;
        var paid = seed.Abandoned ? (DateTime?)null : placed.AddMinutes(2);

        var order = new StoreOrder
        {
            Id = id, Status = seed.Status, BuyerAppUserId = buyer?.Id, BuyerEmail = seed.Email,
            BuyerEmailNormalized = StoreEmail.Normalize(seed.Email), BuyerName = name,
            ShipName = name, ShipPhone = "615-555-01" + n.ToString("D2"), ShipStreet1 = $"{100 + n} Elm Street", ShipCity = seed.City,
            ShipState = seed.State, ShipZip = seed.Zip, ShipCountry = "US", BillingSameAsShipping = true,
            Subtotal = subtotal, DiscountAmount = discountTotal, ShippingAmount = seed.Shipping, TaxAmount = tax,
            ShippingTaxAmount = shippingTax, Total = total, RefundedAmount = seed.Refunded ? total : 0m,
            CouponId = seed.Coupon ? CouponId : null, CouponCode = seed.Coupon ? CouponCode : null,
            CartFingerprint = $"seed-{n}", StripePaymentIntentId = seed.Abandoned ? null : $"seed_pi_{n}",
            StripeChargeId = seed.Abandoned ? null : $"seed_ch_{n}", StripeTaxCalculationId = $"seed_taxcalc_{n}",
            StripeTaxTransactionId = seed.Abandoned ? null : $"seed_taxtxn_{n}",
            AccessToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ReservationExpiresUtc = placed.AddMinutes(15), ReservationReleasedUtc = seed.Abandoned ? placed.AddMinutes(16) : null,
            PlacedUtc = placed, PaidUtc = paid, DateCreated = placed, DateUpdated = placed,
            Items = items,
        };

        void Event(StoreOrderEventKind kind, DateTime at, StoreOrderStatus? to = null, string? note = null, decimal? amount = null)
            => order.Events.Add(new StoreOrderEvent { Id = Guid.NewGuid(), OrderId = id, Kind = kind, ToStatus = to, Note = note, Amount = amount, OccurredUtc = at });

        Event(StoreOrderEventKind.Placed, placed, StoreOrderStatus.PendingPayment);
        if (seed.Abandoned)
        {
            Event(StoreOrderEventKind.ReservationExpired, placed.AddMinutes(16), note: "The buyer left without paying.");
            return order;
        }
        Event(StoreOrderEventKind.PaymentSucceeded, paid!.Value, StoreOrderStatus.Paid, amount: total);

        if (seed.Status is StoreOrderStatus.Shipped or StoreOrderStatus.Delivered)
        {
            order.PackedUtc = paid.Value.AddHours(20);
            order.ShippedUtc = paid.Value.AddDays(1);
            order.Carrier = "USPS";
            order.TrackingNumber = $"9400 1000 0000 0000 0000 {n:D2}";
            order.TrackingUrl = $"https://tools.usps.com/go/TrackConfirmAction?tLabels=94001000000000000000{n:D2}";
            Event(StoreOrderEventKind.Packed, order.PackedUtc.Value, StoreOrderStatus.Packed);
            Event(StoreOrderEventKind.Shipped, order.ShippedUtc.Value, StoreOrderStatus.Shipped, note: "USPS");
        }
        if (seed.Status == StoreOrderStatus.Delivered)
        {
            order.DeliveredUtc = paid.Value.AddDays(4);
            Event(StoreOrderEventKind.Delivered, order.DeliveredUtc.Value, StoreOrderStatus.Delivered);
        }
        if (seed.Refunded)
        {
            var at = paid.Value.AddDays(3);
            order.Refunds.Add(new StoreRefund
            {
                Id = Guid.NewGuid(), OrderId = id, Amount = total, TaxReversed = tax, Reason = "Arrived damaged", Restock = false,
                Status = StoreRefundStatus.Succeeded, StripeRefundId = $"seed_re_{n}", StripeTaxReversalId = $"seed_taxrev_{n}",
                DateCreated = at, CompletedUtc = at,
            });
            Event(StoreOrderEventKind.RefundSucceeded, at, StoreOrderStatus.Refunded, note: "Arrived damaged", amount: total);
        }
        return order;
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
        // Its history opens as the admin pages would have written it (store sellers P2).
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Created, "Created it.", ownerId, StoreChangeActor.Store, now);
        if (active)
            StoreProductHistory.Record(db, productId, StoreProductChangeArea.Sale, "Put it on sale.", ownerId, StoreChangeActor.Store, now.AddSeconds(2));

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

    private const int PictureWidth = 1200, PictureHeight = 900;

    /// <summary>A shelf picture's file name, which is also its version — see the repaint above.</summary>
    private static string ShelfPictureName(Shelf shelf) => $"{shelf.Slug}-shelf-v2.jpg";

    /// <summary>
    /// A 1200×900 picture: the shelf's colour and a few rings (a dial, a speaker's cone — enough to
    /// read as a picture rather than a flat box), with the product's name when <paramref name="withName"/>.
    /// Deterministic, so a re-run can tell whether a stored picture is today's drawing.
    /// </summary>
    private static byte[] Draw(string name, SKColor colour, bool withName)
    {
        using var bitmap = new SKBitmap(PictureWidth, PictureHeight);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var shade = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(PictureWidth, PictureHeight),
                [colour, Darker(colour)], SKShaderTileMode.Clamp);
            using var fill = new SKPaint { Shader = shade };
            canvas.DrawRect(0, 0, PictureWidth, PictureHeight, fill);

            using var ring = new SKPaint { Color = new SKColor(255, 255, 255, 28), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 18 };
            for (var r = 140; r <= 620; r += 120) canvas.DrawCircle(PictureWidth * 0.72f, PictureHeight * 0.62f, r, ring);

            if (withName)
            {
                using var font = new SKFont(SKTypeface.Default, 72);
                using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
                canvas.DrawText(name, PictureWidth / 2f, PictureHeight / 2f + 24, SKTextAlign.Center, font, ink);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 82);
        return jpeg.ToArray();
    }

    /// <summary>Draws a picture and files it as a store image, bytes in the row.</summary>
    private static Guid Picture(BenDataContext db, Guid id, string fileName, string caption, SKColor colour, Guid ownerId, DateTime now,
        bool withName = true)
    {
        const int width = PictureWidth, height = PictureHeight;
        var bytes = Draw(caption, colour, withName);

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
