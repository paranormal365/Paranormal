using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The caller's own emails, phones, addresses and links.
/// </summary>
/// <remarks>
/// <para>Entities and full CRUD have existed since early in the project, but only behind
/// SuperAdmin-only <c>/admin/user-*</c> routes — a signed-in user could not add so much as a phone
/// number for themselves. This is the self-service surface, sharing the <c>api/me</c> prefix with
/// <see cref="MyProfileController"/> (route templates don't collide: that controller owns
/// <c>profile</c> and <c>photos*</c>).</para>
///
/// <para>Every action is scoped to <see cref="BenControllerBase.GetCurrentUserId"/>; no caller
/// ever supplies a user id. Rows are matched on id <i>and</i> owner together, so another person's
/// row reads as 404 rather than 403 — confirming existence to someone who shouldn't see it is its
/// own small leak.</para>
///
/// <para>Requests are plain records, never the entity itself: the admin controllers bind
/// <c>[FromBody] UserEmail</c> directly, which is fine when only a SuperAdmin can reach them, but
/// would let a self-service caller set <c>AppUserId</c> to someone else's id.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MyContactInfoController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IAuditLogService _auditLog;

    private readonly Ben.Data.Common.SiteIdentity _site;

    public MyContactInfoController(
        IDbContextFactory<BenDataContext> dbContextFactory, IAuditLogService auditLog,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site)
    {
        _dbContextFactory = dbContextFactory;
        _auditLog = auditLog;
        _site = site.Value;
    }

    // ── Emails ────────────────────────────────────────────────────────────────

    [HttpGet("emails")]
    public async Task<ActionResult<IEnumerable<MyEmailRecord>>> GetEmails(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var rows = await db.UserEmails.AsNoTracking()
            .Where(e => e.AppUserId == userId)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DateCreated)
            .ToListAsync(ct);

        return Ok(rows.Select(ToRecord));
    }

    [HttpPost("emails")]
    public async Task<ActionResult<MyEmailRecord>> CreateEmail(
        [FromBody] UpsertMyEmailRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        var address = request.EmailAddress?.Trim();
        if (string.IsNullOrWhiteSpace(address)) return BadRequest("An email address is required.");
        // A brand-new row is never validated, so it can never legitimately be public yet — see the
        // class remarks on why public requires validated. Coercing this silently would hide the
        // rule from whoever wrote the client; refusing it surfaces the rule immediately.
        if (request.IsPublic) return BadRequest("A new email address must be validated before it can be made public.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UserEmailTypes.AnyAsync(t => t.Id == request.UserEmailTypeId, ct))
            return BadRequest("Unknown email type.");

        var entity = new UserEmail
        {
            Id = Guid.NewGuid(),
            AppUserId = userId,
            UserEmailTypeId = request.UserEmailTypeId,
            EmailAddress = address,
            IsPrimary = request.IsPrimary,
            IsPublic = false,
            IsHidden = false,
            IsValidated = false,
            ValidationToken = null,
            SortOrder = request.SortOrder,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        if (request.IsPrimary) await UnsetOtherPrimaryEmailsAsync(db, userId, ct);

        db.UserEmails.Add(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UserEmail), entity.Id, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpPut("emails/{id:guid}")]
    public async Task<ActionResult<MyEmailRecord>> UpdateEmail(
        Guid id, [FromBody] UpsertMyEmailRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        var address = request.EmailAddress?.Trim();
        if (string.IsNullOrWhiteSpace(address)) return BadRequest("An email address is required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.UserEmails.FirstOrDefaultAsync(e => e.Id == id && e.AppUserId == userId, ct);
        if (entity is null) return NotFound();
        if (!await db.UserEmailTypes.AnyAsync(t => t.Id == request.UserEmailTypeId, ct))
            return BadRequest("Unknown email type.");

        var before = new UserEmail { Id = entity.Id, EmailAddress = entity.EmailAddress, IsPublic = entity.IsPublic, IsValidated = entity.IsValidated };

        // Changing the address is changing what is being claimed, so whatever was validated about
        // the old text says nothing about the new text. Re-validation starts over, and a public
        // email cannot survive that — it would otherwise stay published while pointing at an
        // address nobody has confirmed.
        var addressChanged = !string.Equals(entity.EmailAddress, address, StringComparison.OrdinalIgnoreCase);
        if (addressChanged)
        {
            entity.IsValidated = false;
            entity.DateValidated = null;
            entity.ValidationToken = null;
            entity.DateValidationSent = null;
            entity.IsPublic = false;
        }
        else if (request.IsPublic && !entity.IsValidated)
        {
            return BadRequest("This email address must be validated before it can be made public.");
        }
        else
        {
            entity.IsPublic = request.IsPublic;
        }

        entity.EmailAddress = address;
        entity.UserEmailTypeId = request.UserEmailTypeId;
        entity.IsPrimary = request.IsPrimary;
        entity.SortOrder = request.SortOrder;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        if (request.IsPrimary) await UnsetOtherPrimaryEmailsAsync(db, userId, ct, except: entity.Id);

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UserEmail), entity.Id, before, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpDelete("emails/{id:guid}")]
    public async Task<IActionResult> DeleteEmail(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.UserEmails.FirstOrDefaultAsync(e => e.Id == id && e.AppUserId == userId, ct);
        if (entity is null) return NotFound();

        db.UserEmails.Remove(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UserEmail), id, entity, userId, AppSources.WebApi));

        return NoContent();
    }

    /// <summary>
    /// Issues a fresh validation link and reports it back to the caller. Sending the actual email
    /// is best-effort — see the class-level note on <see cref="Ben.Data.Common.Interfaces.IEmailService"/>
    /// — so the response always carries the link itself, usable as a copy-paste fallback whenever
    /// SMTP is not configured (every environment today).
    /// </summary>
    [HttpPost("emails/{id:guid}/send-validation")]
    public async Task<ActionResult<SendValidationResponse>> SendValidation(
        Guid id, [FromServices] Ben.Data.Common.Interfaces.IEmailService emailService,
        [FromServices] IConfiguration configuration, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.UserEmails.FirstOrDefaultAsync(e => e.Id == id && e.AppUserId == userId, ct);
        if (entity is null) return NotFound();

        if (entity.IsValidated) return BadRequest("This email address is already validated.");

        // A resend inside the window is almost always a double-click, not a person who genuinely
        // needs a second link within a minute of the first.
        if (entity.DateValidationSent is { } lastSent && DateTime.UtcNow - lastSent < ResendCooldown)
            return BadRequest("A validation email was just sent. Please wait a minute before requesting another.");

        var before = new UserEmail { Id = entity.Id, DateValidationSent = entity.DateValidationSent };

        // Regenerating invalidates whatever link was already out there — only the newest one may
        // be redeemed, so an old email sitting in an inbox can't validate a since-changed address.
        entity.ValidationToken = GenerateToken();
        entity.DateValidationSent = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var appBaseUrl = configuration["AppBaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var link = $"{appBaseUrl}/validate-email/{entity.ValidationToken}";

        var emailSent = false;
        if (emailService.IsConfigured)
        {
            try
            {
                var maskedForSubject = System.Net.WebUtility.HtmlEncode(entity.EmailAddress);
                var body = $"<p>Confirm that <strong>{maskedForSubject}</strong> belongs to your {_site.Name} account.</p>" +
                           $"<p><a href=\"{link}\">Confirm this email address</a></p>" +
                           $"<p>This link expires in {ValidationLifetime.TotalDays:0} days.</p>";
                await emailService.SendAsync(entity.EmailAddress, "Confirm your email address", body, ct);
                emailSent = true;
            }
            catch { /* best-effort — the link below still works */ }
        }

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UserEmail), entity.Id, before, entity, userId, AppSources.WebApi));

        return Ok(new SendValidationResponse(link, emailSent));
    }

    // ── Phones ────────────────────────────────────────────────────────────────

    [HttpGet("phones")]
    public async Task<ActionResult<IEnumerable<MyPhoneRecord>>> GetPhones(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var rows = await db.UserPhones.AsNoTracking()
            .Where(p => p.AppUserId == userId)
            .OrderBy(p => p.DateCreated)
            .ToListAsync(ct);

        return Ok(rows.Select(ToRecord));
    }

    [HttpPost("phones")]
    public async Task<ActionResult<MyPhoneRecord>> CreatePhone(
        [FromBody] UpsertMyPhoneRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var number = request.PhoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number)) return BadRequest("A phone number is required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UserPhoneTypes.AnyAsync(t => t.Id == request.UserPhoneTypeId, ct))
            return BadRequest("Unknown phone type.");

        var entity = new UserPhone
        {
            Id = Guid.NewGuid(),
            AppUserId = userId,
            UserPhoneTypeId = request.UserPhoneTypeId,
            PhoneNumber = number,
            PhoneCountry = string.IsNullOrWhiteSpace(request.PhoneCountry) ? null : request.PhoneCountry.Trim(),
            IsPrimary = request.IsPrimary,
            IsCellular = request.IsCellular,
            IsPublic = request.IsPublic,
            // Phones have never had a validation flow — see the class remarks — so this stays
            // permanently the hardcoded-empty value the admin controllers already use, rather
            // than null, to match the column's non-nullable shape.
            IsValidated = false,
            ValidationToken = string.Empty,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        if (request.IsPrimary) await UnsetOtherPrimaryPhonesAsync(db, userId, ct);

        db.UserPhones.Add(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UserPhone), entity.Id, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpPut("phones/{id:guid}")]
    public async Task<ActionResult<MyPhoneRecord>> UpdatePhone(
        Guid id, [FromBody] UpsertMyPhoneRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var number = request.PhoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number)) return BadRequest("A phone number is required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.UserPhones.FirstOrDefaultAsync(p => p.Id == id && p.AppUserId == userId, ct);
        if (entity is null) return NotFound();
        if (!await db.UserPhoneTypes.AnyAsync(t => t.Id == request.UserPhoneTypeId, ct))
            return BadRequest("Unknown phone type.");

        var before = new UserPhone { Id = entity.Id, PhoneNumber = entity.PhoneNumber, IsPublic = entity.IsPublic };

        entity.PhoneNumber = number;
        entity.PhoneCountry = string.IsNullOrWhiteSpace(request.PhoneCountry) ? null : request.PhoneCountry.Trim();
        entity.UserPhoneTypeId = request.UserPhoneTypeId;
        entity.IsPrimary = request.IsPrimary;
        entity.IsCellular = request.IsCellular;
        entity.IsPublic = request.IsPublic;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        if (request.IsPrimary) await UnsetOtherPrimaryPhonesAsync(db, userId, ct, except: entity.Id);

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UserPhone), entity.Id, before, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpDelete("phones/{id:guid}")]
    public async Task<IActionResult> DeletePhone(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.UserPhones.FirstOrDefaultAsync(p => p.Id == id && p.AppUserId == userId, ct);
        if (entity is null) return NotFound();

        db.UserPhones.Remove(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UserPhone), id, entity, userId, AppSources.WebApi));

        return NoContent();
    }

    // ── Addresses ─────────────────────────────────────────────────────────────

    [HttpGet("addresses")]
    public async Task<ActionResult<IEnumerable<MyAddressRecord>>> GetAddresses(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var rows = await db.UserAddresses.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.DateCreated)
            .ToListAsync(ct);

        return Ok(rows.Select(ToRecord));
    }

    [HttpPost("addresses")]
    public async Task<ActionResult<MyAddressRecord>> CreateAddress(
        [FromBody] UpsertMyAddressRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(request.StreetAddress1) || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.ZipCode))
            return BadRequest("Street address, city, state and ZIP are required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UserAddressTypes.AnyAsync(t => t.Id == request.UserAddressTypeId, ct))
            return BadRequest("Unknown address type.");

        var entity = new UserAddress
        {
            Id = Guid.NewGuid(),
            AppUserId = userId,
            UserAddressTypeId = request.UserAddressTypeId,
            StreetAddress1 = request.StreetAddress1.Trim(),
            StreetAddress2 = string.IsNullOrWhiteSpace(request.StreetAddress2) ? null : request.StreetAddress2.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            ZipCode = request.ZipCode.Trim(),
            Country = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim(),
            IsPublic = request.IsPublic,
            SortOrder = request.SortOrder,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        await ApplyGeocodingAsync(entity, ct);

        db.UserAddresses.Add(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UserAddress), entity.Id, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpPut("addresses/{id:guid}")]
    public async Task<ActionResult<MyAddressRecord>> UpdateAddress(
        Guid id, [FromBody] UpsertMyAddressRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(request.StreetAddress1) || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.ZipCode))
            return BadRequest("Street address, city, state and ZIP are required.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.UserAddresses.FirstOrDefaultAsync(a => a.Id == id && a.AppUserId == userId, ct);
        if (entity is null) return NotFound();
        if (!await db.UserAddressTypes.AnyAsync(t => t.Id == request.UserAddressTypeId, ct))
            return BadRequest("Unknown address type.");

        var before = new UserAddress { Id = entity.Id, StreetAddress1 = entity.StreetAddress1, IsPublic = entity.IsPublic };

        entity.UserAddressTypeId = request.UserAddressTypeId;
        entity.StreetAddress1 = request.StreetAddress1.Trim();
        entity.StreetAddress2 = string.IsNullOrWhiteSpace(request.StreetAddress2) ? null : request.StreetAddress2.Trim();
        entity.City = request.City.Trim();
        entity.State = request.State.Trim();
        entity.ZipCode = request.ZipCode.Trim();
        entity.Country = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim();
        entity.IsPublic = request.IsPublic;
        entity.SortOrder = request.SortOrder;
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        await ApplyGeocodingAsync(entity, ct);

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UserAddress), entity.Id, before, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.UserAddresses.FirstOrDefaultAsync(a => a.Id == id && a.AppUserId == userId, ct);
        if (entity is null) return NotFound();

        db.UserAddresses.Remove(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UserAddress), id, entity, userId, AppSources.WebApi));

        return NoContent();
    }

    // ── Links ─────────────────────────────────────────────────────────────────

    [HttpGet("links")]
    public async Task<ActionResult<IEnumerable<MyLinkRecord>>> GetLinks(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var rows = await db.UserLinks.AsNoTracking()
            .Where(l => l.AppUserId == userId)
            .OrderBy(l => l.DateCreated)
            .ToListAsync(ct);

        return Ok(rows.Select(ToRecord));
    }

    [HttpPost("links")]
    public async Task<ActionResult<MyLinkRecord>> CreateLink(
        [FromBody] UpsertMyLinkRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var url = request.LinkUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return BadRequest("A URL is required.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return BadRequest("Enter a valid http:// or https:// URL.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UserLinkTypes.AnyAsync(t => t.Id == request.UserLinkTypeId, ct))
            return BadRequest("Unknown link type.");

        var entity = new UserLink
        {
            Id = Guid.NewGuid(),
            AppUserId = userId,
            UserLinkTypeId = request.UserLinkTypeId,
            LinkUrl = uri.ToString(),
            DisplayText = string.IsNullOrWhiteSpace(request.DisplayText) ? null : request.DisplayText.Trim(),
            IsActive = true,
            IsPublic = request.IsPublic,
            // Self-service links are never pre-approved — that flag belongs to whatever curation
            // step this app eventually adds, not to the person who typed the link.
            IsVerifiedApproved = false,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UserLinks.Add(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UserLink), entity.Id, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpPut("links/{id:guid}")]
    public async Task<ActionResult<MyLinkRecord>> UpdateLink(
        Guid id, [FromBody] UpsertMyLinkRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var url = request.LinkUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return BadRequest("A URL is required.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return BadRequest("Enter a valid http:// or https:// URL.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.UserLinks.FirstOrDefaultAsync(l => l.Id == id && l.AppUserId == userId, ct);
        if (entity is null) return NotFound();
        if (!await db.UserLinkTypes.AnyAsync(t => t.Id == request.UserLinkTypeId, ct))
            return BadRequest("Unknown link type.");

        var before = new UserLink { Id = entity.Id, LinkUrl = entity.LinkUrl, IsPublic = entity.IsPublic };

        entity.UserLinkTypeId = request.UserLinkTypeId;
        entity.LinkUrl = uri.ToString();
        entity.DisplayText = string.IsNullOrWhiteSpace(request.DisplayText) ? null : request.DisplayText.Trim();
        entity.IsPublic = request.IsPublic;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        // A changed URL is a different destination; whatever review it had no longer applies.
        entity.IsVerifiedApproved = false;

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UserLink), entity.Id, before, entity, userId, AppSources.WebApi));

        return Ok(ToRecord(entity));
    }

    [HttpDelete("links/{id:guid}")]
    public async Task<IActionResult> DeleteLink(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var entity = await db.UserLinks.FirstOrDefaultAsync(l => l.Id == id && l.AppUserId == userId, ct);
        if (entity is null) return NotFound();

        db.UserLinks.Remove(entity);
        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UserLink), id, entity, userId, AppSources.WebApi));

        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ValidationLifetime = TimeSpan.FromDays(7);

    private static string GenerateToken()
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static async Task UnsetOtherPrimaryEmailsAsync(
        BenDataContext db, Guid userId, CancellationToken ct, Guid? except = null)
    {
        var others = await db.UserEmails
            .Where(e => e.AppUserId == userId && e.IsPrimary && e.Id != except)
            .ToListAsync(ct);
        foreach (var o in others) { o.IsPrimary = false; o.DateUpdated = DateTime.UtcNow; o.UpdatedByAppUserId = userId; }
    }

    private static async Task UnsetOtherPrimaryPhonesAsync(
        BenDataContext db, Guid userId, CancellationToken ct, Guid? except = null)
    {
        var others = await db.UserPhones
            .Where(p => p.AppUserId == userId && p.IsPrimary && p.Id != except)
            .ToListAsync(ct);
        foreach (var o in others) { o.IsPrimary = false; o.DateUpdated = DateTime.UtcNow; o.UpdatedByAppUserId = userId; }
    }

    /// <summary>Mirrors <c>AdminUserAddressController.ApplyGeocodingAsync</c> for the self-service path.</summary>
    private static async Task ApplyGeocodingAsync(UserAddress entity, CancellationToken ct)
    {
        // If the client already resolved coordinates (from the live map preview), trust them
        // rather than re-querying the geocoder for the same address a second time.
        if (entity.Latitude.HasValue && entity.Longitude.HasValue)
            return;

        var result = await AddressGeocodingService.TryResolveCoordinatesAsync(
            entity.StreetAddress1, entity.StreetAddress2,
            entity.City, entity.State, entity.ZipCode, entity.Country, ct);
        entity.Latitude = result.Latitude;
        entity.Longitude = result.Longitude;
        entity.GeocodingResponseJson = result.RawResponseJson;
        entity.GeocodingResultType = result.ResultType;
    }

    private static MyEmailRecord ToRecord(UserEmail e) => new(
        e.Id, e.UserEmailTypeId, e.EmailAddress, e.IsPrimary, e.IsPublic,
        e.IsValidated, e.DateValidated, e.DateValidationSent, e.SortOrder);

    private static MyPhoneRecord ToRecord(UserPhone p) => new(
        p.Id, p.UserPhoneTypeId, p.PhoneNumber, p.PhoneCountry, p.IsPrimary, p.IsCellular, p.IsPublic);

    private static MyAddressRecord ToRecord(UserAddress a) => new(
        a.Id, a.UserAddressTypeId, a.StreetAddress1, a.StreetAddress2, a.City, a.State, a.ZipCode,
        a.Country, a.IsPublic, a.SortOrder, a.Latitude, a.Longitude);

    private static MyLinkRecord ToRecord(UserLink l) => new(
        l.Id, l.UserLinkTypeId, l.LinkUrl, l.DisplayText, l.IsPublic, l.IsVerifiedApproved);
}

