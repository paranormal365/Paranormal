using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Entities;
using Xunit;
using Xunit.Abstractions;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The accuracy gate for EVP detection.
/// </summary>
/// <remarks>
/// <para>These tests build a recording with known contents — room tone, three quiet speech-like
/// utterances at exact offsets, a clap, and a mains hum — and assert what the detector does with
/// it. That is the only way to tune this honestly: a detector judged by looking at a waveform can
/// be talked into anything, and the failure mode that matters (quietly missing real events, or
/// burying them under hundreds of false ones) is invisible without ground truth.</para>
///
/// <para>The synthetic "speech" is a three-formant tone with a syllabic envelope. It is not real
/// speech, and passing here is not proof the detector works on tape from a real investigation —
/// it proves the detector responds to voice-band energy above the floor and ignores the two
/// specific things that most often flood this kind of detector (wideband transients and steady
/// out-of-band tones).</para>
/// </remarks>
public class EvpDetectorTests(ITestOutputHelper output)
{
    private const int SampleRate = 16000;

    // ── Signal construction ───────────────────────────────────────────────────

    /// <summary>Deterministic noise: a fixed seed keeps every run of these tests identical.</summary>
    private static void AddRoomTone(float[] buffer, double amplitude, int seed = 12345)
    {
        var rng = new Random(seed);
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] += (float)((rng.NextDouble() * 2 - 1) * amplitude);
    }

    /// <summary>
    /// A voice-like utterance: three formants in the telephone band under a syllabic (~4 Hz)
    /// envelope, with a short attack and decay so it doesn't start like a click.
    /// </summary>
    private static void AddUtterance(
        float[] buffer, double startSeconds, double durationSeconds, double amplitude)
    {
        var start = (int)(startSeconds * SampleRate);
        var count = (int)(durationSeconds * SampleRate);
        double[] formants = [500, 1500, 2500];

        for (var i = 0; i < count && start + i < buffer.Length; i++)
        {
            var t = i / (double)SampleRate;
            var syllable = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 4.0 * t);      // 4 syllables/sec
            var fade = Math.Min(1.0, Math.Min(i, count - i) / (0.02 * SampleRate));

            double sample = 0;
            foreach (var f in formants) sample += Math.Sin(2 * Math.PI * f * t);
            buffer[start + i] += (float)(sample / formants.Length * amplitude * syllable * fade);
        }
    }

    /// <summary>A wideband transient — the classic false positive an energy detector should demote.</summary>
    private static void AddClap(float[] buffer, double atSeconds, double amplitude, int seed = 999)
    {
        var start = (int)(atSeconds * SampleRate);
        var count = (int)(0.01 * SampleRate);          // 10 ms
        var rng = new Random(seed);
        for (var i = 0; i < count && start + i < buffer.Length; i++)
        {
            var decay = 1.0 - i / (double)count;
            buffer[start + i] += (float)((rng.NextDouble() * 2 - 1) * amplitude * decay * decay);
        }
    }

    /// <summary>Steady 60 Hz mains hum — below the voice band, and continuous, so it must not gate.</summary>
    private static void AddHum(float[] buffer, double startSeconds, double durationSeconds, double amplitude)
    {
        var start = (int)(startSeconds * SampleRate);
        var count = (int)(durationSeconds * SampleRate);
        for (var i = 0; i < count && start + i < buffer.Length; i++)
        {
            var t = i / (double)SampleRate;
            buffer[start + i] += (float)(Math.Sin(2 * Math.PI * 60.0 * t) * amplitude);
        }
    }

    /// <summary>
    /// The standard fixture: 30 s of room tone with utterances at 5.0, 13.0 and 22.0 s, a clap at
    /// 9.0 s, and hum running through 16–20 s.
    /// </summary>
    private static float[] BuildFixture()
    {
        var buffer = new float[30 * SampleRate];
        AddRoomTone(buffer, 0.004);                       // ≈ -48 dBFS
        AddUtterance(buffer, 5.0,  0.8, 0.030);           // quiet, but clearly above the tone
        AddClap(buffer, 9.0, 0.500);                      // loud and wideband
        AddUtterance(buffer, 13.0, 1.2, 0.025);
        AddHum(buffer, 16.0, 4.0, 0.060);                 // loud but out of band
        AddUtterance(buffer, 22.0, 0.6, 0.020);           // the faintest of the three
        return buffer;
    }

    private static readonly double[] UtteranceStarts = [5.0, 13.0, 22.0];

    /// <summary>
    /// Mirrors the detector's context padding. Candidates deliberately extend either side of the
    /// detected energy so playback has room, so accuracy is measured against the detected edges
    /// rather than the reported ones.
    /// </summary>
    private const double ContextPad = 0.40;

    private static double DetectedStart(EvpCandidate c) => c.StartSeconds + ContextPad;
    private static double DetectedEnd(EvpCandidate c)   => c.EndSeconds   - ContextPad;

    private static EvpCandidate? Nearest(IReadOnlyList<EvpCandidate> found, double seconds) =>
        found.Count == 0 ? null
            : found.OrderBy(c => Math.Abs(c.StartSeconds - seconds)).First();

    private static bool Covers(EvpCandidate c, double seconds) =>
        c.StartSeconds <= seconds && c.EndSeconds >= seconds;

    private void Dump(string label, IReadOnlyList<EvpCandidate> found)
    {
        output.WriteLine($"{label}: {found.Count} candidate(s)");
        foreach (var c in found)
            output.WriteLine($"   {c.StartSeconds,6:0.00}–{c.EndSeconds,6:0.00}s  ({c.DurationSeconds:0.00}s)  score {c.Score,5:0.0}");
    }

    // ── Detection accuracy ────────────────────────────────────────────────────

    [Fact]
    public void FindsEveryUtterance_WithinATenthOfASecond()
    {
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);
        Dump("Medium", found);

        foreach (var expected in UtteranceStarts)
        {
            var nearest = Nearest(found, expected);
            Assert.True(nearest is not null, $"nothing detected anywhere near {expected}s");
            Assert.True(Math.Abs(DetectedStart(nearest!.Value) - expected) <= 0.1,
                $"utterance at {expected}s was detected at {DetectedStart(nearest.Value):0.000}s");
        }
    }

    [Fact]
    public void PadsEachCandidateWithContextEitherSide()
    {
        // Trimmed exactly to the gate, a candidate plays back as a fragment starting mid-sound.
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);

        foreach (var expected in UtteranceStarts)
        {
            var match = found.Single(c => Covers(c, expected + 0.05));
            Assert.True(match.StartSeconds < expected,
                $"candidate starts at {match.StartSeconds:0.00}s, on top of the event at {expected}s");
            Assert.True(expected - match.StartSeconds >= ContextPad - 0.02,
                $"only {expected - match.StartSeconds:0.00}s of lead-in");
        }
    }

    [Fact]
    public void ClampsContextPaddingToTheRecording()
    {
        // An event in the first moments must not pad to a negative start, and one at the very end
        // must not run past the file — both would be bounds no player can seek to.
        var buffer = new float[3 * SampleRate];
        AddRoomTone(buffer, 0.002);
        AddUtterance(buffer, 0.05, 0.4, 0.050);
        AddUtterance(buffer, 2.40, 0.5, 0.050);

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("edges", found);

        Assert.NotEmpty(found);
        Assert.All(found, c =>
        {
            Assert.True(c.StartSeconds >= 0, $"start {c.StartSeconds}");
            Assert.True(c.EndSeconds <= 3.0, $"end {c.EndSeconds} past a 3s file");
        });
    }

    [Fact]
    public void EachUtteranceIsOneCandidate_NotFragmentedBySyllableGaps()
    {
        // The envelope dips to silence four times a second. Without gap merging each utterance
        // would arrive as a handful of 60 ms slivers, which is unreviewable.
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);

        foreach (var expected in UtteranceStarts)
            Assert.Single(found, c => Covers(c, expected + 0.05));
    }

    [Fact]
    public void CoversTheWholeUtterance_NotJustItsOnset()
    {
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);

        // The 13.0s utterance runs 1.2s; the *detected* portion should span most of it so listening
        // back plays the phrase rather than its first syllable. Measured inside the padding, or the
        // context alone would satisfy this.
        var match = found.Single(c => Covers(c, 13.05));
        var detected = DetectedEnd(match) - DetectedStart(match);
        Assert.True(detected >= 0.9, $"only detected {detected:0.00}s of a 1.2s utterance");
    }

    [Fact]
    public void DoesNotFloodOnASteadyHum()
    {
        // 60 Hz at more than twice the utterances' amplitude. It is far louder than anything else
        // in the fixture, and it must produce nothing: it's below the band, and it's continuous, so
        // the adaptive floor rises to meet it.
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);

        var duringHum = found.Where(c => c.StartSeconds >= 16.2 && c.EndSeconds <= 19.8).ToList();
        Dump("during hum", duringHum);
        Assert.Empty(duringHum);
    }

    [Fact]
    public void DiscardsAClapAsTooShortToBeAnUtterance()
    {
        // The 10 ms clap never reaches scoring — it's dropped by the minimum-length rule. Asserted
        // explicitly so this stays a deliberate outcome rather than a silent side effect.
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);
        Assert.DoesNotContain(found, c => Covers(c, 9.0));
    }

    [Fact]
    public void RanksALoudDoorSlamBelowAQuietUtterance()
    {
        // The real test of band-ratio scoring, which the 10 ms clap above never reaches. A door
        // slam is wideband and long enough to survive every length rule, and it is 10x the
        // amplitude of the utterance — so a detector scoring on loudness alone ranks it top. It
        // should lose because most of its energy lives outside the voice band.
        var buffer = new float[20 * SampleRate];
        AddRoomTone(buffer, 0.004);
        AddUtterance(buffer, 5.0, 0.8, 0.030);

        var rng = new Random(8080);
        var start = (int)(12.0 * SampleRate);
        var count = (int)(0.30 * SampleRate);
        for (var i = 0; i < count; i++)
        {
            var decay = Math.Pow(1.0 - i / (double)count, 3);        // thump, not a tone
            var lowFreq = Math.Sin(2 * Math.PI * 80 * i / SampleRate);
            buffer[start + i] += (float)((lowFreq * 0.7 + (rng.NextDouble() * 2 - 1) * 0.3) * 0.30 * decay);
        }

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("door slam vs utterance", found);

        var utterance = found.Single(c => Covers(c, 5.05));
        var slam = found.FirstOrDefault(c => Covers(c, 12.05));
        Assert.True(slam != default, "the slam should still be proposed — it is a real event");
        Assert.True(utterance.Score > slam.Score,
            $"slam scored {slam.Score:0.0} but the quiet utterance only {utterance.Score:0.0}");
    }

    [Fact]
    public void KeepsTheQueueShortOnCleanAudio()
    {
        // Three real events in 30 seconds. Anything above a handful means the floor tracking is
        // wrong and every review queue would be mostly noise.
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.Medium, 500);
        Assert.InRange(found.Count, 3, 6);
    }

    // ── Sensitivity ───────────────────────────────────────────────────────────

    [Fact]
    public void SensitivityOrdersTheQueueLength()
    {
        var fixture = BuildFixture();
        var low    = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.Low,    500);
        var medium = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.Medium, 500);
        var high   = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.High,   500);

        output.WriteLine($"low={low.Count} medium={medium.Count} high={high.Count}");
        Assert.True(low.Count <= medium.Count, "Low proposed more than Medium");
        Assert.True(medium.Count <= high.Count, "Medium proposed more than High");
    }

    [Fact]
    public void HighSensitivityStillFindsEveryUtterance()
    {
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.High, 500);
        foreach (var expected in UtteranceStarts)
            Assert.Contains(found, c => Covers(c, expected + 0.05));
    }

    // ── Quiet-room behaviour ──────────────────────────────────────────────────

    [Fact]
    public void FindsNothingInRoomToneAlone()
    {
        var buffer = new float[30 * SampleRate];
        AddRoomTone(buffer, 0.004);

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("room tone only", found);
        Assert.Empty(found);
    }

    [Fact]
    public void FindsNothingInDigitalSilence()
    {
        var found = EvpDetector.Detect(new float[10 * SampleRate], SampleRate, EvpSensitivity.Medium, 500);
        Assert.Empty(found);
    }

    [Fact]
    public void FindsAQuietUtteranceUnderALoudFloor()
    {
        // The point of an adaptive floor: the same utterance, 20 dB of extra hiss. A fixed
        // threshold tuned on the quiet fixture would miss this entirely.
        var buffer = new float[20 * SampleRate];
        AddRoomTone(buffer, 0.040);                       // ≈ 20 dB louder than the standard fixture
        AddUtterance(buffer, 10.0, 0.8, 0.300);           // scaled up to match

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("loud room", found);
        Assert.Contains(found, c => Covers(c, 10.05));
    }

    // ── Hard cases ────────────────────────────────────────────────────────────
    // The fixture above is clean. These are the situations that actually decide whether the
    // detector earns its place, and each one is here because a plausible implementation fails it.

    [Fact]
    public void FindsAnUtteranceOnlyAFewDecibelsAboveTheFloor()
    {
        // The characteristic EVP: barely there. At roughly 8 dB over the room tone it is quiet
        // enough that a detector keyed to obvious events misses it entirely.
        var buffer = new float[20 * SampleRate];
        AddRoomTone(buffer, 0.010);
        AddUtterance(buffer, 10.0, 0.7, 0.025);

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("faint utterance", found);
        Assert.Contains(found, c => Covers(c, 10.05));
    }

    [Fact]
    public void DuringContinuousSpeech_ReportsBoundedStretches_NotOneBlob()
    {
        // KNOWN LIMITATION, pinned deliberately. A quiet event happening *underneath* louder
        // speech in the same frequency band cannot be separated out by an energy detector — the
        // loud signal masks it. What the detector can promise is that it still flags the stretch
        // and does not hand back one unreviewable ten-second candidate.
        //
        // Isolating the quiet event would need spectral subtraction or source separation, which is
        // a different tool; the honest answer for now is that a reviewer listens to the stretch.
        var buffer = new float[25 * SampleRate];
        AddRoomTone(buffer, 0.004);
        for (var t = 5.0; t < 15.0; t += 1.0)
            AddUtterance(buffer, t, 0.9, 0.150);          // the investigator, loud and near-continuous
        AddUtterance(buffer, 10.0, 0.5, 0.030);           // the faint event, buried inside it

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("during speech", found);

        Assert.Contains(found, c => c.EndSeconds >= 10.0 && c.StartSeconds <= 10.5);
        // 5s cap on the detected energy, plus context either side.
        Assert.All(found, c => Assert.True(c.DurationSeconds <= 5.0 + 2 * ContextPad + 0.1,
            $"a {c.DurationSeconds:0.0}s candidate is not something anyone reviews"));
    }

    [Fact]
    public void RespondsToBroadbandSpeechBandNoise_NotOnlyToTones()
    {
        // The synthetic utterances are three sine formants. If the detector were somehow keyed to
        // tonality it would pass every test above and fail on real speech, which is noisy.
        var buffer = new float[15 * SampleRate];
        AddRoomTone(buffer, 0.004);

        var rng = new Random(4242);
        var start = (int)(7.0 * SampleRate);
        var count = (int)(0.6 * SampleRate);
        for (var i = 0; i < count; i++)
        {
            var fade = Math.Min(1.0, Math.Min(i, count - i) / (0.02 * SampleRate));
            buffer[start + i] += (float)((rng.NextDouble() * 2 - 1) * 0.05 * fade);
        }

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("broadband burst", found);
        Assert.Contains(found, c => Covers(c, 7.3));
    }

    [Fact]
    public void DoesNotFloodOnAVeryNoisyRecording()
    {
        // Heavy hiss, nothing else. The failure this guards against is the worst one in practice:
        // hundreds of candidates on a bad recording, which makes the feature useless rather than
        // merely imperfect.
        var buffer = new float[60 * SampleRate];
        AddRoomTone(buffer, 0.080, seed: 777);

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        output.WriteLine($"noisy 60s: {found.Count} candidate(s)");
        Assert.True(found.Count <= 5, $"proposed {found.Count} candidates on pure noise");
    }

    [Fact]
    public void SurvivesAStepChangeInRoomNoise()
    {
        // The heating switches on halfway through. A global floor would treat the entire second
        // half as one enormous event; the sliding floor should absorb it within a few seconds.
        var buffer = new float[40 * SampleRate];
        AddRoomTone(buffer, 0.004);
        var loudFrom = (int)(20.0 * SampleRate);
        var rng = new Random(31337);
        for (var i = loudFrom; i < buffer.Length; i++)
            buffer[i] += (float)((rng.NextDouble() * 2 - 1) * 0.040);
        AddUtterance(buffer, 30.0, 0.8, 0.200);           // an event inside the louder half

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Dump("step change", found);

        Assert.Contains(found, c => Covers(c, 30.05));
        // The transition itself may propose something, but it must not swallow the whole tail.
        Assert.All(found, c => Assert.True(c.DurationSeconds < 10.0,
            $"a {c.DurationSeconds:0.0}s candidate means the floor never caught up"));
        // And nothing should still be gating minutes later.
        Assert.DoesNotContain(found, c => c.StartSeconds > 32.0);
    }

    // ── Decoding ──────────────────────────────────────────────────────────────

    [Fact]
    public void DetectsThroughTheWavDecodePath()
    {
        // Everything above feeds the algorithm floats directly. This covers the path the endpoint
        // actually uses: stored bytes in, candidates out.
        var wav = ToWav16BitMono(BuildFixture(), SampleRate);
        using var stream = new MemoryStream(wav);

        var found = EvpDetector.Detect(stream, "audio/wav", EvpSensitivity.Medium, 500);
        Dump("via WAV", found);

        foreach (var expected in UtteranceStarts)
            Assert.Contains(found, c => Covers(c, expected + 0.05));
    }

    private static byte[] ToWav16BitMono(float[] mono, int sampleRate)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var dataBytes = mono.Length * 2;
            writer.Write("RIFF"u8);           writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);           writer.Write("fmt "u8);
            writer.Write(16);                 writer.Write((short)1);
            writer.Write((short)1);           writer.Write(sampleRate);
            writer.Write(sampleRate * 2);     writer.Write((short)2);
            writer.Write((short)16);          writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (var s in mono)
                writer.Write((short)Math.Clamp(s * 32767f, short.MinValue, short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Contract ──────────────────────────────────────────────────────────────

    [Fact]
    public void IsDeterministic()
    {
        var fixture = BuildFixture();
        var first  = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.Medium, 500);
        var second = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.Medium, 500);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ReturnsCandidatesInPlaybackOrder()
    {
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.High, 500);
        Assert.Equal([.. found.OrderBy(c => c.StartSeconds)], found);
    }

    [Fact]
    public void RespectsTheResultCap_KeepingTheHighestScoring()
    {
        var fixture = BuildFixture();
        var all     = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.High, 500);
        var capped  = EvpDetector.Detect(fixture, SampleRate, EvpSensitivity.High, 2);

        Assert.Equal(2, capped.Count);
        var kept    = all.OrderByDescending(c => c.Score).Take(2).Select(c => c.StartSeconds).ToHashSet();
        Assert.All(capped, c => Assert.Contains(c.StartSeconds, kept));
    }

    [Fact]
    public void NeverProposesANegativeStart()
    {
        // Padding must not push an event at the very beginning of a file below zero.
        var buffer = new float[5 * SampleRate];
        AddRoomTone(buffer, 0.002);
        AddUtterance(buffer, 0.0, 0.6, 0.050);

        var found = EvpDetector.Detect(buffer, SampleRate, EvpSensitivity.Medium, 500);
        Assert.All(found, c => Assert.True(c.StartSeconds >= 0, $"start was {c.StartSeconds}"));
    }

    [Fact]
    public void HandlesInputShorterThanOneFrame()
    {
        Assert.Empty(EvpDetector.Detect(new float[10], SampleRate, EvpSensitivity.Medium, 500));
        Assert.Empty(EvpDetector.Detect([], SampleRate, EvpSensitivity.Medium, 500));
    }

    [Fact]
    public void EveryScoreIsWithinRange()
    {
        var found = EvpDetector.Detect(BuildFixture(), SampleRate, EvpSensitivity.High, 500);
        Assert.All(found, c => Assert.InRange(c.Score, 0f, 100f));
    }
}
