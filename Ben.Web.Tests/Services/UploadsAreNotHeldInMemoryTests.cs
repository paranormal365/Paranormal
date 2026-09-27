using System.Diagnostics;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An upload is streamed to storage, never held whole in memory (2026-09-27). Ingest used to copy
/// every upload into a byte array; uploads may be two gigabytes, and a shared host gives the API
/// half of one, so a single video would have taken the process down. Each test here fails against
/// that old code — a MemoryStream cannot even grow past two gigabytes, so the large cases throw.
/// </summary>
public sealed class UploadsAreNotHeldInMemoryTests : IDisposable
{
    /// <summary>Bigger than any byte array or MemoryStream can hold.</summary>
    private const long HugeLength = 2_500_000_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ingest-stream-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_video_bigger_than_memory_could_hold_is_stored_whole()
    {
        long written = 0;
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, Stream content, CancellationToken ct) =>
            {
                var counter = new CountingStream();
                await content.CopyToAsync(counter, ct);
                written += counter.Count;
            });

        var ingest = Ingest(storage.Object);
        var upload = Upload(new ZeroStream(HugeLength), "night-vision.mp4", "video/mp4");

        var result = await ingest.IngestAsync(upload, "cases/x/night-vision.mp4", Guid.NewGuid(), default);

        Assert.Equal(HugeLength, written);
        Assert.Equal(HugeLength, result.ServedFileSize);
        Assert.False(result.WasSanitized);
    }

    [Fact]
    public async Task An_upload_that_cannot_seek_is_still_kept_and_cleaned()
    {
        var photo = Jpeg();
        var ingest = TestMedia.IngestToDisk(_root);
        var upload = Upload(new ForwardOnlyStream(new MemoryStream(photo)), "hallway.jpg", "image/jpeg", photo.Length);

        var result = await ingest.IngestAsync(upload, "users/u/hallway.jpg", Guid.NewGuid(), default);

        Assert.True(result.WasSanitized);
        Assert.Equal(photo, await File.ReadAllBytesAsync(Path.Combine(_root, "users/u/hallway.jpg")));
        Assert.True(File.Exists(Path.Combine(_root, "users/u/hallway.jpg.clean.jpg")));
        Assert.True(File.Exists(Path.Combine(_root, "users/u/hallway.jpg.thumb.jpg")));
    }

    [Fact]
    public async Task A_picture_too_large_to_decode_is_refused_rather_than_read_in()
    {
        var ingest = Ingest(new Mock<IFileStorageService>().Object);
        var upload = Upload(new ZeroStream(ImageBytes.MaxBytes + 1), "huge.jpg", "image/jpeg");

        var refusal = await Assert.ThrowsAsync<UnreadableImageException>(
            () => ingest.IngestAsync(upload, "users/u/huge.jpg", Guid.NewGuid(), default));
        Assert.Contains("too large", refusal.Message);
    }

    [Fact]
    public async Task Asking_a_recording_for_a_thumbnail_does_not_read_the_recording_in()
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.Exists("cases/x/long.mp4")).Returns(true);
        storage.Setup(s => s.OpenReadAsync("cases/x/long.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ZeroStream(HugeLength));

        var thumbnail = await Ingest(storage.Object).OpenThumbnailAsync("cases/x/long.mp4", default);

        Assert.Null(thumbnail);
    }

    [Fact]
    public async Task A_forward_only_picture_over_the_cap_is_refused_as_it_arrives()
    {
        var tooBig = await ImageBytes.ReadAsync(new ForwardOnlyStream(new ZeroStream(ImageBytes.MaxBytes + 1)), default);
        var fits   = await ImageBytes.ReadAsync(new ForwardOnlyStream(new ZeroStream(1024)), default);

        Assert.Null(tooBig);
        Assert.Equal(1024, fits!.Length);
    }

    /// <summary>
    /// The real ffmpeg, where this machine has one: the metadata goes, and the stripped copy's
    /// scratch file goes with the stream that carried it.
    /// </summary>
    [SkippableFact]
    public async Task Stripping_through_ffmpeg_streams_in_and_out_and_cleans_up()
    {
        var ffmpeg = Environment.GetEnvironmentVariable("BEN_FFMPEG") ?? @"C:\tools\ffmpeg\bin\ffmpeg.exe";
        Skip.IfNot(File.Exists(ffmpeg), $"no ffmpeg at {ffmpeg}");

        Directory.CreateDirectory(_root);
        var clip = Path.Combine(_root, "clip.mp4");
        const string Marker = "IsHauntedStripMarker";
        await RunAsync(ffmpeg, "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=64x64:rate=10",
            "-c:v", "mpeg4", "-metadata", $"title={Marker}", clip);
        Assert.Contains(Marker, System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(clip)));

        var stripper = new AvMetadataStripper(
            Options.Create(new MediaToolOptions { FfmpegPath = ffmpeg }), NullLogger<AvMetadataStripper>.Instance);

        string scratch;
        byte[] cleaned;
        await using (var source = File.OpenRead(clip))
        await using (var stripped = await stripper.StripAsync(source, "clip.mp4", default))
        {
            Assert.NotNull(stripped);
            scratch = Assert.IsType<FileStream>(stripped).Name;
            using var copy = new MemoryStream();
            await stripped!.CopyToAsync(copy);
            cleaned = copy.ToArray();
        }

        Assert.NotEmpty(cleaned);
        Assert.DoesNotContain(Marker, System.Text.Encoding.ASCII.GetString(cleaned));
        Assert.False(File.Exists(scratch), "the stripped copy's scratch file outlived its stream");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static MediaIngestService Ingest(IFileStorageService storage) => new(
        storage, new FileMetadataExtractorService(), new MediaSanitizationService(),
        TestMedia.Stripper(), NullLogger<MediaIngestService>.Instance);

    private static IFormFile Upload(Stream content, string name, string contentType, long? length = null)
    {
        var file = new Mock<IFormFile>();
        file.Setup(f => f.OpenReadStream()).Returns(content);
        file.Setup(f => f.FileName).Returns(name);
        file.Setup(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.Length).Returns(length ?? content.Length);
        return file.Object;
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(32, 24);
        bitmap.Erase(SKColors.DarkSlateGray);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static async Task RunAsync(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stderr);
    }

    /// <summary>A seekable stream of zeros of any length, costing no memory.</summary>
    private sealed class ZeroStream(long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, Math.Max(0, length - Position));
            Array.Clear(buffer, offset, n);
            Position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => length + offset,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Hides seeking, as a raw request body does.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Counts what is written to it and keeps none of it.</summary>
    private sealed class CountingStream : Stream
    {
        public long Count { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Count += count;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
