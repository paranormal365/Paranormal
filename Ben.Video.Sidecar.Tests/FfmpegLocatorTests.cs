using System.Security.Cryptography;
using System.Text.Json;
using Ben.Video.Sidecar.Jobs;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 158 — <see cref="FfmpegLocator"/> grew from one binary to two. The asymmetry is
/// the point and is what these tests lock in: ffmpeg keeps its original fail-CLOSED integrity
/// contract (an unverified ffmpeg means no job endpoints at all), while ffprobe fails SOFT (an
/// unverified or absent ffprobe only withholds the probe/thumbnail capabilities, leaving the
/// sidecar fully usable for everything it could do before this phase).
/// </summary>
public sealed class FfmpegLocatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("benvideo-locator-test-").FullName;

    private string Rid => new FfmpegLocator(_dir).Rid;

    private string WriteBinary(string name, string content)
    {
        var dir = Path.Combine(_dir, "ffmpeg", Rid);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private void WriteManifest(string? ffmpegSha, string? ffprobeSha)
    {
        var entry = new Dictionary<string, object?>
        {
            ["url"] = "https://example.invalid/ffmpeg.zip",
            ["sha256"] = ffmpegSha ?? "",
            ["version"] = "test",
        };
        if (ffprobeSha is not null) entry["ffprobeSha256"] = ffprobeSha;

        // The REAL manifest carries a top-level "_comment" string beside the per-RID objects —
        // included here so these tests parse the same shape the shipped file has. See
        // Manifest_WithNonObjectSiblingKeys_StillVerifies for why that detail matters.
        File.WriteAllText(
            Path.Combine(_dir, "ffmpeg-manifest.json"),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["_comment"] = "supply-chain manifest",
                ["_schema"] = "hashes are of the extracted binaries",
                [Rid] = entry,
            }));
    }

    private static string ExeName(string baseName) =>
        OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;

    [Fact]
    public void Ffmpeg_MatchingHash_VerifiesOk()
    {
        var path = WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        WriteManifest(Sha256Of(path), ffprobeSha: null);

        var locator = new FfmpegLocator(_dir);

        Assert.True(locator.VerifyIntegrity());
        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffmpeg));
    }

    [Fact]
    public void Ffmpeg_MismatchedHash_FailsClosed()
    {
        WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        WriteManifest(new string('a', 64), ffprobeSha: null);

        Assert.False(new FfmpegLocator(_dir).VerifyIntegrity());
    }

    [Fact]
    public void Ffprobe_Missing_FailsSoft_WithoutAffectingFfmpeg()
    {
        // The realistic pre-158 bundle: a verified ffmpeg, no ffprobe on disk at all.
        var path = WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        WriteManifest(Sha256Of(path), ffprobeSha: null);

        var locator = new FfmpegLocator(_dir);

        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffmpeg));
        Assert.False(locator.VerifyIntegrity(FfmpegTool.Ffprobe));
    }

    [Fact]
    public void Ffprobe_PresentButNoManifestHash_IsNotTrusted()
    {
        // An old manifest (no ffprobeSha256) plus an ffprobe that appeared from somewhere must not
        // be trusted — an unpinned binary is exactly what threat T7's hash pinning exists to stop.
        var ffmpeg = WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        WriteBinary(ExeName("ffprobe"), "pretend-ffprobe");
        WriteManifest(Sha256Of(ffmpeg), ffprobeSha: null);

        Assert.False(new FfmpegLocator(_dir).VerifyIntegrity(FfmpegTool.Ffprobe));
    }

    [Fact]
    public void Ffprobe_MatchingHash_VerifiesOk()
    {
        var ffmpeg  = WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        var ffprobe = WriteBinary(ExeName("ffprobe"), "pretend-ffprobe");
        WriteManifest(Sha256Of(ffmpeg), Sha256Of(ffprobe));

        var locator = new FfmpegLocator(_dir);

        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffmpeg));
        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffprobe));
    }

    [Fact]
    public void Ffprobe_MismatchedHash_IsNotTrusted()
    {
        var ffmpeg = WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        WriteBinary(ExeName("ffprobe"), "pretend-ffprobe");
        WriteManifest(Sha256Of(ffmpeg), new string('b', 64));

        Assert.False(new FfmpegLocator(_dir).VerifyIntegrity(FfmpegTool.Ffprobe));
    }

    [Fact]
    public void DevelopmentOverrides_SkipHashVerification_Independently()
    {
        // Overrides point at a developer's own build; there is no manifest entry to check them
        // against. Each tool's override is independent — overriding ffprobe alone must not make
        // an unverified bundled ffmpeg suddenly pass.
        var probeOverride = Path.Combine(_dir, "my-ffprobe");
        File.WriteAllText(probeOverride, "local build");

        var locator = new FfmpegLocator(_dir, developmentPathOverride: null, ffprobeDevelopmentPathOverride: probeOverride);

        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffprobe));
        Assert.False(locator.VerifyIntegrity(FfmpegTool.Ffmpeg)); // no binary, no manifest
    }

    /// <summary>
    /// Item #70 phase 174 — regression for a bug that only appeared the first time a real binary
    /// was bundled. The manifest was deserialized whole into
    /// <c>Dictionary&lt;string, ManifestEntry&gt;</c>, so the top-level <c>"_comment"</c> STRING
    /// that has always been in the shipped file threw a JsonException, the blanket catch turned it
    /// into <c>false</c>, and the sidecar reported an unverified binary whose SHA-256 matched
    /// perfectly. Every earlier test passed because it wrote its own comment-free manifest, and
    /// every earlier live run used a dev override — which returns before the file is ever read.
    ///
    /// <para>Explicitly re-asserted here (rather than relying on <see cref="WriteManifest"/> now
    /// emitting a comment) so that deleting the comment from the helper can't quietly retire the
    /// only coverage of this.</para>
    /// </summary>
    [Fact]
    public void Manifest_WithNonObjectSiblingKeys_StillVerifies()
    {
        var ffmpeg  = WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        var ffprobe = WriteBinary(ExeName("ffprobe"), "pretend-ffprobe");

        File.WriteAllText(Path.Combine(_dir, "ffmpeg-manifest.json"), $$"""
        {
          "_comment": "a plain string sitting beside the RID objects",
          "_schema": "another one",
          "{{Rid}}": {
            "url": "https://example.invalid/ffmpeg.zip",
            "archiveSha256": "{{new string('c', 64)}}",
            "sha256": "{{Sha256Of(ffmpeg)}}",
            "ffprobeSha256": "{{Sha256Of(ffprobe)}}",
            "version": "test"
          }
        }
        """);

        var locator = new FfmpegLocator(_dir);

        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffmpeg));
        Assert.True(locator.VerifyIntegrity(FfmpegTool.Ffprobe));
    }

    /// <summary>A RID key whose value isn't an object must fail closed rather than throw — the
    /// same defensive shape as a missing entry.</summary>
    [Fact]
    public void Manifest_WithMalformedEntryForThisRid_FailsClosed()
    {
        WriteBinary(ExeName("ffmpeg"), "pretend-ffmpeg");
        File.WriteAllText(
            Path.Combine(_dir, "ffmpeg-manifest.json"),
            $$"""{ "_comment": "x", "{{Rid}}": "not-an-object" }""");

        Assert.False(new FfmpegLocator(_dir).VerifyIntegrity());
    }

    [Fact]
    public void PathFor_ResolvesBothToolsUnderTheRidDirectory()
    {
        var locator = new FfmpegLocator(_dir);

        Assert.Equal(locator.ExecutablePath, locator.PathFor(FfmpegTool.Ffmpeg));
        Assert.EndsWith(ExeName("ffprobe"), locator.PathFor(FfmpegTool.Ffprobe));
        Assert.Contains(Path.Combine("ffmpeg", locator.Rid), locator.PathFor(FfmpegTool.Ffprobe));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }
}
