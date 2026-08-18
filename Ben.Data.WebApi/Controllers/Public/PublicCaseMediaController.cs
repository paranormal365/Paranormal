using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Cms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Serves the bytes of a case file that a group has published — the only anonymous route to a
/// case's media, and deliberately the narrowest one that can exist.
/// </summary>
/// <remarks>
/// <para><b>Why this had to be written.</b> <see cref="CaseMediaPublication"/> answers "which of a
/// case's files may be published", and item #80's page templates were built on that answer — but
/// nothing could actually serve the bytes. <c>/api/upload-files/{id}/download</c> gates on
/// <see cref="Ben.Data.WebApi.Services.Access.FileAudienceAccess.CanViewFileAsync"/>, which for an
/// anonymous caller grants only files flagged <c>IsPublic</c> or shared to a Public target. A photo
/// on a Public timeline entry of a public case is neither, so a gallery pointing at that endpoint
/// would have rendered a page of broken images. The rule said publishable; the pipe said no.</para>
///
/// <para><b>The alternative was worse.</b> The cheap fix is to set <c>IsPublic</c> on the file when
/// an author picks it. That flag is global and permanent — it would outlive the page, survive the
/// timeline entry being pulled back to private, and grant the file to every other endpoint in the
/// app at the same time. Publishing a photo on one page would quietly hand it out everywhere,
/// forever, which is the opposite of the binding-not-copying discipline the rest of item #80 is
/// built on.</para>
///
/// <para><b>So the gate is asked here, per request.</b> The route carries the case because
/// <see cref="CaseMediaPublication.MayPublishAsync"/> is a question about a file <i>in the context
/// of a case</i>, and a bare file id could not have posed it. Narrow the entry's visibility, close
/// the case, or unlink the file, and this endpoint stops answering — with no page needing to be
/// edited and nobody needing to remember which pages used it.</para>
///
/// <para><b>404, never 403.</b> A refusal that distinguishes "no such file" from "that file exists
/// but is not published" tells an anonymous caller which ids are real. Both answers are the same
/// answer.</para>
/// </remarks>
[ApiController]
[Route("api/public/cases/{caseId:guid}/media")]
[AllowAnonymous]
public sealed class PublicCaseMediaController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;

    public PublicCaseMediaController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage)
    { _db = db; _fileStorage = fileStorage; }

    /// <summary>Streams one published file's bytes, or 404.</summary>
    /// <remarks>
    /// The publication check runs <b>before</b> the file row is read, so an id that is not
    /// publishable never reaches storage — a refusal should not be distinguishable by how long it
    /// took, and it should not touch the disk on the way to saying no.
    /// </remarks>
    [HttpGet("{uploadFileId:guid}")]
    public async Task<IActionResult> Get(Guid caseId, Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await CaseMediaPublication.MayPublishAsync(db, caseId, uploadFileId, ct))
            return NotFound();

        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (file is null) return NotFound();

        // Same disk-then-blob fallback as UploadFileController.Download: rows predating the storage
        // migration still keep their bytes in the column.
        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            var stream = await _fileStorage.OpenReadAsync(file.StoragePath, ct);
            return File(stream, file.ContentType, file.FileName);
        }

        if (file.FileData is not null)
            return File(file.FileData, file.ContentType, file.FileName);

        return NotFound();
    }
}
