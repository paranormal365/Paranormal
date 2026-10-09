using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// Three section kinds built from what a group keeps about itself: its contact details, a gallery of its
/// public files, and the members who agreed to be named (backlog 256, 10/09/2026).
/// </summary>
/// <remarks>
/// <para>Until now each was drawn on the public page as a gray placeholder ("Contact information section —
/// configure in org settings."), so the editor stopped offering them. They are resolved the way the case and
/// event sections are: the section stores choices, and what a visitor gets is built on every read from the
/// live records. A phone number made private this afternoon leaves the page this afternoon.</para>
///
/// <para>Nothing private can come through. Contact details pass each record's own public switch, and an
/// address its display mode (a street only when the group chose to show the full address). Gallery files pass
/// only when public. Members pass only when they themselves turned on "list me on this group's public pages".</para>
/// </remarks>
public static class CmsGroupSections
{
    public static bool Handles(CmsSectionType type)
        => type is CmsSectionType.ContactInfo or CmsSectionType.FileGallery or CmsSectionType.MemberRoster;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<string> ResolveAsync(
        BenDataContext db, Guid organizationId, CmsSectionType type, string? contentJson, CancellationToken ct)
    {
        var stored = Parse(contentJson);
        object resolved = type switch
        {
            CmsSectionType.ContactInfo => await ContactAsync(db, organizationId, stored, ct),
            CmsSectionType.FileGallery => await GalleryAsync(db, stored, ct),
            CmsSectionType.MemberRoster => await RosterAsync(db, organizationId, stored, ct),
            _ => new { },
        };
        return JsonSerializer.Serialize(resolved, Json);
    }

    // ── What the group stores ────────────────────────────────────────────────

    private sealed record Stored(
        bool ShowAddresses = true, bool ShowEmails = true, bool ShowPhones = true, bool ShowLinks = true,
        IReadOnlyList<Guid>? UploadFileIds = null,
        bool Everyone = true, IReadOnlyList<Guid>? MemberIds = null, bool ShowRole = true);

    private static Stored Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Stored>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    // ── Contact details ──────────────────────────────────────────────────────

    /// <summary>One way to reach the group. <paramref name="Href"/> is a mailto:, tel: or web address.</summary>
    public sealed record ContactLine(string Kind, string? Label, string Value, string? Href);

    private static async Task<List<ContactLine>> ContactAsync(BenDataContext db, Guid orgId, Stored s, CancellationToken ct)
    {
        var lines = new List<ContactLine>();
        var org = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId)
            .Select(o => new { o.PublicEmail, o.PublicPhone, o.PublicWebsite }).FirstOrDefaultAsync(ct);

        if (s.ShowEmails)
        {
            if (!string.IsNullOrWhiteSpace(org?.PublicEmail)) lines.Add(new("email", null, org.PublicEmail.Trim(), $"mailto:{org.PublicEmail.Trim()}"));
            foreach (var e in await db.OrganizationEmails.AsNoTracking()
                         .Where(e => e.OrganizationId == orgId && e.IsPublic && !e.IsHidden)
                         .OrderBy(e => e.SortOrder).ToListAsync(ct))
                lines.Add(new("email", e.DisplayText, e.EmailAddress, $"mailto:{e.EmailAddress}"));
        }

        if (s.ShowPhones)
        {
            if (!string.IsNullOrWhiteSpace(org?.PublicPhone)) lines.Add(new("phone", null, org.PublicPhone.Trim(), Tel(org.PublicPhone)));
            foreach (var p in await (from p in db.OrganizationPhones.AsNoTracking()
                                     join t in db.OrganizationPhoneTypes.AsNoTracking() on p.OrganizationPhoneTypeId equals t.Id
                                     where p.OrganizationId == orgId && p.IsPublic
                                     orderby p.IsPrimary descending
                                     select new { p.PhoneNumber, Type = t.Name }).ToListAsync(ct))
                lines.Add(new("phone", p.Type, p.PhoneNumber, Tel(p.PhoneNumber)));
        }

        if (s.ShowLinks)
        {
            if (WebAddress(org?.PublicWebsite) is { } site) lines.Add(new("link", null, site, site));
            foreach (var l in await db.OrganizationLinks.AsNoTracking()
                         .Where(l => l.OrganizationId == orgId && l.IsPublic && l.IsActive).ToListAsync(ct))
                if (WebAddress(l.LinkUrl) is { } url)
                    lines.Add(new("link", l.DisplayText, url, url));
        }

