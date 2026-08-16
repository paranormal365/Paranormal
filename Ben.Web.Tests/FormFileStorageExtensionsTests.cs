using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Covers the streaming upload path that replaced "buffer the whole IFormFile into a MemoryStream,
/// call ToArray(), write the array".
/// </summary>
/// <remarks>
/// The point of these is byte-for-byte fidelity, not just "a file appeared". A streaming write that
/// starts from the wrong position, stops short, or writes a truncated buffer still produces a file
/// at the right path — so every assertion here compares the stored content to the source content,
/// and the size assertions use a payload larger than the 81,920-byte copy buffer so a
/// single-buffer-only bug cannot pass.
/// </remarks>
public class FormFileStorageExtensionsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ben-upload-test-{Guid.NewGuid()}");
    private readonly LocalFileStorageService _svc;

    public FormFileStorageExtensionsTests()
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

    private static IFormFile FormFile(byte[] content, string fileName = "evidence.bin")
    {
        var stream = new MemoryStream(content, writable: false);
        return new FormFile(stream, 0, content.Length, name: "file", fileName: fileName);
    }

    private async Task<byte[]> ReadStoredAsync(string relativePath)
    {
        await using var stored = await _svc.OpenReadAsync(relativePath);
        using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task WriteFormFileAsync_StoresContentByteForByte()
    {
        var content = new byte[] { 1, 2, 3, 250, 251, 252 };

        await _svc.WriteFormFileAsync("users/a/file.bin", FormFile(content));

        Assert.Equal(content, await ReadStoredAsync("users/a/file.bin"));
    }

    [Fact]
    public async Task WriteFormFileAsync_StoresPayloadLargerThanTheCopyBuffer()
    {
        // 300 KB — several times the 81,920-byte FileStream copy buffer, so a write that only
        // moved the first chunk would fail here while passing the small-payload test above.
        var content = new byte[300 * 1024];
        Random.Shared.NextBytes(content);

        await _svc.WriteFormFileAsync("users/a/large.bin", FormFile(content));

        var stored = await ReadStoredAsync("users/a/large.bin");
        Assert.Equal(content.Length, stored.Length);
        Assert.Equal(content, stored);
    }

    [Fact]
    public async Task WriteFormFileAsync_ReadsFromTheStartOfTheFormFile()
    {
        // FormFile sits at an offset inside a larger multipart body. Writing from the raw stream's
        // current position rather than the file's own offset would store the wrong bytes.
        var body = new byte[] { 9, 9, 9, 42, 43, 44 };
        var stream = new MemoryStream(body, writable: false);
        var file = new FormFile(stream, baseStreamOffset: 3, length: 3, name: "file", fileName: "offset.bin");

        await _svc.WriteFormFileAsync("users/a/offset.bin", file);

        Assert.Equal(new byte[] { 42, 43, 44 }, await ReadStoredAsync("users/a/offset.bin"));
    }

    [Fact]
    public async Task WriteFormFileAsync_OverwritesAnExistingFileCompletely()
    {
        // The replace flow overwrites each case copy at its existing path. A write that did not
        // truncate would leave the tail of the longer previous file behind.
        await _svc.WriteFormFileAsync("users/a/replace.bin", FormFile(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 }));
        await _svc.WriteFormFileAsync("users/a/replace.bin", FormFile(new byte[] { 7, 7 }));

        Assert.Equal(new byte[] { 7, 7 }, await ReadStoredAsync("users/a/replace.bin"));
    }

    [Fact]
    public async Task WriteBytesAsync_StoresContentByteForByte()
    {
        var content = new byte[] { 10, 20, 30 };

        await _svc.WriteBytesAsync("users/a/sanitized.svg", content);

        Assert.Equal(content, await ReadStoredAsync("users/a/sanitized.svg"));
    }
}
