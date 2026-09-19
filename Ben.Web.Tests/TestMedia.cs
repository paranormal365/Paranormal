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
        => new MediaIngestService(
            StorageOnDisk(root),
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            Stripper(),
            NullLogger<MediaIngestService>.Instance);

    /// <summary>
    /// Storage over real files that behaves like <c>LocalFileStorageService</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>The paths it hands out are RELATIVE</b> ("users/{id}/file.jpg"), resolved against
    /// <paramref name="root"/> inside every method — exactly as the real one does. That detail is
    /// the whole point of this helper: an earlier version returned absolute paths, so a controller
    /// that passed a storage path straight to the filesystem passed its tests and then served
    /// nothing at all in the running site. A stub that is easier to satisfy than the real thing is
    /// a stub that certifies bugs.</para>
    /// </remarks>
    public static IFileStorageService StorageOnDisk(string root)
    {
        Directory.CreateDirectory(root);
        string Absolute(string relative) => Path.Combine(root, relative);

        var storage = new Mock<IFileStorageService>();

        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<Guid, string>((userId, name) => $"users/{userId}/{name}");
        storage.Setup(s => s.OrgFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<Guid, string>((orgId, name) => $"orgs/{orgId}/{name}");
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<Guid, string>((caseId, name) => $"cases/{caseId}/{name}");

        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (string relative, Stream content, CancellationToken ct) =>
            {
                var path = Absolute(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var target = File.Create(path);
                await content.CopyToAsync(target, ct);
            });

        storage.Setup(s => s.Exists(It.IsAny<string>()))
            .Returns<string>(relative => File.Exists(Absolute(relative)));

        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((relative, _) =>
                Task.FromResult<Stream>(File.OpenRead(Absolute(relative))));

        storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((relative, _) =>
            {
                var path = Absolute(relative);
                if (File.Exists(path)) File.Delete(path);
                return Task.CompletedTask;
            });

        return storage.Object;
    }
}
