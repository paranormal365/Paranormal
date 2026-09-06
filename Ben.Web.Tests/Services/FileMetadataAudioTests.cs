using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using NAudio.Wave;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Measuring an uploaded recording — the step that decides whether anything downstream knows how
/// long it is.
/// </summary>
/// <remarks>
/// <para>The extractor constructed <c>Mp3FileReader</c> directly, which defaults to the ACM codec:
/// <c>Msacm32.dll</c>, a Windows system library. Off Windows every MP3 threw
/// <see cref="DllNotFoundException"/>, a bare <c>catch</c> swallowed it, and the file was recorded
/// with no duration, no sample rate and no channel count. Silently — an MP3 nobody had measured
/// looked exactly like one that could not be measured.</para>
///
/// <para>The site runs on Linux, so that was every MP3 anybody had ever uploaded, and it is why the
/// case mixer had no lengths to draw its clips with. Nothing caught it because no unit test ever
/// decoded an MP3 (2026-09-06 audio audit, phase 4).</para>
/// </remarks>
public sealed class FileMetadataAudioTests
{
    private static readonly string Mp3Path =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    private static UploadFileMetadata Extract(byte[] bytes, string contentType)
        => new FileMetadataExtractorService().Extract(Guid.NewGuid(), contentType, bytes);

    /// <summary>The one that was broken, on the one platform this site runs on.</summary>
    [Fact]
    public void An_mp3_is_measured()
    {
        Assert.True(File.Exists(Mp3Path), $"the MP3 fixture is missing at {Mp3Path}");

        var meta = Extract(File.ReadAllBytes(Mp3Path), "audio/mpeg");

        Assert.Equal("Audio", meta.MediaKind);
        Assert.NotNull(meta.DurationSeconds);
        Assert.True(meta.DurationSeconds > 1,
            $"an MP3 that decodes should be longer than a second; got {meta.DurationSeconds}");
        Assert.NotNull(meta.SampleRateHz);
        Assert.NotNull(meta.Channels);
    }

    [Fact]
    public void A_wav_is_measured()
    {
        var meta = Extract(SilentWav(seconds: 3, sampleRate: 8000, channels: 1), "audio/wav");

        Assert.Equal("Audio", meta.MediaKind);
        Assert.Equal(3.0, meta.DurationSeconds!.Value, 1);
        Assert.Equal(8000, meta.SampleRateHz);
        Assert.Equal(1, meta.Channels);
    }

    [Fact]
    public void A_stereo_wav_reports_two_channels()
    {
        var meta = Extract(SilentWav(seconds: 1, sampleRate: 44100, channels: 2), "audio/wav");

        Assert.Equal(2, meta.Channels);
        Assert.Equal(44100, meta.SampleRateHz);
    }

    /// <summary>
    /// Something that is not audio at all leaves no measurement, and does not throw.
    /// </summary>
    [Fact]
    public void Bytes_that_are_not_audio_are_left_unmeasured()
    {
        var meta = Extract([1, 2, 3, 4, 5, 6, 7, 8], "audio/wav");

        Assert.Null(meta.DurationSeconds);
    }

    private static byte[] SilentWav(int seconds, int sampleRate, int channels)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 16, channels)))
        {
            var frame = new short[channels];
            for (var i = 0; i < seconds * sampleRate; i++) writer.WriteSamples(frame, 0, channels);
        }
        return ms.ToArray();
    }
}