        if (s.ShowAddresses)
        {
            foreach (var a in await db.OrganizationAddresses.AsNoTracking()
                         .Where(a => a.OrganizationId == orgId
                                  && a.Visibility == OrganizationAddressVisibility.Public
                                  && a.PublicDisplayMode != OrganizationAddressDisplayMode.Hidden)
                         .OrderBy(a => a.SortOrder).ToListAsync(ct))
            {
                // The street only when the group chose to show the full address; otherwise the town.
                var full = a.PublicDisplayMode is OrganizationAddressDisplayMode.FullAddressAndMap
                                                or OrganizationAddressDisplayMode.FullAddressOnly;
                var town = string.Join(", ", new[] { a.City, a.State }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var text = full
                    ? string.Join(", ", new[] { a.StreetAddress1, a.StreetAddress2, town }.Where(x => !string.IsNullOrWhiteSpace(x)))
                      + (string.IsNullOrWhiteSpace(a.ZipCode) ? "" : $" {a.ZipCode}")
                    : town;
                if (text.Length > 0) lines.Add(new("address", null, text, null));
            }
        }

        // The same address recorded twice (the group's own field and a contact row) is said once.
        return [.. lines.GroupBy(l => (l.Kind, l.Value.ToLowerInvariant())).Select(g => g.First())];
    }

    private static string Tel(string number) => "tel:" + new string([.. number.Where(c => char.IsDigit(c) || c == '+')]);

    /// <summary>A web address a visitor can follow: http or https only, never a script or a file.</summary>
    private static string? WebAddress(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var text = url.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.ToString() : null;
    }

    // ── File gallery ─────────────────────────────────────────────────────────

    /// <summary>A file in the gallery, in the group's order.</summary>
    public sealed record GalleryFile(Guid Id, string FileName, string ContentType);

    private static async Task<List<GalleryFile>> GalleryAsync(BenDataContext db, Stored s, CancellationToken ct)
    {
        var ids = (s.UploadFileIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return [];
        // Public files only: the page is public, and a members-only file would show a visitor a broken frame.
        var files = await db.UploadFiles.AsNoTracking()
            .Where(f => ids.Contains(f.Id) && f.IsPublic)
            .Select(f => new GalleryFile(f.Id, f.FileName, f.ContentType))
            .ToListAsync(ct);
        return [.. ids.Select(id => files.FirstOrDefault(f => f.Id == id)).Where(f => f is not null)!];
    }

    // ── Our members ──────────────────────────────────────────────────────────

    /// <summary>A member who agreed to be named. <paramref name="PhotoFileId"/> is their public photo, if any.</summary>
    public sealed record RosterMember(Guid Id, string Name, string? Title, Guid? PhotoFileId);

    private static async Task<List<RosterMember>> RosterAsync(BenDataContext db, Guid orgId, Stored s, CancellationToken ct)
    {
        var willing = await (from m in db.OrganizationUserMemberships.AsNoTracking()
                             join u in db.AppUsers.AsNoTracking() on m.AppUserId equals u.Id
                             where m.OrganizationId == orgId && m.IsActive && m.ShowOnPublicPages && u.DateClosed == null
                             select new
                             {
                                 u.Id,
                                 Name = u.DisplayName ?? u.Handle ?? "A member",
                                 Title = m.MemberLevel != null ? m.MemberLevel.Name : null,
                                 Photo = db.AppUserPhotos
                                     .Where(p => p.AppUserId == u.Id && p.IsActive && p.IsPublic
                                              && db.UploadFiles.Any(f => f.Id == p.UploadFileId && f.IsPublic))
                                     .Select(p => (Guid?)p.UploadFileId).FirstOrDefault(),
                             }).ToListAsync(ct);

        var chosen = s.Everyone || s.MemberIds is null
            ? willing.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : [.. s.MemberIds.Select(id => willing.FirstOrDefault(w => w.Id == id)).Where(w => w is not null)!];

        return [.. chosen.Select(w => new RosterMember(w!.Id, w.Name, s.ShowRole ? w.Title : null, w.Photo))];
    }
}
