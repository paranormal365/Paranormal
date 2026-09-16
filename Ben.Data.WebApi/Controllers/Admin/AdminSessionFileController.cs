using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Every session file on the server, newest first.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "I think the SuperAdmin should have another grid like the user grid, but it is
/// uploaded .ben files and the ability to view them by clicking a button in the row. They are
/// sorted new to old." And: "Also in the grid, the name of the person who recorded it and the name
/// of the person who uploaded it."
/// </para>
/// <para>
/// <b>Two names, because they are two different facts.</b> A device can be handed to a colleague
/// to upload, and a session recorded while signed out has nobody's name on it at all. The upload
/// door has always kept those apart rather than quietly attributing a night to whoever sent it, so
/// a list that collapsed them would be the first place in the product to lose the distinction.
/// Nobody-recorded-it is shown as such and never as the uploader.
/// </para>
/// <para>
/// Read-only. Deleting a session is somebody's own decision on their own session, or the orphan
/// sweep for sessions whose bytes are gone — neither belongs on a list whose job is to show what
/// is there.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/session-files")]
public sealed class AdminSessionFileController(
    IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    /// <summary>How many rows one request returns. Enough to scroll, bounded so a grid cannot ask
    /// for every session ever recorded in one go.</summary>
    private const int MaxRows = 500;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SessionFileRecord>>> GetAll(
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.FieldSessionUploads.AsNoTracking()
            // Newest first, by when the session was RECORDED rather than when it arrived: a night
            // driven home from and sent the next morning belongs where it happened in the list.
            .OrderByDescending(s => s.StartedAt)
            .Take(MaxRows)
            .Select(s => new SessionFileRecord(
                s.Id,
                s.IsBundle,
                s.DocumentUploadFile.FileName,
                s.DocumentUploadFile.FileSize,
                s.LocationLabel,
                s.StartedAt,
                s.EndedAt,
                s.ReadingCount,
                s.MarkerCount,
                s.Files.Count,
                // The person the device said recorded it. Null is an ordinary answer and is shown
                // as one — the app records with nobody signed in on purpose.
                s.RecordedByAppUser != null ? s.RecordedByAppUser.DisplayName : s.RecordedByName,
                s.SubmittedByAppUser.DisplayName,
                s.InvestigationId,
                s.Investigation != null ? s.Investigation.Title : null,
                s.PublishedAtUtc))
            .ToListAsync(ct);

        return Ok(rows);
    }
}

/// <param name="IsBundle">
/// Whether this arrived as one <c>.ben</c>. False for everything sent by the approved 1.0.2 build,
/// which uploads a document and a file per recording — both shapes are on this list, and the
/// column says which is which rather than hiding the older one.
/// </param>
/// <param name="RecordedByName">
/// Null when nobody was signed in on the device. Not the uploader's name: those are different
/// facts and the upload door has never conflated them.
/// </param>
public sealed record SessionFileRecord(
    Guid SessionId,
    bool IsBundle,
    string FileName,
    long FileSize,
    string? LocationLabel,
    DateTime StartedAt,
    DateTime? EndedAt,
    int ReadingCount,
    int MarkerCount,
    int FileCount,
    string? RecordedByName,
    string? UploadedByName,
    Guid? InvestigationId,
    string? InvestigationTitle,
    DateTime? PublishedAtUtc);
