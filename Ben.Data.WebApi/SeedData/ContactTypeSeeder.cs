using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds the Home/Work/Mobile-style lookup rows behind a person's own emails, phones, addresses
/// and links. Idempotent — safe to run on every startup.
/// </summary>
/// <remarks>
/// <para>These four tables have existed since early in the project and were never populated by
/// anything. That went unnoticed for as long as the only way to reach them was the SuperAdmin
/// screens, where an empty list reads as "nobody has made one yet". The self-service profile makes
/// it load-bearing: the type is required on every row, so with no types the contact cards render a
/// dropdown with nothing in it and every save is rejected. A feature that is dead on arrival for
/// every existing deployment is not much of a feature.</para>
///
/// <para>Matched by name rather than by a fixed id, so a deployment that already created its own
/// "Home" by hand keeps it instead of gaining a duplicate. Nothing here is ever updated or removed
/// on a later run — an administrator's edits and deletions are theirs to keep.</para>
/// </remarks>
internal static class ContactTypeSeeder
{
    private static readonly (string Name, string Description, string Icon)[] EmailTypes =
    [
        ("Personal", "A private address you use day to day.",        "bi bi-house"),
        ("Work",     "An address at your job or organization.",      "bi bi-briefcase"),
        ("Other",    "Anything that doesn't fit the two above.",     "bi bi-envelope"),
    ];

    private static readonly (string Name, string Description, string Icon)[] PhoneTypes =
    [
        ("Mobile", "A phone you carry, which can usually receive texts.", "bi bi-phone"),
        ("Home",   "A landline at the place you live.",                   "bi bi-telephone"),
        ("Work",   "A number at your job or organization.",               "bi bi-building"),
        ("Other",  "Anything that doesn't fit the three above.",          "bi bi-telephone-plus"),
    ];

    private static readonly (string Name, string Description, string Icon)[] AddressTypes =
    [
        ("Home",     "Where you live.",                            "bi bi-house-door"),
        ("Work",     "Your job or organization's address.",        "bi bi-building"),
        ("Mailing",  "Where post should go, if that differs.",     "bi bi-mailbox"),
        ("Other",    "Anything that doesn't fit the three above.", "bi bi-geo-alt"),
    ];

    private static readonly (string Name, string Description, string Icon)[] LinkTypes =
    [
        ("Website",       "Your own site or blog.",                   "bi bi-globe"),
        ("Social",        "A profile on a social network.",           "bi bi-people"),
        ("Video Channel", "YouTube, Twitch, or anywhere you post video.", "bi bi-camera-video"),
        ("Other",         "Anything that doesn't fit the three above.",   "bi bi-link-45deg"),
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

        var existingEmails = await db.UserEmailTypes.Select(t => t.Name).ToListAsync();
        for (var i = 0; i < EmailTypes.Length; i++)
        {
            var (name, description, icon) = EmailTypes[i];
            if (existingEmails.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            db.UserEmailTypes.Add(new UserEmailType
            {
                Id = Guid.NewGuid(), Name = name, Description = description, IconClass = icon,
                IsActive = true, IsPublic = true, SortOrder = i,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id,
            });
        }

        var existingPhones = await db.UserPhoneTypes.Select(t => t.Name).ToListAsync();
        for (var i = 0; i < PhoneTypes.Length; i++)
        {
            var (name, description, icon) = PhoneTypes[i];
            if (existingPhones.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            db.UserPhoneTypes.Add(new UserPhoneType
            {
                Id = Guid.NewGuid(), Name = name, Description = description, IconClass = icon,
                IsActive = true, IsPublic = true, SortOrder = i,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id,
            });
        }

        var existingAddresses = await db.UserAddressTypes.Select(t => t.Name).ToListAsync();
        for (var i = 0; i < AddressTypes.Length; i++)
        {
            var (name, description, icon) = AddressTypes[i];
            if (existingAddresses.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            db.UserAddressTypes.Add(new UserAddressType
            {
                Id = Guid.NewGuid(), Name = name, Description = description, IconClass = icon,
                IsActive = true, IsPublic = true, SortOrder = i,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id,
            });
        }

        var existingLinks = await db.UserLinkTypes.Select(t => t.Name).ToListAsync();
        for (var i = 0; i < LinkTypes.Length; i++)
        {
            var (name, description, icon) = LinkTypes[i];
            if (existingLinks.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            db.UserLinkTypes.Add(new UserLinkType
            {
                Id = Guid.NewGuid(), Name = name, Description = description, IconClass = icon,
                IsActive = true, IsPublic = true, SortOrder = i,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id,
            });
        }

        await db.SaveChangesAsync();
    }
}
