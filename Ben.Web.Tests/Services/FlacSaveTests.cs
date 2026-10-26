using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.Entities;
using Ben.Web.Website.Library.Manage.Audio;
using NAudio.Wave;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Clips and edits are saved as FLAC (phase 4b, 10/26/2026). Ben: "I would not recommend a format
/// that loses information." These hold the format to that.
/// </summary>
public class FlacSaveTests
{
    /// <summary>
    /// The whole promise: every sample of the WAV comes back out of the FLAC unchanged. A
    /// noisy tone, because silence would compress to nothing and prove nothing.
    /// </summary>
    [SkippableFact]
    public async Task A_flac_decodes_to_exactly_the_samples_of_the_wav()
    {
        var ffmpeg = Ffmpeg();
        var wav    = NoisyToneWav(seconds: 2, sampleRate: 44_100, channels: 2);

        var flac = await ffmpeg.TryWavToFlacAsync(wav, default);
        Assert.NotNull(flac);
        Assert.True(flac!.AsSpan(0, 4).SequenceEqual("fLaC"u8), "not a FLAC file");

        var back = await ffmpeg.DecodeRangeToWavAsync(new MemoryStream(flac), "audio/flac", "clip.flac", 0, 10, default);

        Assert.Equal(Samples(wav), Samples(back));
        Assert.Equal(Format(wav), Format(back));
    }

    [SkippableFact]
    public async Task A_flac_is_smaller_than_its_wav()
    {
        var wav  = NoisyToneWav(seconds: 2, sampleRate: 44_100, channels: 1);
        var flac = await Ffmpeg().TryWavToFlacAsync(wav, default);

        Assert.True(flac!.Length < wav.Length, $"FLAC {flac.Length} bytes, WAV {wav.Length}");
    }

    /// <summary>A clip of a FLAC clip needs its length from the header before anything is decoded.</summary>
    [SkippableFact]
    public async Task The_length_of_a_flac_is_read_from_its_header()
    {
        var flac = await Ffmpeg().TryWavToFlacAsync(NoisyToneWav(seconds: 3, sampleRate: 22_050, channels: 1), default);

        Assert.Equal(3.0, MediaDuration.Seconds(new MemoryStream(flac!))!.Value, precision: 3);
    }

    [Fact]
    public async Task With_no_ffmpeg_it_stays_wav_and_says_nothing_was_converted()
    {
        var wav = NoisyToneWav(seconds: 1, sampleRate: 8_000, channels: 1);

        var saved = await AudioSave.AsAsync((wav, "audio/wav", ".wav"), AudioSaveFormat.Flac, TestAudio.Encoder(), default);

        Assert.Equal(("audio/wav", ".wav", (long?)null), (saved.ContentType, saved.Extension, saved.WavBytes));
        Assert.Same(wav, saved.Bytes);
    }

    [Fact]
    public async Task Asking_for_wav_never_converts()
    {
        var wav = NoisyToneWav(seconds: 1, sampleRate: 8_000, channels: 1);

        var saved = await AudioSave.AsAsync((wav, "audio/wav", ".wav"), AudioSaveFormat.Wav, TestAudio.MarkingFlac(), default);

        Assert.Equal("audio/wav", saved.ContentType);
    }

    // ── What the page says ────────────────────────────────────────────────────

    [Fact]
    public void A_flac_save_names_both_sizes_and_says_nothing_is_lost()
    {
        var said = AudioSaveSummary.For(Record("audio/flac", 4_300_000, wav: 10_276_000), AudioSaveFormat.Flac);

        Assert.Equal("Saved as FLAC, 4.1 MB (as WAV it would be 9.8 MB). Nothing is lost: it plays back exactly the same.", said);
    }

    [Fact]
    public void A_wav_where_flac_was_asked_for_says_why()
    {
        var said = AudioSaveSummary.For(Record("audio/wav", 10_276_000), AudioSaveFormat.Flac);

        Assert.Contains("cannot save FLAC yet", said);
        Assert.StartsWith("Saved as WAV, 9.8 MB.", said);
    }

    [Fact]
    public void A_wav_that_was_asked_for_just_says_so()
        => Assert.Equal("Saved as WAV, 9.8 MB.", AudioSaveSummary.For(Record("audio/wav", 10_276_000), AudioSaveFormat.Wav));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FfmpegAudioDecoder Ffmpeg()
    {
        var ffmpeg = TestAudio.FfmpegOnThisMachine();
        Skip.If(ffmpeg is null, "No ffmpeg on this machine, so FLAC cannot be written. Install ffmpeg or set MediaTools__FfmpegPath.");
        return TestAudio.Ffmpeg(ffmpeg);
    }

    private static UploadFileRecord Record(string contentType, long size, long? wav = null) => new()
    {
        FileName = "clip", StoredFileName = "clip", ContentType = contentType, FileSize = size, WavEquivalentBytes = wav,
    };

    /// <summary>16-bit PCM, the only kind this server writes: a tone with deterministic noise on it.</summary>
    private static byte[] NoisyToneWav(int seconds, int sampleRate, int channels)
    {
        var random = new Random(253);
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 16, channels)))
        {
            for (var i = 0; i < seconds * sampleRate; i++)
            {
                var tone = Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.4;
                for (var c = 0; c < channels; c++)
                    writer.WriteSample((float)(tone + (random.NextDouble() - 0.5) * 0.1));
            }
        }
        return ms.ToArray();
    }

    private static short[] Samples(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var bytes = new byte[reader.Length];
        var read  = reader.Read(bytes, 0, bytes.Length);
        var samples = new short[read / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, read);
        return samples;
    }

    private static (int Rate, int Channels, int Bits) Format(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        return (reader.WaveFormat.SampleRate, reader.WaveFormat.Channels, reader.WaveFormat.BitsPerSample);
    }
}
