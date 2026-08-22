using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds the global equipment category taxonomy — flat, SuperAdmin-maintained, ships in the same
/// commit as the equipment tables it feeds. Without this, the category picker on every equipment
/// form is empty on first deploy and every save is rejected — the same trap
/// <see cref="ContactTypeSeeder"/>'s own doc comment describes: "a feature that is dead on
/// arrival for every existing deployment is not much of a feature." Idempotent by name, safe to
/// run on every startup.
/// </summary>
/// <remarks>
/// Also seeds one "Generic / Unbranded" <see cref="EquipmentBrand"/> with one generic
/// <see cref="EquipmentModel"/> per category. Not every piece of gear has a real manufacturer —
/// a homemade trigger device or an unbranded flashlight still needs somewhere to live — and
/// forcing a made-up brand name into the accumulating public catalog would pollute it. This gives
/// every category an immediately usable "I don't know/care about the brand" entry without any
/// nullable alternate path through the item schema: it is just an ordinary model, so it sorts,
/// searches, and moderates exactly like every name-brand one a user later proposes alongside it.
/// </remarks>
internal static class EquipmentTaxonomySeeder
{
    private const string GenericBrandName = "Generic / Unbranded";
    private static readonly (string Name, string Description, string IconClass)[] _categories =
    [
        ("Audio Recorder",           "Digital recorders used to capture EVP and ambient audio.", "bi bi-mic"),
        ("Video Camera",             "Standard and night-vision video cameras.", "bi bi-camera-video"),
        ("Still Camera",             "Photo cameras, including full-spectrum and infrared.", "bi bi-camera"),
        ("EMF Meter",                "Electromagnetic field detectors.", "bi bi-broadcast"),
        ("Thermal Imaging",          "Thermal/infrared cameras and imagers.", "bi bi-thermometer-half"),
        ("Environmental Sensor",     "Temperature, humidity, and pressure sensors.", "bi bi-cloud-sun"),
        ("Motion / Vibration Sensor", "Motion detectors and vibration sensors.", "bi bi-arrows-move"),
        ("REM-Pod / Trigger Device", "Self-contained radiating electromagnetic field devices and other trigger objects.", "bi bi-lightning"),
        ("Spirit Box",               "Radio-sweep devices used for real-time communication attempts.", "bi bi-soundwave"),
        ("Lighting / IR Illuminator", "Flashlights, IR illuminators, and other supplemental lighting.", "bi bi-brightness-high"),
        ("Communications",           "Two-way radios and other team communication gear.", "bi bi-walkie-talkie"),
        ("Power & Batteries",        "Battery packs, chargers, and power distribution.", "bi bi-battery-charging"),
        ("Tripods & Mounts",         "Tripods, mounts, and rigging for cameras and sensors.", "bi bi-camera-reels"),
        ("Computers & Software",     "Laptops, tablets, and analysis software.", "bi bi-laptop"),
        ("Protective / Utility",     "Flashlights, first-aid, and general field utility gear.", "bi bi-toolbox"),
        ("Other",                    "Gear that doesn't fit an existing category yet.", "bi bi-question-circle"),
    ];


    // ── The real catalog (launch seed, 2026-08-22) ───────────────────────────
    // Every entry is a genuine product the hobby actually uses, so a clean launch database
    // starts with a catalog worth browsing instead of one Generic brand and a moderation queue.
    // Names and model numbers are as the manufacturers print them; descriptions say what the
    // thing does in the field, not marketing copy. All name-matched and idempotent, so a
    // deployment that accumulated its own entries keeps them.

    private static readonly string[] _realBrands =
    [
        "K-II Enterprises", "DAS Distribution", "Digital Dowsing", "GhostStop", "AlphaLab",
        "GQ Electronics", "Extech", "Fluke", "FLIR", "Seek Thermal",
        "Zoom", "Tascam", "Sony", "Olympus", "Panasonic",
        "RadioShack", "Wyze", "GoPro", "SiOnyx", "Tendelux",
        "ThermoPro", "Govee", "Motorola", "BaoFeng", "Anker",
        "Manfrotto", "Joby", "Pelican", "Streamlight",
    ];

