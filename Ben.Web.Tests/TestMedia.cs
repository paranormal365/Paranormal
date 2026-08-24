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
}