// ── Request / response records ──────────────────────────────────────────────
// Deliberately not the admin AdminXAdminRecord shapes for requests: those exist for a caller that
// supplies AppUserId (a SuperAdmin acting on someone else). Response shapes are new records too,
// even though they overlap heavily with the admin ones, so the self-service contract can carry its
// own fields (DateValidationSent) without touching the admin surface.

public sealed record MyEmailRecord(
    Guid Id, Guid UserEmailTypeId, string EmailAddress, bool IsPrimary, bool IsPublic,
    bool IsValidated, DateTime? DateValidated, DateTime? DateValidationSent, int SortOrder);

public sealed record UpsertMyEmailRequest(
    Guid UserEmailTypeId, string? EmailAddress, bool IsPrimary, bool IsPublic, int SortOrder = 0);

public sealed record SendValidationResponse(string ValidationLink, bool EmailSent);

public sealed record MyPhoneRecord(
    Guid Id, Guid UserPhoneTypeId, string PhoneNumber, string? PhoneCountry,
    bool IsPrimary, bool IsCellular, bool IsPublic);

public sealed record UpsertMyPhoneRequest(
    Guid UserPhoneTypeId, string? PhoneNumber, string? PhoneCountry,
    bool IsPrimary, bool IsCellular, bool IsPublic);

public sealed record MyAddressRecord(
    Guid Id, Guid UserAddressTypeId, string StreetAddress1, string? StreetAddress2,
    string City, string State, string ZipCode, string Country, bool IsPublic, int SortOrder,
    decimal? Latitude, decimal? Longitude);

public sealed record UpsertMyAddressRequest(
    Guid UserAddressTypeId, string? StreetAddress1, string? StreetAddress2,
    string? City, string? State, string? ZipCode, string? Country, bool IsPublic, int SortOrder = 0,
    decimal? Latitude = null, decimal? Longitude = null);

public sealed record MyLinkRecord(
    Guid Id, Guid UserLinkTypeId, string LinkUrl, string? DisplayText, bool IsPublic, bool IsVerifiedApproved);

public sealed record UpsertMyLinkRequest(
    Guid UserLinkTypeId, string? LinkUrl, string? DisplayText, bool IsPublic);
