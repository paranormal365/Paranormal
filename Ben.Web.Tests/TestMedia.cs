using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ben.Web.Tests;

/// <summary>
/// The media services a controller fixture needs, built once here rather than spelled out at
/// every construction site.
/// </summary>
public static class TestMedia
{
    /// <summary>
    /// A stripper that reports itself UNAVAILABLE — fixtures must never shell out to ffmpeg, and
    /// "no tool configured" is the honest state of a test host. The paths that matter when a tool
    /// IS present are covered directly in <c>AvMetadataStrippingTests</c>.
    /// </summary>
    public static IAvMetadataStripper Stripper()
    {
        var mock = new Mock<IAvMetadataStripper>();
        mock.Setup(s => s.IsAvailable).Returns(false);
        mock.Setup(s => s.CanStrip(It.IsAny<string>())).Returns(false);
        return mock.Object;
    }

    /// <summary>A real ingest service over mocked storage: it extracts and sanitizes for real.</summary>
    public static IMediaIngestService Ingest()
        => new MediaIngestService(
            new Mock<IFileStorageService>().Object,
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            Stripper(),
            NullLogger<MediaIngestService>.Instance);

    /// <summary>
    /// The same ingest, but writing to REAL files under the given root.
    /// </summary>
    /// <remarks>
    /// For the tests that follow a file all the way back out again — upload, strip, store, serve.
    /// The mocked-storage version above silently writes nowhere, which is fine for a test about
    /// metadata rows and useless for one about whether the serving route finds anything.
    /// </remarks>
    public static IMediaIngestService IngestToDisk(string root)
    {
        Directory.CreateDirectory(root);

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, Stream content, CancellationToken ct) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var target = File.Create(path);
                await content.CopyToAsync(target, ct);
            });
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<Guid, string>((_, name) => Path.Combine(root, name));

        return new MediaIngestService(
            storage.Object,
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            Stripper(),
            NullLogger<MediaIngestService>.Instance);
    }
}
