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

    /// <summary>Default avatar for someone whose profile says they are a man. Falls back to the
    /// generic default when unset — three settings, one fallback chain, never a broken image.</summary>
    public const string DefaultAvatarManUploadFileId = "avatar.default.man.upload-file-id";

    /// <summary>Default avatar for someone whose profile says they are a woman. Same fallback.</summary>
    public const string DefaultAvatarWomanUploadFileId = "avatar.default.woman.upload-file-id";

    /// <summary>Whether new organizations may be registered by ordinary users.</summary>
    public const string AllowOrganizationSelfRegistration = "org.allow-self-registration";

    /// <summary>Short notice shown site-wide — maintenance windows, outages. Empty = nothing shown.</summary>
    public const string SiteAnnouncement = "site.announcement";

    /// <summary>Contact address published on public pages for general enquiries.</summary>
    public const string PublicContactEmail = "site.public-contact-email";

    /// <summary>Postal address shown on the contact page. Line breaks are preserved.</summary>
    public const string ContactPostalAddress = "contact.postal-address";

    /// <summary>Phone number shown on the contact page.</summary>
    public const string ContactPhone = "contact.phone";

    /// <summary>When someone can expect an answer, e.g. "Weekdays, 9am–5pm Central".</summary>
    public const string ContactHours = "contact.hours";

    /// <summary>Requests per minute, per caller, allowed against the geocoding endpoints.</summary>
    public const string RateLimitGeocodingPerMinute = "ratelimit.geocoding-per-minute";

    /// <summary>Requests per minute, per caller, allowed against sign-in and registration.</summary>
    public const string RateLimitAuthPerMinute = "ratelimit.auth-per-minute";

    /// <summary>Requests per minute, per caller, allowed against everything else.</summary>
    public const string RateLimitGlobalPerMinute = "ratelimit.global-per-minute";

    /// <summary>
    /// Requests per minute, per caller, allowed against public event sign-up — where a whole
    /// tour group shares one address and must not look like one script.
    /// </summary>
    public const string RateLimitEventAttendancePerMinute = "ratelimit.event-attendance-per-minute";

    /// <summary>
    /// Requests per minute, per caller, allowed against the audio operations that decode a whole
    /// recording synchronously — edit, clip, EVP scan and mix export.
    /// </summary>
    public const string RateLimitAudioProcessingPerMinute = "ratelimit.audio-processing-per-minute";

    /// <summary>Largest file one upload may be, in bytes. Unset = the built-in 2 GiB default.</summary>
    public const string UploadMaxFileBytes = "upload.max-file-bytes";

    /// <summary>Largest single chunk a chunked upload may send, in bytes. Unset = 64 MiB.</summary>
    public const string UploadChunkMaxBytes = "upload.chunk-max-bytes";

    /// <summary>
    /// How much a person with no paying group may store, in megabytes. See
    /// <c>AccountStorageGuard</c>, which owns the rule and the fallback.
    /// </summary>
    public const string FreeAccountStorageMegabytes = "storage.free-account-megabytes";

    // ── Feature flags ─────────────────────────────────────────────────────────
    //
    // One switch per major section of the site, so a SuperAdmin can turn a whole area off without
    // a deployment. Two rules make these safe to add:
    //
    //   * The DEFAULT is stated at the read site, not here — a key with no row reads as unset, and
    //     every consumer passes its own `whenUnset`. Sections that already exist default ON, so
    //     adding a flag never silently removes a working feature; the two unbuilt features default
    //     OFF so they cannot appear before they are finished.
    //   * Turning one off must kill the URLs, not just the navigation links. Hiding a link while
    //     the page still answers is the failure this codebase has already learned to distrust.

    /// <summary>Video editor pages (My Videos, the case editor, the standalone host's site links).</summary>
    public const string FeatureVideoEditor = "features.video-editor";

    /// <summary>Equipment: personal inventory, group catalogues, checkouts, loans.</summary>
    public const string FeatureEquipment = "features.equipment";

    /// <summary>Group calendars, public events, RSVPs — and the reminder emails that go with them.</summary>
    public const string FeatureEvents = "features.events";

    /// <summary>"What's near me" search and the public maps on the home page.</summary>
    public const string FeatureDiscovery = "features.discovery";

    /// <summary>Group-authored public pages at /o/{group}. Gates the anonymous read path too.</summary>
    public const string FeatureCmsPages = "features.cms-pages";

    /// <summary>The media library and its browse/attach surfaces.</summary>
    public const string FeatureMediaLibrary = "features.media-library";

    /// <summary>Group messaging — inbox, sent, compose.</summary>
    public const string FeatureOrgMessaging = "features.org-messaging";

    /// <summary>Voting on cases, evidence and files.</summary>
    public const string FeatureVoting = "features.voting";

    /// <summary>The public feed. Off until the feature ships.</summary>
    public const string FeaturePublicFeed = "features.public-feed";

    /// <summary>Group publications and subscriptions. Off until the feature ships.</summary>
    public const string FeaturePublications = "features.publications";

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
            "Shown when someone has no profile photo the viewer is allowed to see and their profile doesn't say whether they are a man or a woman. Upload one here; replacing it removes the previous image."),
        (DefaultAvatarManUploadFileId, "Default profile picture — man",
            "Shown instead of the generic default when the person's profile says they are a man. Leave unset to use the generic default for everyone."),
        (DefaultAvatarWomanUploadFileId, "Default profile picture — woman",
            "Shown instead of the generic default when the person's profile says they are a woman. Leave unset to use the generic default for everyone."),
        (AllowOrganizationSelfRegistration, "Allow groups to self-register",
            "When on, any signed-in user can register a new group. When off, only a SuperAdmin can create one."),
        (SiteAnnouncement, "Site-wide announcement",
            "A short notice shown across the site — planned maintenance, known issues. Leave empty to show nothing."),
        (PublicContactEmail, "Public contact email",
            "Contact address published on public pages for general enquiries."),
        (ContactPostalAddress, "Postal address",
            "Shown on the contact page. Put each line of the address on its own line — line breaks are preserved as written."),
        (ContactPhone, "Contact phone",
            "Phone number shown on the contact page. Leave empty to show none."),
        (ContactHours, "When we reply",
            "When someone can expect an answer, e.g. \"Weekdays, 9am–5pm Central\". Leave empty to show nothing."),
        (RateLimitGeocodingPerMinute, "Rate limit — address lookup (per minute)",
            "How many address lookups one caller may make each minute. Geocoding is billed per lookup by the outside service, so this is the setting that caps that bill. Leave empty to use the built-in default. Changes take effect within a minute; no restart needed."),
        (RateLimitAuthPerMinute, "Rate limit — sign in and register (per minute)",
            "How many sign-in or registration attempts one caller may make each minute. Low enough to stop password guessing, high enough that a person mistyping their password never notices. Leave empty for the default."),
        (RateLimitGlobalPerMinute, "Rate limit — everything else (per minute)",
            "A ceiling on all other requests from one caller each minute, so a runaway client cannot saturate the server. Generous by design — normal use should never reach it. Leave empty for the default."),
        (RateLimitEventAttendancePerMinute, "Rate limit — public event sign-up (per minute)",
            "How many event sign-up requests may come from one address each minute. Deliberately much higher than the others: a tour group of thirty all signing up at the meeting point shares the venue's wifi, so they reach the site as a single caller. Raise this if a busy operator reports guests being turned away; it does not affect how many people may attend. Leave empty for the default."),

        (RateLimitAudioProcessingPerMinute, "Rate limit \u2014 audio processing (per minute)",
            "How many audio edits, clips, EVP scans or mix exports one caller may ask for each minute. Each of these decodes a whole recording while the request waits, so a handful at once is what a busy person does and a hundred is a script. Low by design; raise it if somebody working through a long recording reports being turned away. Leave empty for the default."),

        (FreeAccountStorageMegabytes, "Free account storage (MB)",
            "How much somebody with no paid group may store in their own field sessions. Members of a group on a paid plan are not counted against this. Leave empty for the built-in default of 2048 MB."),
        (UploadMaxFileBytes, "Upload limit — one file (bytes)",
            "The largest file anyone may upload, in bytes. Applies to every upload path — the classic form and the chunked uploader alike. Leave empty for the built-in default of 2 GiB (2147483648)."),
        (UploadChunkMaxBytes, "Upload limit — one chunk (bytes)",
            "How large each piece of a chunked upload may be, in bytes. Keep this under 100 MB (104857600): the site is served through Cloudflare, which rejects any single request bigger than that. Leave empty for the built-in default of 64 MiB (67108864)."),

        (FeatureVideoEditor, "Feature — Video editor",
            "The video editor: My Videos, the editor on a case, and the links to the standalone editor. Turning this off hides those pages and makes their addresses stop working. Anything already exported or saved is untouched."),
        (FeatureEquipment, "Feature — Equipment",
            "Personal equipment lists, group catalogues, checkouts and loans. Off hides the whole section; the records stay in the database."),
        (FeatureEvents, "Feature — Events and calendars",
            "Group calendars, public events and RSVPs, including the reminder emails sent before an event. Off stops the reminders as well as the pages."),
        (FeatureDiscovery, "Feature — Local discovery and maps",
            "\"What's near me\" search and the public maps on the home page. Off leaves the rest of the home page intact."),
        (FeatureCmsPages, "Feature — Group public pages",
            "The pages groups author for visitors, at /o/{group}. Off takes them down for anonymous visitors too, not just signed-in users."),
        (FeatureMediaLibrary, "Feature — Media library",
            "The media library and the screens that browse or attach from it. Files themselves are not affected."),
        (FeatureOrgMessaging, "Feature — Group messaging",
            "Group inbox, sent messages and compose. Case message boards between a client and their group are separate and stay on."),
        (FeatureVoting, "Feature — Voting",
            "Voting on cases, evidence and files. Off hides the vote controls; existing votes are kept and counted if you turn it back on."),
        (FeaturePublicFeed, "Feature — Public feed",
            "The site-wide feed any signed-in member can post to, with mentions, hashtags and following. Off by default. Turning it on also turns on the moderation queue, which is where reported posts arrive."),
        (FeaturePublications, "Feature — Publications",
            "Long-form publications a group writes and readers subscribe to. Off by default. Subscriptions are free; nothing here charges anyone."),
    ];

    /// <summary>
    /// Settings whose value is expected to run to several lines, so the admin page gives them a
    /// textarea instead of a single-line input.
    /// </summary>
    /// <remarks>
    /// A postal address in a one-line input is technically editable and miserable to edit. Kept as
    /// a set here rather than a fourth tuple field so the seed stays readable.
    /// </remarks>
    public static readonly IReadOnlySet<string> MultiLineKeys =
        new HashSet<string> { ContactPostalAddress, SiteAnnouncement };

    /// <summary>
    /// The feature switches, paired with what each reads when no-one has ever set it.
    /// </summary>
    /// <remarks>
    /// This list is the contract between the admin page, the public features endpoint and the
    /// website's gate. Adding a switch here is all it takes for it to appear in all three, which
    /// is also why the default lives here: a flag whose default is written separately at each
    /// read site is a flag that eventually disagrees with itself.
    /// </remarks>
    public static readonly IReadOnlyList<(string Key, bool DefaultWhenUnset)> FeatureDefaults =
    [
        (FeatureVideoEditor,  true),
        (FeatureEquipment,    true),
        (FeatureEvents,       true),
        (FeatureDiscovery,    true),
        (FeatureCmsPages,     true),
        (FeatureMediaLibrary, true),
        (FeatureOrgMessaging, true),
        (FeatureVoting,       true),
        (FeaturePublicFeed,   false),
        (FeaturePublications, false),
    ];

    /// <summary>Just the feature keys, in declaration order.</summary>
    public static IEnumerable<string> FeatureFlags => FeatureDefaults.Select(f => f.Key);

    /// <summary>What a feature reads when nobody has set it. Unknown keys are off.</summary>
    public static bool DefaultFor(string key)
        => FeatureDefaults.FirstOrDefault(f => f.Key == key).DefaultWhenUnset;

    /// <summary>
    /// Settings that are on/off, so the admin page gives them a switch instead of a text box.
    /// </summary>
    /// <remarks>
    /// Before this existed, the only boolean-shaped setting was
    /// <see cref="AllowOrganizationSelfRegistration"/>, whose description had to end with
    /// "Accepts true or false" — an instruction that exists only because the control was wrong.
    /// It is in this set now too.
    /// </remarks>
    /// <remarks>
    /// Built from <see cref="FeatureDefaults"/> lazily rather than in a field initializer. Static
    /// fields initialize in declaration order, and this one used to run before
    /// <see cref="FeatureDefaults"/> existed — so it enumerated null and every request that
    /// touched this class died in a TypeInitializationException. A property has no such ordering
    /// to get wrong.
    /// </remarks>
    public static IReadOnlySet<string> BooleanKeys { get; } =
        new HashSet<string>(
            FeatureDefaults.Select(f => f.Key).Append(AllowOrganizationSelfRegistration),
            StringComparer.Ordinal);

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

    /// <summary>Instance overload for callers that have no context of their own — a controller
    /// enforcing a policy setting, typically.</summary>
    public async Task<bool> GetBoolAsync(string key, bool whenUnset, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        return await GetBoolAsync(db, key, whenUnset, ct);
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
            // Always the CURRENT declaration's text, not whatever was stored when the row was
            // first written: a stored description fossilizes the wording of the day it was set,
            // and the admin page ends up explaining a workflow that no longer exists (caught by
            // the 2026-08-23 screenshot pass — the generic avatar row still said "paste its file
            // id here" months of edits after that stopped being true).
            .Select(s => { s.Description = DescriptionFor(s.Key) ?? s.Description; return s; })
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
