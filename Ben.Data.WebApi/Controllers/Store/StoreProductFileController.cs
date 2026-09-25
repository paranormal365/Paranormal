using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Store;

/// <summary>
/// A product's file for the people who keep it (store sellers, backlog 251, P11): the store's staff,
/// and the product's own seller — private files included. Buyers download from their order instead.
/// </summary>
/// <remarks>Never behind the store switch, like the editors that list these files. Anybody else is
/// told "not found": a 403 would confirm the file exists.</remarks>
[ApiController]
[Authorize]
[Route("api/store/product-files")]
public sealed class StoreProductFileController(
    IDbContextFactory<BenDataContext> dbFactory, StoreImageStorage storage, IAuthorizationService authorization) : BenControllerBase
{
    [HttpGet("{fileId:guid}/download")]
    public async Task<IActionResult> Download(Guid fileId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var file = await MayKeepAsync(db, fileId, ct);
        if (file is not { UploadFile: { StoragePath: { Length: > 0 } path } upload }) return NotFound();
        return File(await storage.OpenAsync(path, ct), upload.ContentType ?? "application/octet-stream", upload.FileName, enableRangeProcessing: true);
    }

    [HttpGet("{fileId:guid}/manual")]
    public async Task<ActionResult<StoreManualRecord>> Manual(Guid fileId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var file = await MayKeepAsync(db, fileId, ct);
        if (file?.ManualHtml is not { } html) return NotFound();
        return Ok(new StoreManualRecord(file.Id, file.Product.Name, file.Title, file.VersionLabel, html));
    }

    private async Task<Ben.Data.Source.Entities.StoreProductFile?> MayKeepAsync(BenDataContext db, Guid fileId, CancellationToken ct)
    {
        var file = await db.StoreProductFiles.AsNoTracking().Include(f => f.UploadFile).Include(f => f.Product)
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return null;
        if ((await authorization.AuthorizeAsync(User, RoleNames.SuperAdmin)).Succeeded) return file;
        return GetCurrentUserIdOrNull() is { } me && file.Product.SellerAppUserId == me ? file : null;
    }
}
