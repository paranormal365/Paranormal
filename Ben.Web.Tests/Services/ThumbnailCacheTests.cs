using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A cached thumbnail with nothing in it is made again rather than served as a picture.
/// </summary>
/// <remarks>
/// Storage used to be able to leave an empty file behind when a write was cut short, and the thumbnail cache took any
/// file that existed for a finished thumbnail. Two pictures in the media library then came back as zero-byte images on
/// every request, for good (found walking the site after hosted events merged, 2026-09-13). Storage no longer leaves
/// such files; this is for the ones it already left.
/// </remarks>
public sealed class ThumbnailCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ben-thumb-test-{Guid.NewGuid():N}");
    private readonly LocalFileStorageService _storage;
    private readonly MediaIngestService _media;

    public ThumbnailCacheTests()
    {
        _storage = new LocalFileStorageService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = _root })
            .Build());
        _media = new MediaIngestService(_storage, new FileMetadataExtractorService(), new MediaSanitizationService(),
            TestMedia.Stripper(), NullLogger<MediaIngestService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        await using (stream)
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }

    [Fact]
    public async Task An_empty_cached_thumbnail_is_made_again_and_kept()
    {
        var photo = _storage.OrgFilePath(Guid.NewGuid(), "venue/hallway.jpg");
        await _storage.WriteAsync(photo, new MemoryStream(TestImages.Jpeg(640, 480)));

        var thumbnailPath = new MediaSanitizationService().ThumbnailPathFor(photo);
        await _storage.WriteAsync(thumbnailPath, new MemoryStream([]));

        var served = await ReadAllAsync((await _media.OpenThumbnailAsync(photo, CancellationToken.None))!);

        Assert.NotEmpty(served);
        Assert.Equal(0xFF, served[0]);   // a JPEG starts FF D8
        Assert.Equal(0xD8, served[1]);
        Assert.Equal(served, await ReadAllAsync(await _storage.OpenReadAsync(thumbnailPath)));
    }

    [Fact]
    public async Task A_cached_thumbnail_with_something_in_it_is_served_as_it_is()
    {
        var photo = _storage.OrgFilePath(Guid.NewGuid(), "venue/ballroom.jpg");
        await _storage.WriteAsync(photo, new MemoryStream(TestImages.Jpeg(640, 480)));
        var cached = TestImages.Jpeg(16, 12);
        await _storage.WriteAsync(new MediaSanitizationService().ThumbnailPathFor(photo), new MemoryStream(cached));

        var served = await ReadAllAsync((await _media.OpenThumbnailAsync(photo, CancellationToken.None))!);

        Assert.Equal(cached, served);
    }
}
