using Ben.Data.WebApi.Services.Audio;
using NAudio.Wave;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Reading stored audio without holding several copies of it.
/// </summary>
/// <remarks>
/// Every server-side audio feature used to grow a <c>List&lt;float&gt;</c> a chunk at a time and
/// then call <c>ToArray</c> on it. Measured on a 90-minute stereo recording, one Normalize peaked
/// at 8.6 GB and one EVP scan at 5.1 GB, and neither released it (2026-09-06 audio walk, findings
/// 1 and 1b).
/// </remarks>
public sealed class AudioSourceReaderTests : IDisposable
{
    private readonly TimeSpan _originalCeiling = AudioSourceReader.MaximumEditDuration;

    public void Dispose() => AudioSourceReader.MaximumEditDuration = _originalCeiling;

    /// <summary>A tone, at a chosen rate, channel count and length.</summary>
    private static byte[] Wav(double seconds, int sampleRate = 44_100, int channels = 2, double hz = 440)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 16, channels)))
        {
            var frames = (int)(seconds * sampleRate);
            var frame  = new short[channels];

            for (var i = 0; i < frames; i++)
            {
                var value = (short)(Math.Sin(2 * Math.PI * hz * i / sampleRate) * 12_000);
                for (var c = 0; c < channels; c++) frame[c] = value;
                writer.WriteSamples(frame, 0, channels);
            }
        }
        return ms.ToArray();
    }

    // ── Reading the whole thing ───────────────────────────────────────────────

    [Fact]
    public void A_recording_reads_back_at_its_own_rate_and_channel_count()
    {
        var (samples, format) = AudioSourceReader.ReadAll(new MemoryStream(Wav(1.0)), "audio/wav");

        Assert.Equal(44_100, format.SampleRate);
        Assert.Equal(2, format.Channels);
        Assert.Equal(44_100 * 2, samples.Length);
    }

    /// <summary>
    /// The buffer is sized from the header, so a WAV — whose length is exact — is never copied.
    /// A buffer longer than the audio would mean the old grow-and-copy behaviour is back.
    /// </summary>
    [Fact]
    public void A_wav_is_read_into_a_buffer_of_exactly_its_own_size()
    {
        var (samples, _) = AudioSourceReader.ReadAll(new MemoryStream(Wav(2.5, channels: 1)), "audio/wav");

        Assert.Equal((int)(2.5 * 44_100), samples.Length);
    }

    [Fact]
    public void A_recording_longer_than_the_ceiling_is_refused_before_it_is_decoded()
    {
        AudioSourceReader.MaximumEditDuration = TimeSpan.FromSeconds(1);

        var error = Assert.Throws<AudioTooLargeException>(
            () => AudioSourceReader.ReadAll(new MemoryStream(Wav(3.0)), "audio/wav"));

        Assert.Contains("limited to", error.Message);
        // Says what to do about it, not just that it will not.
        Assert.Contains("clip", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The endpoints already turn <see cref="NotSupportedException"/> into a 400, so the refusal
    /// reaches somebody as a sentence rather than as a 500.
    /// </summary>
    [Fact]
    public void The_refusal_is_the_kind_the_endpoints_already_answer_400_for() =>
        Assert.IsAssignableFrom<NotSupportedException>(new AudioTooLargeException("x"));

    [Fact]
    public void A_recording_at_the_ceiling_is_allowed()
    {
        AudioSourceReader.MaximumEditDuration = TimeSpan.FromSeconds(2);

        var (samples, _) = AudioSourceReader.ReadAll(new MemoryStream(Wav(1.5)), "audio/wav");

        Assert.True(samples.Length > 0);
    }

    // ── Reading for the detector ──────────────────────────────────────────────

    [Fact]
    public void The_detector_reads_mono_at_its_own_rate_whatever_the_source_was()
    {
        var (mono, rate) = AudioSourceReader.ReadMonoAt(new MemoryStream(Wav(2.0)), "audio/wav");

        Assert.Equal(AudioSourceReader.DetectionSampleRate, rate);
        Assert.InRange(mono.Length, 2.0 * rate * 0.95, 2.0 * rate * 1.05);
    }

    /// <summary>
    /// The point of the exercise: 16 kHz mono is a fraction of 44.1 kHz stereo, and the ceiling
    /// does not apply here because the recording is never held at its own rate.
    /// </summary>
    [Fact]
    public void The_detector_holds_far_less_than_the_recording_itself()
    {
        var wav = Wav(3.0);

        var (full, _) = AudioSourceReader.ReadAll(new MemoryStream(wav), "audio/wav");
        var (mono, _) = AudioSourceReader.ReadMonoAt(new MemoryStream(wav), "audio/wav");

        Assert.True(mono.Length * 5 < full.Length,
            $"mono was {mono.Length} samples against {full.Length} — expected roughly a fifth");
    }

    [Fact]
    public void A_recording_past_the_edit_ceiling_can_still_be_scanned()
    {
        AudioSourceReader.MaximumEditDuration = TimeSpan.FromSeconds(1);

        var (mono, _) = AudioSourceReader.ReadMonoAt(new MemoryStream(Wav(3.0)), "audio/wav");

        Assert.True(mono.Length > 0, "the scan must not inherit the edit ceiling — long recordings are its whole purpose");
    }

    [Fact]
    public void A_mono_source_is_read_without_a_mixdown()
    {
        var (mono, rate) = AudioSourceReader.ReadMonoAt(
            new MemoryStream(Wav(1.0, sampleRate: 16_000, channels: 1)), "audio/wav");

        Assert.Equal(16_000, rate);
        Assert.InRange(mono.Length, 15_000, 17_000);
    }

    /// <summary>A field recorder can hand back four channels, which NAudio's own helper cannot.</summary>
    [Fact]
    public void More_than_two_channels_are_averaged_rather_than_refused()
    {
        var (mono, _) = AudioSourceReader.ReadMonoAt(
            new MemoryStream(Wav(1.0, channels: 4)), "audio/wav");

        Assert.InRange(mono.Length, AudioSourceReader.DetectionSampleRate * 0.9,
                                    AudioSourceReader.DetectionSampleRate * 1.1);
        Assert.Contains(mono, s => Math.Abs(s) > 0.05f);
    }

    /// <summary>The sound has to survive the mixdown, not just the sample count.</summary>
    [Fact]
    public void The_mixdown_keeps_the_signal()
    {
        var (mono, rate) = AudioSourceReader.ReadMonoAt(new MemoryStream(Wav(1.0, hz: 300)), "audio/wav");

        var crossings = 0;
        for (var i = 1; i < mono.Length; i++)
            if (mono[i - 1] < 0 && mono[i] >= 0) crossings++;

        // A 300 Hz tone crosses zero upward 300 times a second, whatever rate it was read at.
        Assert.InRange(crossings / (mono.Length / (double)rate), 280, 320);
    }

    /// <summary>
    /// Reading is chunked, and this is what says so.
    /// </summary>
    /// <remarks>
    /// <para>The first version of this reader asked the provider for the whole remaining array in
    /// one call. Every stage in front of it sizes its scratch buffer from what it is asked for, so
    /// the mono mixdown allocated an interleaved copy of the entire recording and the resampler did
    /// the same: 5.2 GB of allocation to produce a 329 MB result on a 90-minute file. Chunking the
    /// reads brought that to 674 MB. Nothing about the result changed, which is exactly why this
    /// needs a test — it is invisible from the outside.</para>
    ///
    /// <para>What is measured is the <i>marginal</i> cost of a longer recording, so the fixed
    /// overheads — the source bytes in memory, the decoder's own buffers, one read chunk — cancel
    /// instead of swamping a short fixture. Scratch buffers sized from the whole recording show up
    /// here as a marginal cost several times the audio they return.</para>
    /// </remarks>
    [Fact]
    public void Reading_a_longer_recording_costs_little_more_than_the_audio_it_returns()
    {
        // This thread's allocations, not the process's: the suite runs test classes in parallel,
        // and GC.GetTotalAllocatedBytes counts every one of them. Measured that way the figure
        // was right when run alone and four times too high in a full run — a test that reports
        // whatever else happened to be running.
        static long AllocatedReading(byte[] wav)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            AudioSourceReader.ReadMonoAt(new MemoryStream(wav), "audio/wav");
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var shortWav = Wav(5.0);
        var longWav  = Wav(25.0);

        AllocatedReading(shortWav);   // warm up: JIT and one-off buffers are not the subject

        var marginalAllocation = AllocatedReading(longWav) - AllocatedReading(shortWav);

        // What twenty more seconds unavoidably costs: the source bytes the caller handed over, and
        // the samples handed back.
        var marginalUnavoidable = (longWav.Length - shortWav.Length)
                                + 20.0 * AudioSourceReader.DetectionSampleRate * sizeof(float);

        Assert.True(marginalAllocation < marginalUnavoidable * 4,
            $"twenty more seconds of audio cost {marginalAllocation / 1024} KB to read, against "
            + $"{marginalUnavoidable / 1024:0} KB of source and result — a stage is sizing its "
            + "scratch buffer from the whole recording again");
    }

    // ── Refusals that were already there ──────────────────────────────────────

    [Fact]
    public void An_unsupported_content_type_is_still_refused()
    {
        var error = Assert.Throws<NotSupportedException>(
            () => AudioSourceReader.ReadAll(new MemoryStream(Wav(0.1)), "audio/ogg"));

        Assert.Contains("WAV and MP3", error.Message);
    }
}
