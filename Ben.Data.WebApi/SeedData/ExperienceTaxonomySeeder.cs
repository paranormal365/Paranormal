using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds the global experience category + type taxonomy used across
/// client requests, case timelines, and investigation evidence.
/// Idempotent — safe to run on every startup.
/// </summary>
internal static class ExperienceTaxonomySeeder
{
    private static readonly (string Name, string? Description, string? ColorClass, (string Name, string? Description)[] Types)[] _seed =
    [
        ("Audible", "Sounds heard without an identifiable natural source.", "text-warning",
        [
            ("Knocking / Banging", "Repeated or single knocking sounds on walls, floors, or objects."),
            ("Voices / Whispering", "Heard speech or whispering when no one is present."),
            ("Footsteps", "Footstep sounds with no visible source."),
            ("Music / Humming", "Music, humming, or singing without a discernible origin."),
            ("Screaming / Crying", "Distressed vocalizations without an identifiable source."),
            ("Breathing", "Audible breathing sounds in an unoccupied area."),
            ("Other Audible", null),
        ]),
        ("Visual", "Things seen that cannot be explained by ordinary means.", "text-info",
        [
            ("Apparition", "A visible human or animal form or partial form."),
            ("Shadow Figure", "A dark human-shaped figure without a physical source."),
            ("Orb", "A ball of light captured in photo or video, or seen by the naked eye."),
            ("Light Anomaly", "Unexplained flashes, streaks, or glowing areas of light."),
            ("Mist / Fog", "A misty or foggy form in an area where none should exist."),
            ("Object Movement", "An object observed moving without physical cause."),
            ("Other Visual", null),
        ]),
        ("Physical", "Tangible effects felt or measured without a natural cause.", "text-danger",
        [
            ("Temperature Drop", "A sudden, localized cold spot or rapid temperature decrease."),
            ("Temperature Spike", "A sudden, localized increase in temperature."),
            ("Being Touched", "A physical sensation of being touched when no one is present."),
            ("Object Moved", "An object found in a different position than left."),
            ("Door / Window Opening or Closing", "A door or window moved without human or wind cause."),
            ("Electronics Malfunction", "Devices turning on, off, or behaving erratically."),
            ("EMF Spike", "An electromagnetic field reading with no identifiable electrical source."),
            ("Other Physical", null),
        ]),
        ("Olfactory", "Unexplained smells or odors.", "text-success",
        [
            ("Unexplained Odor", "A smell that cannot be linked to any present source."),
            ("Perfume / Cologne", "A distinctive fragrance without a person or product source."),
            ("Sulfur / Burning", "A sulfur or burning smell with no physical cause."),
            ("Other Olfactory", null),
        ]),
        ("Psychological", "Internal experiences or feelings that cannot be logically explained.", "text-secondary",
        [
            ("Feeling of Being Watched", "A persistent sense of observation with no visible observer."),
            ("Dread / Fear", "An overwhelming sense of fear or dread in a specific location."),
            ("Overwhelming Sadness", "An unexplained wave of grief or sadness tied to a location."),
            ("Euphoria", "An unexplained sense of joy or comfort tied to a location."),
            ("Other Psychological", null),
        ]),
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
        var now = DateTime.UtcNow;

        int catSort = 1;
        foreach (var (catName, catDesc, colorClass, types) in _seed)
        {
            var category = await db.ExperienceCategories.FirstOrDefaultAsync(c => c.Name == catName);
            if (category is null)
            {
                category = new ExperienceCategory
                {
                    Id                   = Guid.NewGuid(),
                    Name                 = catName,
                    Description          = catDesc,
                    ColorClass           = colorClass,
                    SortOrder            = catSort,
                    IsActive             = true,
                    IsApproved           = true,
                    ApprovedByAppUserId  = owner.Id,
                    DateApproved         = now,
                    DateCreated          = now,
                    CreatedByAppUserId   = owner.Id,
                };
                db.ExperienceCategories.Add(category);
                await db.SaveChangesAsync();
            }

            int typeSort = 1;
            var existingNames = await db.ExperienceTypes
                .Where(t => t.ExperienceCategoryId == category.Id)
                .Select(t => t.Name)
                .ToHashSetAsync();

            bool added = false;
            foreach (var (typeName, typeDesc) in types)
            {
                if (existingNames.Contains(typeName)) { typeSort++; continue; }
                db.ExperienceTypes.Add(new ExperienceType
                {
                    Id                   = Guid.NewGuid(),
                    ExperienceCategoryId = category.Id,
                    Name                 = typeName,
                    Description          = typeDesc,
                    SortOrder            = typeSort,
                    IsActive             = true,
                    IsApproved           = true,
                    ApprovedByAppUserId  = owner.Id,
                    DateApproved         = now,
                    DateCreated          = now,
                    CreatedByAppUserId   = owner.Id,
                });
                added = true;
                typeSort++;
            }
            if (added) await db.SaveChangesAsync();
            catSort++;
        }
    }
}
