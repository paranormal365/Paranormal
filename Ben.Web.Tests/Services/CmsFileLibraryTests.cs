using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Unit tests for the CMS file library methods in BenAdminClientAdapter.
/// Verifies correct delegation to IWebApiClient and correct multipart form construction.
/// </summary>
public class CmsFileLibraryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IWebApiClient> ApiMock() => new Mock<IWebApiClient>();
    private static Mock<IWebApiAuthService> AuthMock() => new Mock<IWebApiAuthService>();

    private static BenAdminClientAdapter Build(
        Mock<IWebApiClient> api, Mock<IWebApiAuthService>? auth = null)
        => new BenAdminClientAdapter(api.Object, (auth ?? AuthMock()).Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Web.Services.WebApi.WebApiOptions()));

    // ── GetOrgSharedFilesAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetOrgSharedFilesAsync_DelegatesToApiAndReturnsFiles()
    {
        var orgId   = Guid.NewGuid();
        var apiMock = ApiMock();
        var files   = new List<UploadFileRecord>
        {
            new() { FileName = "logo.png", StoredFileName = "s.png", ContentType = "image/png" }
        };

        apiMock.Setup(x => x.GetOrgSharedFilesAsync(orgId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(files);

        var adapter = Build(apiMock);
        var result  = await adapter.GetOrgSharedFilesAsync(orgId);

        Assert.Single(result);
        Assert.Equal("logo.png", result[0].FileName);
        apiMock.Verify(x => x.GetOrgSharedFilesAsync(orgId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrgSharedFilesAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var apiMock = ApiMock();
        apiMock.Setup(x => x.GetOrgSharedFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<UploadFileRecord>)null!);

        var adapter = Build(apiMock);
        var result  = await adapter.GetOrgSharedFilesAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    // ── GetFileDataAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetFileDataAsync_ReturnsBytesAndContentType()
    {
        var fileId  = Guid.NewGuid();
        var bytes   = new byte[] { 1, 2, 3, 4 };
        var apiMock = ApiMock();

        apiMock.Setup(x => x.DownloadFileAsync(fileId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((bytes, "image/png", "logo.png"));

        var adapter = Build(apiMock);
        var result  = await adapter.GetFileDataAsync(fileId);

        Assert.NotNull(result);
        Assert.Equal(bytes, result.Value.Data);
        Assert.Equal("image/png", result.Value.ContentType);
    }

    [Fact]
    public async Task GetFileDataAsync_WhenApiReturnsNull_ReturnsNull()
    {
        var apiMock = ApiMock();
        apiMock.Setup(x => x.DownloadFileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(((byte[], string, string)?)null);

        var adapter = Build(apiMock);
        var result  = await adapter.GetFileDataAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── GetPublicFileTypesAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetPublicFileTypesAsync_DelegatesToApi()
    {
        var apiMock = ApiMock();
        var types   = new List<UploadFileTypeRecord>
        {
            new() { Name = "Images" }
        };

        apiMock.Setup(x => x.GetUploadFileTypesAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(types);

        var adapter = Build(apiMock);
        var result  = await adapter.GetPublicFileTypesAsync();

        Assert.Single(result);
        Assert.Equal("Images", result[0].Name);
    }

    // ── UploadImageAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UploadImageAsync_PassesCorrectFormFieldsToApi()
    {
        var fileTypeId = Guid.NewGuid();
        var userId     = Guid.NewGuid();
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG magic bytes
        var apiMock    = ApiMock();

        MultipartFormDataContent? captured = null;
        apiMock.Setup(x => x.UploadFileAsync(It.IsAny<MultipartFormDataContent>(), It.IsAny<CancellationToken>()))
               .Callback<MultipartFormDataContent, CancellationToken>((form, _) => captured = form)
               .ReturnsAsync(new UploadFileRecord { FileName = "logo.jpg", StoredFileName = "s.jpg", ContentType = "image/jpeg" });

        var adapter = Build(apiMock);
        var result  = await adapter.UploadImageAsync(fileTypeId, userId, "logo.jpg", "image/jpeg", imageBytes);

        Assert.NotNull(result);
        Assert.Equal("logo.jpg", result.FileName);
        Assert.NotNull(captured);

        // Read all form parts
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? capturedFileName = null;

        foreach (var part in captured)
        {
            var name     = part.Headers.ContentDisposition?.Name?.Trim('"');
            var fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
            if (name is null) continue;

            if (fileName is not null)
            {
                capturedFileName = fileName;
            }
            else
            {
                parts[name] = await part.ReadAsStringAsync();
            }
        }

        Assert.Equal(fileTypeId.ToString(), parts["uploadFileTypeId"]);
        Assert.Equal(userId.ToString(), parts["appUserId"]);
        Assert.Equal("true", parts["isPublic"]);
        Assert.Equal("logo.jpg", capturedFileName);
    }

    [Fact]
    public async Task UploadImageAsync_WhenApiReturnsNull_ReturnsNull()
    {
        var apiMock = ApiMock();
        apiMock.Setup(x => x.UploadFileAsync(It.IsAny<MultipartFormDataContent>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((UploadFileRecord?)null);

        var adapter = Build(apiMock);
        var result  = await adapter.UploadImageAsync(
            Guid.NewGuid(), Guid.NewGuid(), "x.jpg", "image/jpeg", [1, 2, 3]);

        Assert.Null(result);
    }
}
