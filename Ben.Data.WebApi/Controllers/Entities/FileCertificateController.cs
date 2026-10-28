using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Files;
using Ben.Service.Models.FieldSessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A file's certificate of authenticity, for somebody who can open the file (10/28/2026). The public
/// twin is on <c>PublicFieldSessionController</c>, for the clips a session's public page shows.
/// </summary>
[Authorize]
[Route("api/upload-files/{fileId:guid}/certificate")]
public sealed class FileCertificateController(
    IDbContextFactory<BenDataContext> dbFactory, FileProvenanceService provenance) : BenControllerBase
{
    /// <summary>The certificate, or 404 when the file has none or this person can't open it.</summary>
    [HttpGet]
    public async Task<ActionResult<FileCertificateRecord>> Get(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return NotFound();
        return await FileProvenanceService.DescribeAsync(db, fileId, ct) is { } certificate
            ? Ok(certificate)
            : NotFound("This file has no certificate: it wasn't made from a field session's recording on this site.");
    }

    /// <summary>Checks the file's bytes now, and the certificate against the site's seal.</summary>
    [HttpPost("check")]
    [EnableRateLimiting(RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<FileCertificateCheck>> Check(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return NotFound();
        return await provenance.CheckAsync(db, fileId, ct) is { } check ? Ok(check) : NotFound();
    }
}
