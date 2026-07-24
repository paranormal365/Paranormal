using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Unit tests for LocalFileStorageService.
/// Each test gets its own isolated temp directory that is cleaned up afterwards.
/// </summary>
public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ben-fs-test-{Guid.NewGuid()}");
    private readonly LocalFileStorageService _svc;

    public LocalFileStorageServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = _tempRoot
            })
            .Build();
        _svc = new LocalFileStorageService(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── UserFilePath ──────────────────────────────────────────────────────────

    [Fact]
    public void UserFilePath_ReturnsExpectedFormat()
    {
        var userId = Guid.NewGuid();
        var path = _svc.UserFilePath(userId, "abc.mp3");
        Assert.Equal($"users/{userId}/abc.mp3", path);
    }

    [Fact]
    public void UserFilePath_UsesForwardSlashes()
    {
        var path = _svc.UserFilePath(Guid.NewGuid(), "file.wav");
        Assert.DoesNotContain('\\', path);
    }

    // ── WriteAsync / Exists ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesFileOnDisk()
    {
        var relativePath = _svc.UserFilePath(Guid.NewGuid(), "test.txt");
        var bytes = "hello world"u8.ToArray();

        await _svc.WriteAsync(relativePath, new MemoryStream(bytes));

        Assert.True(_svc.Exists(relativePath));
    }

    [Fact]
    public async Task WriteAsync_CreatesIntermediateDirectories()
    {
        var relativePath = $"users/{Guid.NewGuid()}/deep/path/file.bin";
        await _svc.WriteAsync(relativePath, new MemoryStream([1, 2, 3]));

        Assert.True(_svc.Exists(relativePath));
    }

    // ── OpenReadAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenReadAsync_ReturnsCorrectBytes()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };
        var path  = _svc.UserFilePath(Guid.NewGuid(), "data.bin");

        await _svc.WriteAsync(path, new MemoryStream(bytes));

        await using var stream = await _svc.OpenReadAsync(path);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task OpenReadAsync_ThrowsWhenFileDoesNotExist()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _svc.OpenReadAsync("users/missing/file.mp3"));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var path = _svc.UserFilePath(Guid.NewGuid(), "remove-me.wav");
        await _svc.WriteAsync(path, new MemoryStream([9, 8, 7]));
        Assert.True(_svc.Exists(path));

        await _svc.DeleteAsync(path);

        Assert.False(_svc.Exists(path));
    }

    [Fact]
    public async Task DeleteAsync_IsNoOpWhenFileAbsent()
    {
        // Should not throw
        await _svc.DeleteAsync("users/nobody/ghost.mp3");
    }

    // ── Exists ────────────────────────────────────────────────────────────────

    [Fact]
    public void Exists_ReturnsFalseForMissingFile()
    {
        Assert.False(_svc.Exists(_svc.UserFilePath(Guid.NewGuid(), "nope.ogg")));
    }
}
