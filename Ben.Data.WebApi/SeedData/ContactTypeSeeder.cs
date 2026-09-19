using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds the Home/Work/Mobile-style lookup rows behind emails, phones, addresses, links and
/// notes — for a person AND for an organization. Idempotent — safe to run on every startup.
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
///
/// <para><b>The organization side was added 2026-08-22</b>, after a database check found five of
/// these tables completely empty: organization emails, phones, links and notes, plus user notes.
/// The user-side half had been fixed when the self-service profile made it load-bearing, and the
/// identical hole on the group side simply went unlooked-at — a group administrator adding their
/// group's email got a dropdown with nothing in it and a save that could not succeed. Same
/// invisible failure, one table over.</para>
/// </remarks>
internal static class ContactTypeSeeder
{
    // ── Organization-side ────────────────────────────────────────────────────
    // Worded for a group rather than a person: a group has a "Main" address and a "Bookings"
    // inbox, not a "Home" one.

    private static readonly (string Name, string Description, string Icon)[] OrgEmailTypes =
    [
        ("General",  "The address people should write to first.",        "bi bi-envelope"),
        ("Bookings", "Enquiries about investigations and events.",       "bi bi-calendar-check"),
        ("Press",    "Media and interview requests.",                    "bi bi-megaphone"),
        ("Other",    "Anything that doesn't fit the three above.",       "bi bi-envelope-plus"),
    ];

    private static readonly (string Name, string Description, string Icon)[] OrgPhoneTypes =
    [
        ("Main",      "The number to ring first.",                       "bi bi-telephone"),
        ("Mobile",    "A phone somebody carries on investigations.",     "bi bi-phone"),
        ("Emergency", "For urgent contact during an investigation.",     "bi bi-telephone-forward"),
        ("Other",     "Anything that doesn't fit the three above.",      "bi bi-telephone-plus"),
    ];

    private static readonly (string Name, string Description, string Icon)[] OrgAddressTypes =
    [
        ("Headquarters", "Where the group is based.",                    "bi bi-building"),
        ("Mailing",      "Where post should go, if that differs.",       "bi bi-mailbox"),
        ("Storage",      "Where the group keeps its equipment.",         "bi bi-box-seam"),
        ("Meeting",      "Where the group gathers.",                     "bi bi-people"),
        ("Other",        "Anything that doesn't fit the four above.",    "bi bi-geo-alt"),
    ];

    private static readonly (string Name, string Description, string Icon)[] OrgLinkTypes =
    [
        ("Website", "The group's own site.",                             "bi bi-globe"),
        ("Social",  "A profile on a social network.",                    "bi bi-people"),
        ("Video",   "A channel where the group posts footage.",          "bi bi-camera-video"),
        ("Other",   "Anything that doesn't fit the three above.",        "bi bi-link-45deg"),
    ];

    private static readonly (string Name, string Description, string Icon)[] OrgNoteTypes =
    [
        ("General",   "Anything worth writing down about the group.",    "bi bi-sticky"),
        ("Meeting",   "Notes from a group meeting.",                     "bi bi-journal-text"),
        ("Equipment", "Notes about gear, its condition or its history.", "bi bi-tools"),
        ("Admin",     "Internal administrative notes.",                  "bi bi-clipboard"),
    ];

    private static readonly (string Name, string Description, string Icon)[] UserNoteTypes =
    [
        ("General",  "Anything worth writing down.",                     "bi bi-sticky"),
        ("Contact",  "A record of speaking to somebody.",                "bi bi-chat-left-text"),
        ("Research", "Background reading and findings.",                 "bi bi-search"),
        ("Admin",    "Internal administrative notes.",                   "bi bi-clipboard"),
    ];

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

        // ── Organization-side, and the user notes that were missed with them ──
        await SeedAsync(db.OrganizationEmailTypes,   OrgEmailTypes,   owner.Id,
            (id, n, d, i, o) => new OrganizationEmailType   { Id = id, Name = n, Description = d, IconClass = i, IsActive = true, IsPublic = true, SortOrder = o, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id });
        await SeedAsync(db.OrganizationPhoneTypes,   OrgPhoneTypes,   owner.Id,
            (id, n, d, i, o) => new OrganizationPhoneType   { Id = id, Name = n, Description = d, IconClass = i, IsActive = true, IsPublic = true, SortOrder = o, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id });
        await SeedAsync(db.OrganizationAddressTypes, OrgAddressTypes, owner.Id,
            (id, n, d, i, o) => new OrganizationAddressType { Id = id, Name = n, Description = d, IconClass = i, IsActive = true, IsPublic = true, SortOrder = o, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id });
        await SeedAsync(db.OrganizationLinkTypes,    OrgLinkTypes,    owner.Id,
            (id, n, d, i, o) => new OrganizationLinkType    { Id = id, Name = n, Description = d, IconClass = i, IsActive = true, IsPublic = true, SortOrder = o, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id });
        await SeedAsync(db.OrganizationNoteTypes,    OrgNoteTypes,    owner.Id,
            (id, n, d, i, o) => new OrganizationNoteType    { Id = id, Name = n, Description = d, IconClass = i, IsActive = true, IsPublic = true, SortOrder = o, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id });
        await SeedAsync(db.UserNoteTypes,            UserNoteTypes,   owner.Id,
            (id, n, d, i, o) => new UserNoteType            { Id = id, Name = n, Description = d, IconClass = i, IsActive = true, IsPublic = true, SortOrder = o, DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner.Id });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Adds the rows whose names are not already there. Name-matched, never updating.
    /// </summary>
    /// <remarks>
    /// Generic because six tables now share this shape exactly, and six hand-written loops is six
    /// chances to forget one — which is how the organization side stayed empty while the user side
    /// was fixed.
    /// </remarks>
    private static async Task SeedAsync<T>(
        Microsoft.EntityFrameworkCore.DbSet<T> set,
        (string Name, string Description, string Icon)[] rows,
        Guid ownerId,
        Func<Guid, string, string, string, int, T> make) where T : class
    {
        var existing = await set.Select(t => EF.Property<string>(t, "Name")).ToListAsync();

        for (var i = 0; i < rows.Length; i++)
        {
            var (name, description, icon) = rows[i];
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            set.Add(make(Guid.NewGuid(), name, description, icon, i));
        }
    }
}
