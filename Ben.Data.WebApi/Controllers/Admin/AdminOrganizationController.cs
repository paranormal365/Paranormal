using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/organizations")]
public sealed class AdminOrganizationController : AdminEntityControllerBase<Organization, OrganizationAdminRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public AdminOrganizationController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog)
    {
        _dbFactory = dbContextFactory;
        _mapper    = mapper;
        _auditLog  = auditLog;
    }

    /// <summary>Suppresses the base Create(Organization entity) route — use CreateOrganization instead.</summary>
    [NonAction]
    public override Task<ActionResult<OrganizationAdminRecord>> Create(
        [FromBody] Organization entity, CancellationToken cancellationToken)
        => throw new NotSupportedException("Use POST /api/admin/organizations with AdminCreateOrganizationRequest.");

    /// <summary>Creates a new organization. The current SuperAdmin user is recorded as creator.</summary>
    [HttpPost]
    public async Task<ActionResult<OrganizationAdminRecord>> CreateOrganization(
        [FromBody] AdminCreateOrganizationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(request.UrlName))
            return BadRequest("UrlName is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var urlName = request.UrlName.Trim().ToLowerInvariant();
        if (await db.Organizations.AnyAsync(o => o.UrlName == urlName, ct))
            return BadRequest($"UrlName '{urlName}' is already in use.");

        var org = new Organization
        {
            Name               = request.Name.Trim(),
            UrlName            = urlName,
            PublicPhone        = request.PublicPhone?.Trim(),
            PublicEmail        = request.PublicEmail?.Trim(),
            PublicWebsite      = request.PublicWebsite?.Trim(),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = GetCurrentUserIdOrThrow()
        };

        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Organization), org.Id, org, GetCurrentUserId(), AppSources.WebApi, ct));

        return CreatedAtAction(nameof(GetById), new { id = org.Id }, _mapper.Map<OrganizationAdminRecord>(org));
    }
}

public sealed record AdminCreateOrganizationRequest(string Name, string UrlName,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null);

