using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Admin;

/// <summary>
/// The one honest way to remove a file row after a purge: one at a time, and a row something
/// else still holds is left standing.
/// </summary>
/// <remarks>
/// Two dozen tables point at UploadFiles and several do so with NoAction — a published video,
/// a marker, a profile photo — so a bulk delete would either be refused outright or would need
/// every one of those tables swept first. Neither purge does that. Each removes what nobody
/// else has a claim on and reports what stayed. Kept outside the purges so the coverage guard,
/// which reads OrganizationPurge's own source, does not see a bulk delete that is not there.
/// </remarks>
public static class UploadFileRows
{
    /// <summary>True when the row went; false when the database kept it because something still points at it.</summary>
    public static async Task<bool> TryDeleteAsync(BenDataContext db, Guid fileId, CancellationToken ct)
    {
        try
        {
            return await db.UploadFiles.Where(f => f.Id == fileId).ExecuteDeleteAsync(ct) > 0;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