    /// <summary>(Category, Brand, Model, ModelNumber, Description) — see the block comment above.</summary>
    private static readonly (string Category, string Brand, string Model, string? Number, string Description)[] _realModels =
    [
        // ── EMF meters ──
        ("EMF Meter", "K-II Enterprises", "K-II EMF Meter", "K-II",
         "The five-LED meter most people picture when they hear EMF. Instant response, no logging — a first-alert device."),
        ("EMF Meter", "DAS Distribution", "Mel-8704R", "8704R",
         "Gary Galka's combined EMF and ambient-temperature meter, designed for investigation work with a red backlight for the dark."),
        ("EMF Meter", "AlphaLab", "TriField TF2", "TF2",
         "Lab-grade AC magnetic, electric and RF measurement in one meter. The one to reach for when a K-II spike needs a second opinion."),
        ("EMF Meter", "GhostStop", "EDI+ Meter", "EDI+",
         "EMF, temperature, pressure, humidity and vibration in one unit, with on-board data logging for later graphing."),
        ("EMF Meter", "GQ Electronics", "EMF-390", "EMF-390",
         "Budget tri-field meter with logging and a spectrum display; popular for baseline sweeps."),

        // ── Spirit boxes ──
        ("Spirit Box", "DAS Distribution", "P-SB7 Spirit Box", "P-SB7 Rev4",
         "The standard radio-sweep box: AM/FM scanned at adjustable speed, audio out to a speaker or recorder."),
        ("Spirit Box", "DAS Distribution", "P-SB11 Spirit Box", "P-SB11",
         "Dual simultaneous sweeps (AM+FM) with adjustable gap and noise-cancel modes; the P-SB7's bigger sibling."),
        ("Spirit Box", "RadioShack", "12-587 (Hack Box)", "12-587",
         "The classic modified RadioShack scanner — mute-pin disabled so it sweeps continuously. Long discontinued; still traded and used."),

        // ── REM-Pods and trigger devices ──
        ("REM-Pod / Trigger Device", "DAS Distribution", "REM-Pod", "REM-EMT",
         "Radiates its own EM field from a telescoping antenna and alarms on disturbance, with ambient-temperature deviation alerts."),
        ("REM-Pod / Trigger Device", "GhostStop", "Boo Buddy", null,
         "Trigger-object teddy bear that speaks when its EMF, temperature or motion sensors trip. Made for cases involving children."),

        // ── ITC and team communications ──
        ("Communications", "Digital Dowsing", "Ovilus V", "Ovilus 5",
         "Bill Chappell's word-bank ITC device: environmental readings select words from an internal dictionary, spoken and displayed."),
        ("Communications", "Digital Dowsing", "Paranormal Puck 2", "Puck 2",
         "Environmental sensor platform that streams readings to a phone and renders words from changes; the Ovilus's data-first sibling."),
        ("Communications", "Motorola", "Talkabout T470", "T470",
         "License-free two-way radios for keeping split teams in contact across a large site."),
        ("Communications", "BaoFeng", "UV-5R", "UV-5R",
         "Cheap programmable handheld transceiver; common team radio where somebody holds the licence."),

        // ── Audio recorders ──
        ("Audio Recorder", "Zoom", "H1n Handy Recorder", "H1n",
         "Pocket X/Y stereo recorder; the default first EVP recorder for many groups."),
        ("Audio Recorder", "Zoom", "H4n Pro", "H4n Pro",
         "Four-track recorder with XLR inputs — run static room mics and a handheld pair at once."),
        ("Audio Recorder", "Tascam", "DR-05X", "DR-05X",
         "Omnidirectional stereo recorder with clean preamps; a workhorse for room captures."),
        ("Audio Recorder", "Tascam", "DR-40X", "DR-40X",
         "Adjustable X/Y-A/B mics plus XLR inputs, four tracks; a static-session hub."),
        ("Audio Recorder", "Sony", "PCM-A10", "PCM-A10",
         "High-resolution recorder with adjustable mics and USB output; excellent low noise floor for quiet rooms."),
        ("Audio Recorder", "Sony", "ICD-PX470", "ICD-PX470",
         "Entry-level voice recorder that punches above its price for EVP work; runs forever on AAA cells."),
        ("Audio Recorder", "Olympus", "WS-853", "WS-853",
         "Simple stereo voice recorder with strong battery life; a common handout unit for guest investigators."),
        ("Audio Recorder", "Panasonic", "RR-DR60", "RR-DR60",
         "The legendary 1990s IC recorder claimed to be unusually EVP-prone. Discontinued; sells second-hand for extraordinary money."),

        // ── Video ──
        ("Video Camera", "GhostStop", "Full Spectrum POV Cam", null,
         "Compact full-spectrum camcorder capturing UV through IR, made for body-worn and static use with IR lighting."),
        ("Video Camera", "Wyze", "Wyze Cam v3", "v3",
         "Cheap wired camera with genuinely good night vision — groups scatter several as static coverage and record centrally."),
        ("Video Camera", "GoPro", "HERO12 Black", "HERO12",
         "Action camera for walkthrough POV; pair with an IR conversion or lighting for dark interiors."),
        ("Video Camera", "SiOnyx", "Aurora Pro", "Aurora Pro",
         "True digital night-vision camera that films full colour by moonlight; no IR illuminator needed outdoors."),

        // ── Still photography ──
        ("Still Camera", "GhostStop", "Full Spectrum Digital Camera", null,
         "Point-and-shoot converted to pass UV through IR, for stills matching the full-spectrum video rig."),

        // ── Thermal ──
        ("Thermal Imaging", "FLIR", "ONE Pro", "ONE Pro",
         "Phone-attached thermal camera; enough resolution to chase cold spots without carrying a second device."),
        ("Thermal Imaging", "FLIR", "C5", "C5",
         "Pocket standalone thermal camera with Wi-Fi export; the step up from phone attachments."),
        ("Thermal Imaging", "FLIR", "TG165-X", "TG165-X",
         "Spot thermometer with a thermal image behind the number — quick wall and window surveys."),
        ("Thermal Imaging", "Seek Thermal", "CompactPRO", "CompactPRO",
         "High-resolution phone-attached thermal imager; FLIR ONE's main rival in the field."),

        // ── Environmental logging ──
        ("Environmental Sensor", "Extech", "RHT10 Datalogger", "RHT10",
         "USB humidity and temperature logger — leave one per room and graph the whole night afterwards."),
        ("Environmental Sensor", "Fluke", "62 MAX+", "62 MAX+",
         "Rugged IR spot thermometer for fast surface readings; survives being dropped in the dark."),
        ("Environmental Sensor", "ThermoPro", "TP49 Hygrometer", "TP49",
         "Tiny digital temperature and humidity display; cheap enough to leave one in every room on camera."),
        ("Environmental Sensor", "Govee", "H5075 Thermometer", "H5075",
         "Bluetooth temperature/humidity logger with phone export — baseline data with no wiring."),

        // ── Motion and vibration ──
        ("Motion / Vibration Sensor", "GhostStop", "Geophone", null,
         "Seismic vibration detector with an LED ladder — footsteps and knocks register as light, silently, on camera."),

        // ── Lighting ──
        ("Lighting / IR Illuminator", "Tendelux", "AI4 IR Illuminator", "AI4",
         "Wide-angle 850nm infrared floodlight; makes night-vision cameras actually see a whole room."),
        ("Lighting / IR Illuminator", "GhostStop", "Laser Grid GS1", "GS1",
         "Green laser matrix pen — project a dot grid across a hall and any movement breaks the pattern on camera."),
        ("Lighting / IR Illuminator", "Streamlight", "ProTac HL-X", "HL-X",
         "1,000-lumen duty flashlight with a low mode that will not wreck night vision."),

        // ── Power, support, cases ──
        ("Power & Batteries", "Anker", "PowerCore 10000", "A1263",
         "Pocket USB battery bank — keeps phones, IR lights and Wyze cams alive through a long night."),
        ("Tripods & Mounts", "Manfrotto", "Compact Action", "MKCOMPACTACN",
         "Light aluminium tripod with a joystick head; fast to reposition between rooms in the dark."),
        ("Tripods & Mounts", "Joby", "GorillaPod 3K", "3K",
         "Flexible legs wrap railings and door frames — static cameras where no tripod stands."),
        ("Protective / Utility", "Pelican", "1510 Case", "1510",
         "Carry-on-sized waterproof hard case; the standard way a group's kit travels and survives."),
    ];

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        var owner = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        await SeedIntoAsync(db, owner.Id);
    }

    /// <summary>
    /// The DB-only half of seeding, split out from <see cref="SeedAsync"/> so tests can exercise
    /// idempotency directly against an in-memory context without standing up the full
    /// UserManager/DI stack — the same reasoning <c>UploadFileTypeSeeder.SeedFileTypeAsync</c>
    /// is split out for, just without needing reflection since this one is internal, not private.
    /// </summary>
    internal static async Task SeedIntoAsync(BenDataContext db, Guid ownerId)
    {
        var now = DateTime.UtcNow;

        var existingNames = await db.EquipmentCategories.Select(c => c.Name).ToHashSetAsync();

        var sort = 1;
        var categoryAdded = false;
        foreach (var (name, description, iconClass) in _categories)
        {
            if (!existingNames.Contains(name))
            {
                db.EquipmentCategories.Add(new EquipmentCategory
                {
                    Id                 = Guid.NewGuid(),
                    Name               = name,
                    Description        = description,
                    IconClass          = iconClass,
                    SortOrder          = sort,
                    IsActive           = true,
                    DateCreated        = now,
                    CreatedByAppUserId = ownerId,
                });
                categoryAdded = true;
            }
            sort++;
        }
        if (categoryAdded) await db.SaveChangesAsync();

        // ── Generic / Unbranded brand + one generic model per category ──────────
        var genericBrand = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Name == GenericBrandName);
        if (genericBrand is null)
        {
            genericBrand = new EquipmentBrand
            {
                Id                  = Guid.NewGuid(),
                Name                = GenericBrandName,
                IsApproved          = true,
                ApprovedByAppUserId = ownerId,
                DateApproved        = now,
                DateCreated         = now,
                CreatedByAppUserId  = ownerId,
            };
            db.EquipmentBrands.Add(genericBrand);
            await Services.EquipmentCatalogSlugs.AssignAsync(db, genericBrand, default);
            await db.SaveChangesAsync();
        }

        var categories = await db.EquipmentCategories
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
        var existingGenericModelCategoryIds = await db.EquipmentModels
            .Where(m => m.EquipmentBrandId == genericBrand.Id)
            .Select(m => m.EquipmentCategoryId)
            .ToHashSetAsync();

        var modelAdded = false;
        foreach (var category in categories)
        {
            if (existingGenericModelCategoryIds.Contains(category.Id)) continue;
            var genericModel = new EquipmentModel
            {
                Id                  = Guid.NewGuid(),
                EquipmentBrandId    = genericBrand.Id,
                EquipmentCategoryId = category.Id,
                // The category name is naturally unique within this one brand, since a category
                // gets at most one generic model — "Audio Recorder", "EMF Meter", and so on.
                Name                = category.Name,
                Description         = "Use when the real brand and model aren't known or don't matter.",
                IsApproved          = true,
                ApprovedByAppUserId = ownerId,
                DateApproved        = now,
                DateCreated         = now,
                CreatedByAppUserId  = ownerId,
            };
            db.EquipmentModels.Add(genericModel);
            await Services.EquipmentCatalogSlugs.AssignAsync(db, genericModel, default);
            modelAdded = true;
        }
        if (modelAdded) await db.SaveChangesAsync();

        await SeedRealCatalogAsync(db, ownerId, now);

        await BackfillSlugsAsync(db);
    }

    /// <summary>Adds the real brands and models above, by name, never touching existing rows.</summary>
    /// <remarks>
    /// Approved on arrival: this is SuperAdmin-curated launch data, not a user proposal, so it
    /// skips the moderation queue the accumulating catalog uses. A brand or model a deployment
    /// already has — including one a user proposed under the same name — is left exactly as it
    /// is, edits, approval state and all.
    /// </remarks>
    private static async Task SeedRealCatalogAsync(BenDataContext db, Guid ownerId, DateTime now)
    {
        var brandsByName = (await db.EquipmentBrands.ToListAsync())
            .ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in _realBrands)
        {
            if (brandsByName.ContainsKey(name)) continue;
            var brand = new EquipmentBrand
            {
                Id = Guid.NewGuid(), Name = name,
                IsApproved = true, ApprovedByAppUserId = ownerId, DateApproved = now,
                DateCreated = now, CreatedByAppUserId = ownerId,
            };
            db.EquipmentBrands.Add(brand);
            await Services.EquipmentCatalogSlugs.AssignAsync(db, brand, default);
            brandsByName[name] = brand;
        }
        await db.SaveChangesAsync();

        var categoriesByName = (await db.EquipmentCategories.ToListAsync())
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var existingModels = (await db.EquipmentModels.Select(m => new { m.EquipmentBrandId, m.Name }).ToListAsync())
            .Select(m => (m.EquipmentBrandId, m.Name.ToLowerInvariant()))
            .ToHashSet();

        var added = 0;
        foreach (var (categoryName, brandName, modelName, number, description) in _realModels)
        {
            if (!categoriesByName.TryGetValue(categoryName, out var category)) continue;
            if (!brandsByName.TryGetValue(brandName, out var brand)) continue;
            if (existingModels.Contains((brand.Id, modelName.ToLowerInvariant()))) continue;

            var model = new EquipmentModel
            {
                Id = Guid.NewGuid(),
                EquipmentBrandId = brand.Id, EquipmentCategoryId = category.Id,
                Name = modelName, ModelNumber = number, Description = description,
                IsApproved = true, ApprovedByAppUserId = ownerId, DateApproved = now,
                DateCreated = now, CreatedByAppUserId = ownerId,
            };
            db.EquipmentModels.Add(model);
            await Services.EquipmentCatalogSlugs.AssignAsync(db, model, default);
            added++;
        }
        if (added > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[EquipmentTaxonomySeeder] Added {added} real model(s) to the catalog.");
        }
    }

    /// <summary>
    /// Gives an address to any make or model created before the catalog had them.
    /// </summary>
    /// <remarks>
    /// <para>Done here in C# rather than in the migration's SQL so there is exactly <b>one</b>
    /// definition of how a name becomes a slug. A SQL approximation with nested REPLACEs would
    /// handle the ordinary names and quietly disagree with <c>UrlSlug</c> on accents, punctuation
    /// and length — and a row whose address does not match the rule everything else follows is
    /// worse than a row with no address at all.</para>
    ///
    /// <para>Idempotent, as everything in this seeder is: rows that already have an address are not
    /// touched, so a rename is never undone by a restart.</para>
    /// </remarks>
    private static async Task BackfillSlugsAsync(BenDataContext db)
    {
        var brands = await db.EquipmentBrands.Where(b => b.UrlName == null).ToListAsync();
        foreach (var brand in brands)
            await Services.EquipmentCatalogSlugs.AssignAsync(db, brand, default);

        if (brands.Count > 0) await db.SaveChangesAsync();

        var models = await db.EquipmentModels.Where(m => m.UrlName == null).ToListAsync();
        foreach (var model in models)
            await Services.EquipmentCatalogSlugs.AssignAsync(db, model, default);

        if (models.Count > 0) await db.SaveChangesAsync();
    }
}
