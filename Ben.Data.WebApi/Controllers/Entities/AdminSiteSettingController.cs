using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Sitewide configuration. SuperAdmin only — these values affect every organization and every
/// visitor, and no org-level role should be able to change them.
/// </summary>
/// <remarks>
/// Deliberately holds nothing personal. Anything scoped to one person belongs on their profile,
/// anything scoped to one organization belongs in that org's settings.
/// </remarks>
[ApiController]
[Authorize(Roles = RoleNames.SuperAdmin)]
[Route("api/admin/site-settings")]
public sealed class AdminSiteSettingController : BenControllerBase
{
    private readonly SiteSettingsService _settings;
    private readonly IAuditLogService _auditLog;

    public AdminSiteSettingController(SiteSettingsService settings, IAuditLogService auditLog)
    {
        _settings = settings;
        _auditLog = auditLog;
    }

    /// <summary>Every setting the site declares, including ones never yet given a value.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SiteSettingRecord>>> GetAll(CancellationToken ct)
    {
        var rows = await _settings.GetAllAsync(ct);
        return Ok(rows.Select(ToRecord));
    }

    /// <summary>Sets one setting. An empty value clears it.</summary>
    [HttpPut("{key}")]
    public async Task<ActionResult<SiteSettingRecord>> Set(
        string key, [FromBody] SetSiteSettingRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        // Only keys the site declares. Without this the table becomes a junk drawer of typo'd
        // keys that no code reads and nobody can tell apart from real settings.
        if (!SiteSettingsService.IsKnownKey(key))
            return BadRequest($"'{key}' is not a known site setting.");

        var before = (await _settings.GetAllAsync(ct)).First(s => s.Key == key);
        var beforeSnapshot = new SiteSetting { Id = before.Id, Key = before.Key, Value = before.Value };

        var row = await _settings.SetAsync(key, request.Value, userId, ct);

        // Audited like any other mutation: a sitewide switch changing under everyone is exactly
        // the kind of thing someone needs to be able to trace later.
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(SiteSetting), row.Id, beforeSnapshot, row, userId, AppSources.WebApi, ct));

        return Ok(ToRecord(row));
    }

    private static SiteSettingRecord ToRecord(SiteSetting s)
        => new(s.Key, SiteSettingsService.LabelFor(s.Key), s.Value, s.Description, s.DateUpdated ?? s.DateCreated);
}
