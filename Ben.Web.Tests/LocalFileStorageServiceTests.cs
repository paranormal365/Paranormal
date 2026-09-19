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

    // ── CaseFilePath ──────────────────────────────────────────────────────────

    [Fact]
    public void CaseFilePath_ReturnsExpectedFormat()
    {
        var caseId = Guid.NewGuid();
        var path = _svc.CaseFilePath(caseId, "evidence.jpg");
        Assert.Equal($"cases/{caseId}/evidence.jpg", path);
    }

    [Fact]
    public void CaseFilePath_UsesForwardSlashes()
    {
        var path = _svc.CaseFilePath(Guid.NewGuid(), "clip.mp3");
        Assert.DoesNotContain('\\', path);
    }

    [Fact]
    public async Task CaseFilePath_WrittenFileCanBeRead()
    {
        var caseId = Guid.NewGuid();
        var path   = _svc.CaseFilePath(caseId, "test.bin");
        var data   = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await _svc.WriteAsync(path, new MemoryStream(data));

        await using var stream = await _svc.OpenReadAsync(path);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(data, ms.ToArray());
    }

    // ── A write is all there or not there ─────────────────────────────────────

    /// <summary>A stream that hands over some bytes and then fails, as a request cut off mid-upload does.</summary>
    private sealed class FailsPartWay(byte[] first) : MemoryStream(first)
    {
        private bool _handedOver;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_handedOver) throw new OperationCanceledException("the reader went away");
            _handedOver = true;
            return await base.ReadAsync(buffer, ct);
        }
    }

    [Fact]
    public async Task A_write_cut_short_leaves_the_old_file_whole_and_nothing_half_written()
    {
        var path = _svc.OrgFilePath(Guid.NewGuid(), "photo.jpg.thumb.jpg");
        var whole = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _svc.WriteAsync(path, new MemoryStream(whole));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _svc.WriteAsync(path, new FailsPartWay(new byte[] { 9, 9, 9 })));

        await using (var stream = await _svc.OpenReadAsync(path))
        using (var read = new MemoryStream())
        {
            await stream.CopyToAsync(read);
            Assert.Equal(whole, read.ToArray());
        }

        var directory = path[..path.LastIndexOf('/')];
        Assert.Equal([path], _svc.ListFiles(directory));
        Assert.Single(Directory.GetFiles(Path.Combine(_tempRoot, directory.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task A_write_cut_short_on_a_new_path_leaves_no_file_at_all()
    {
        var path = _svc.OrgFilePath(Guid.NewGuid(), "new.jpg.thumb.jpg");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _svc.WriteAsync(path, new FailsPartWay(new byte[] { 9, 9, 9 })));

        Assert.False(_svc.Exists(path));
    }

    [Fact]
    public async Task A_file_open_for_reading_can_still_be_replaced()
    {
        var path = _svc.OrgFilePath(Guid.NewGuid(), "busy.jpg");
        await _svc.WriteAsync(path, new MemoryStream(new byte[] { 1 }));

        await using var reader = await _svc.OpenReadAsync(path);
        await _svc.WriteAsync(path, new MemoryStream(new byte[] { 2, 2 }));

        await using var fresh = await _svc.OpenReadAsync(path);
        Assert.Equal(2, fresh.Length);
    }
}
