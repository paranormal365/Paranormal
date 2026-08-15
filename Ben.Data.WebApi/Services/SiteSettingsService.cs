using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The stable identifiers for sitewide settings, and what each one means.
/// </summary>
/// <remarks>
/// Keys are strings in the database but constants here, so a typo is a compile error rather than a
/// setting that silently reads as unset. <see cref="Seed"/> is the source of truth for which
/// settings exist: the admin page lists whatever the seed declares, so adding a setting is one
/// entry here plus a consumer — no migration, no UI change.
/// </remarks>
public static class SiteSettingKeys
{
    /// <summary>
    /// UploadFile id of an image shown when a person has no photo the viewer may see.
    /// </summary>
    public const string DefaultAvatarUploadFileId = "avatar.default.upload-file-id";

    /// <summary>Whether new organizations may be registered by ordinary users.</summary>
    public const string AllowOrganizationSelfRegistration = "org.allow-self-registration";

    /// <summary>Short notice shown site-wide — maintenance windows, outages. Empty = nothing shown.</summary>
    public const string SiteAnnouncement = "site.announcement";

    /// <summary>Contact address published on public pages for general enquiries.</summary>
    public const string PublicContactEmail = "site.public-contact-email";

    /// <summary>
    /// Every setting the site knows about: its key, the human label, and the description shown in
    /// the admin page. Order here is the order they appear.
    /// </summary>
    /// <remarks>
    /// The label is stated rather than derived from the key. An earlier version split the key on
    /// dots and title-cased the last segment, which rendered
    /// <c>avatar.default.upload-file-id</c> as "Upload file id" — technically a label, useless as
    /// one. Keys are named for the API; humans get their own string.
    /// </remarks>
    public static readonly IReadOnlyList<(string Key, string Label, string Description)> Seed =
    [
        (DefaultAvatarUploadFileId, "Default profile picture",
            "Image shown in place of initials when someone has no profile photo the viewer is allowed to see. Upload it as a public file first, then paste its file id here."),
        (AllowOrganizationSelfRegistration, "Allow groups to self-register",
            "When on, any signed-in user can register a new group. When off, only a SuperAdmin can create one. Accepts true or false."),
        (SiteAnnouncement, "Site-wide announcement",
            "A short notice shown across the site — planned maintenance, known issues. Leave empty to show nothing."),
        (PublicContactEmail, "Public contact email",
            "Contact address published on public pages for general enquiries."),
    ];
}

/// <summary>
/// Reads and writes sitewide settings. Values are stored as text; this is the only place that
/// knows how to turn them into something typed.
/// </summary>
/// <remarks>
/// Deliberately not cached. Settings are read on paths that already hit the database, changes must
/// take effect immediately rather than after an unpredictable cache expiry, and a stale sitewide
/// flag is the kind of bug that wastes an afternoon. If a hot path ever needs one, cache there
/// where the invalidation story can be reasoned about locally.
/// </remarks>
public sealed class SiteSettingsService
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public SiteSettingsService(IDbContextFactory<BenDataContext> dbContextFactory)
        => _dbContextFactory = dbContextFactory;

    /// <summary>Raw value for a key, or null when unset or absent.</summary>
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        return await GetAsync(db, key, ct);
    }

    /// <summary>Overload for callers that already have a context open.</summary>
    public static async Task<string?> GetAsync(BenDataContext db, string key, CancellationToken ct = default)
    {
        var value = await db.SiteSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>A setting parsed as a Guid, or null when unset, absent, or unparseable.</summary>
    public static async Task<Guid?> GetGuidAsync(BenDataContext db, string key, CancellationToken ct = default)
    {
        var raw = await GetAsync(db, key, ct);
        // Unparseable is treated as unset rather than thrown: a bad value typed into an admin box
        // should degrade to the default behaviour, not break every page that reads it.
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>A setting parsed as a bool, falling back to <paramref name="whenUnset"/>.</summary>
    public static async Task<bool> GetBoolAsync(
        BenDataContext db, string key, bool whenUnset, CancellationToken ct = default)
    {
        var raw = await GetAsync(db, key, ct);
        return bool.TryParse(raw, out var value) ? value : whenUnset;
    }

    /// <summary>
    /// Every known setting, with current values. Settings declared in <see cref="SiteSettingKeys.Seed"/>
    /// but never yet written appear with a null value rather than being missing, so the admin page
    /// always shows the full list.
    /// </summary>
    public async Task<IReadOnlyList<SiteSetting>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var stored = await db.SiteSettings.AsNoTracking().ToListAsync(ct);

        return SiteSettingKeys.Seed
            .Select(seed => stored.FirstOrDefault(s => s.Key == seed.Key)
                            ?? new SiteSetting { Key = seed.Key, Description = seed.Description })
            .Select(s => { s.Description ??= DescriptionFor(s.Key); return s; })
            .ToList();
    }

    /// <summary>Writes a value, creating the row on first use.</summary>
    public async Task<SiteSetting> SetAsync(
        string key, string? value, Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null)
        {
            row = new SiteSetting
            {
                Id                 = Guid.NewGuid(),
                Key                = key,
                Description        = DescriptionFor(key),
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            };
            db.SiteSettings.Add(row);
        }
        else
        {
            row.DateUpdated        = DateTime.UtcNow;
            row.UpdatedByAppUserId = userId;
        }

        // Whitespace is not a value — store null so every reader's "is it set" check agrees.
        row.Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>True when the key is one the site actually declares.</summary>
    public static bool IsKnownKey(string key)
        => SiteSettingKeys.Seed.Any(s => s.Key == key);

    private static string? DescriptionFor(string key)
        => SiteSettingKeys.Seed.FirstOrDefault(s => s.Key == key).Description;

    /// <summary>The human-readable name for a key.</summary>
    public static string LabelFor(string key)
        => SiteSettingKeys.Seed.FirstOrDefault(s => s.Key == key).Label ?? key;
}
