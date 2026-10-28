using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Places;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// A comparison scan inside a session, and what it carries (LiDAR plan, phase 2).
/// </summary>
/// <remarks>
/// <para>A comparison is a stored ZIP inside the session's stored ZIP, so its members are a range of
/// a range of the one file on disk — nothing is unpacked, the same way every recording is served.</para>
///
/// <para>A change names its picture <c>scans/compare-001.scan#changes/01.jpg</c>, and a URL cannot
/// carry the <c>#</c> to a server. The member follows the file as a path instead:
/// <c>…/scans/compare-001.scan/changes/01.jpg</c>. Whoever may read the session may read these;
/// the public copy of a session leaves comparisons out.</para>
/// </remarks>
public sealed partial class FieldSessionUploadController
{
    /// <summary>A comparison scan in the session, whole; or one member of it.</summary>
    [HttpGet("{sessionId:guid}/scans/{scanFile}/{**member}")]
    public async Task<IActionResult> GetScanMember(Guid sessionId, string scanFile, string? member, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || !session.IsBundle) return NotFound();
        if (!await MayReadAsync(db, session, userId, ct)) return NotFound();

        var entryPath = $"scans/{scanFile}";
        if (!scanFile.EndsWith(".scan", StringComparison.Ordinal) || BenBundle.RefusalForPath(entryPath) is not null)
            return NotFound();

        var bundlePath = await db.UploadFiles.AsNoTracking()
            .Where(f => f.Id == session.DocumentUploadFileId)
            .Select(f => f.StoragePath)
            .FirstOrDefaultAsync(ct);
        if (bundlePath is not { Length: > 0 } || !_fileStorage.Exists(bundlePath)) return NotFound();

        var outer = await _bundles.OpenEntryAsync(bundlePath, entryPath, ct);
        if (outer is null) return NotFound();
        if (string.IsNullOrEmpty(member))
            return File(outer, FieldSessionFileGuard.PlaceScanContentType, scanFile, enableRangeProcessing: true);

        try
        {
            if (BenBundle.RefusalForPath(member, "scan") is not null) throw new BenBundleFormatException("not a member");
            var inner = await BenBundle.ReadAsync(outer, BenBundleRules.Scan, ct);
            if (inner.Find(member) is not { } found) throw new BenBundleFormatException("no such member");

            return File(new BundleWindowStream(outer, found.Offset, found.Length),
                        PlaceScanManifest.ContentTypeFor(member), Path.GetFileName(member), enableRangeProcessing: true);
        }
        catch (BenBundleFormatException)
        {
            await outer.DisposeAsync();
            return NotFound();
        }
    }
}
