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
            db.EquipmentModels.Add(new EquipmentModel
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
            });
            modelAdded = true;
        }
        if (modelAdded) await db.SaveChangesAsync();
    }
}
