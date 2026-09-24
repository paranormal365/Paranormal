using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>Why a store picture was not stored.</summary>
public enum StoreImageRefusal
{
    None = 0,
    NotAPicture = 1,
    Unreadable = 2,
}

/// <summary>
/// Files a store picture — a category's or a product's — as a site-owned upload (storefront S1.3).
/// </summary>
/// <remarks>
/// <para><b>Ownerless on purpose.</b> <c>UploadFile.AppUserId</c> is the model's one cascading
/// key to AppUsers, so a picture owned by the admin who added it would vanish with their account
/// and take the product's gallery with it. The row names the admin only as its creator; both owner
/// columns are null and it never expires, so the retention sweep leaves it alone.</para>
///
/// <para><b>What is stored is a copy.</b> Decoded and re-encoded as JPEG at 1600 px on its long
/// edge, which drops EXIF, camera and co-ordinates by construction, with a 400 px thumbnail
/// beside it at <see cref="IMediaSanitizationService.ThumbnailPathFor"/>. The metadata row
/// describes the copy, not the original — the original is never kept.</para>
/// </remarks>
public sealed class StoreImageStorage(
    IFileStorageService storage, IMediaSanitizationService images, IMediaIngestService ingest)
{
    /// <summary>Long edge of the served picture — sharp on a retina product page, small enough for a phone.</summary>
    public const int LongEdge = 1600;

    /// <summary>
    /// Writes the copy and its thumbnail and adds the <see cref="UploadFile"/> and metadata rows
    /// to <paramref name="db"/> (unsaved — the caller saves them with the row that points at them).
    /// </summary>
    /// <param name="folder">Under <c>store/</c>, e.g. <c>products/{id:N}</c>.</param>
    public async Task<(UploadFile? File, StoreImageRefusal Refusal)> SaveAsync(
        BenDataContext db, byte[] original, string? contentType, string fileName, string folder, Guid adminId,
        CancellationToken ct)
    {
        if (original.Length == 0 || contentType is null
            || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (null, StoreImageRefusal.NotAPicture);

        byte[] served, thumb;
        int width, height;
        try
        {
            served = images.Sanitize(original, LongEdge);
            thumb = images.Sanitize(served, IMediaSanitizationService.ThumbnailLongEdge);
            var bounds = SkiaSharp.SKBitmap.DecodeBounds(served);
            (width, height) = (bounds.Width, bounds.Height);
        }
        catch (UnreadableImageException) { return (null, StoreImageRefusal.Unreadable); }

        var storedName = $"{Guid.NewGuid()}.jpg";
        var path = $"store/{folder}/{storedName}";
        await storage.WriteAsync(path, new MemoryStream(served), ct);
        await storage.WriteAsync(images.ThumbnailPathFor(path), new MemoryStream(thumb), ct);

        var now = DateTime.UtcNow;
        var file = new UploadFile
        {
            Id = Guid.NewGuid(),
            UploadFileTypeId = UploadFileTypeSeeder.StoreImageFileTypeId,
            AppUserId = null,
            OwnerOrganizationId = null,
            FileName = Path.ChangeExtension(Path.GetFileName(fileName), ".jpg"),
            StoredFileName = storedName,
            ContentType = "image/jpeg",
            FileSize = served.LongLength,
            StoragePath = path,
            IsPublic = true,
            ExpiresAtUtc = null,
            DateCreated = now,
            CreatedByAppUserId = adminId,
        };
        db.UploadFiles.Add(file);
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = file.Id, MediaKind = "Image",
            WidthPixels = width, HeightPixels = height, ExtractedAtUtc = now,
        });
        return (file, StoreImageRefusal.None);
    }

    /// <summary>
    /// A new file with the same bytes, for a duplicated product — never a shared row, so deleting
    /// either product's picture cannot take the other's.
    /// </summary>
    public async Task<UploadFile> CopyAsync(BenDataContext db, Guid uploadFileId, string folder, Guid adminId, CancellationToken ct)
    {
        var source = await db.UploadFiles.AsNoTracking().SingleAsync(f => f.Id == uploadFileId, ct);
        var metadata = await db.UploadFileMetadata.AsNoTracking().FirstOrDefaultAsync(m => m.UploadFileId == uploadFileId, ct);
        var now = DateTime.UtcNow;
        var storedName = $"{Guid.NewGuid()}.jpg";
        string? path = null;

        // Seeded pictures live in the row itself (FileData); uploaded ones on storage.
        if (!string.IsNullOrWhiteSpace(source.StoragePath))
        {
            path = $"store/{folder}/{storedName}";
            await CopyFileAsync(source.StoragePath, path, ct);
            var thumb = images.ThumbnailPathFor(source.StoragePath);
            if (storage.Exists(thumb)) await CopyFileAsync(thumb, images.ThumbnailPathFor(path), ct);
        }

        var copy = new UploadFile
        {
            Id = Guid.NewGuid(),
            UploadFileTypeId = UploadFileTypeSeeder.StoreImageFileTypeId,
            AppUserId = null,
            OwnerOrganizationId = null,
            FileName = source.FileName,
            StoredFileName = storedName,
            ContentType = source.ContentType,
            FileSize = source.FileSize,
            StoragePath = path,
            FileData = path is null ? source.FileData : null,
            IsPublic = true,
            ExpiresAtUtc = null,
            DateCreated = now,
            CreatedByAppUserId = adminId,
        };
        db.UploadFiles.Add(copy);
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = copy.Id, MediaKind = "Image",
            WidthPixels = metadata?.WidthPixels, HeightPixels = metadata?.HeightPixels, ExtractedAtUtc = now,
        });
        return copy;
    }

    private async Task CopyFileAsync(string from, string to, CancellationToken ct)
    {
        await using var read = await storage.OpenReadAsync(from, ct);
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        await storage.WriteAsync(to, buffer, ct);
    }

    /// <summary>
    /// Removes a picture nothing points at any more — the row, then its bytes. A row something
    /// else still holds — an order line keeps the picture it was bought with — is left.
    /// </summary>
    public async Task RemoveAsync(BenDataContext db, Guid uploadFileId, CancellationToken ct)
    {
        var path = await db.UploadFiles.Where(f => f.Id == uploadFileId).Select(f => f.StoragePath).FirstOrDefaultAsync(ct);
        if (await Admin.UploadFileRows.TryDeleteAsync(db, uploadFileId, ct) && !string.IsNullOrWhiteSpace(path))
            await ingest.DeleteAllAsync(path, ct);
    }
}
