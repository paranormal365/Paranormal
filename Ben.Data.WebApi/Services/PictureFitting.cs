using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// A photograph made fit for a public page: fitted inside 1920×1080 and re-encoded, so no camera and no
/// co-ordinates survive (item 235).
/// </summary>
/// <remarks>
/// Shared by an event's gallery and a venue's photo library, so the two never disagree about what a public
/// picture may carry.
/// </remarks>
public static class PictureFitting
{
    public sealed record Fitted(byte[] Bytes, int Width, int Height);

    /// <returns>The fitted picture, or the sentence saying why it could not be read.</returns>
    public static async Task<(Fitted? Picture, string? Refusal)> FitAsync(
        IFormFile file, IMediaSanitizationService images, CancellationToken ct)
    {
        if (!images.CanSanitize(file.ContentType)) return (null, "This takes photographs — JPEG, PNG or similar.");

        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);

        try
        {
            var info = SkiaSharp.SKBitmap.DecodeBounds(buffer.ToArray());
            if (info.Width <= 0 || info.Height <= 0) return (null, "That picture could not be read.");
            var scale = Math.Min(Math.Min((double)TourGalleryImage.MaxWidth / info.Width, (double)TourGalleryImage.MaxHeight / info.Height), 1.0);
            var fitted = images.Sanitize(buffer.ToArray(), Math.Max(1, (int)Math.Round(Math.Max(info.Width, info.Height) * scale)));
            var bounds = SkiaSharp.SKBitmap.DecodeBounds(fitted);
            return (new Fitted(fitted, bounds.Width, bounds.Height), null);
        }
        catch (UnreadableImageException)
        {
            return (null, "That picture could not be read.");
        }
    }
}
