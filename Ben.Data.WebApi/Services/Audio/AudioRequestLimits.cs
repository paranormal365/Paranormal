using Ben.Service.Models.Entities;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// The numbers the audio endpoints will accept, and the sentences they answer with when they will
/// not.
/// </summary>
/// <remarks>
/// <para>Every audio endpoint used to carry its own partial idea of what a valid request looked
/// like, and the gaps between them were the interesting part: <c>SpeedRatio</c> was bounded above
/// but not below, so <c>0.001</c> asked for a thousand times the samples and then ran a phase
/// vocoder over them; <c>GainDb</c> and the fades accepted <c>NaN</c>, which multiplies every
/// sample into nothing and answers 201 with a silent file; the mixer's offsets were not bounded at
/// all. None of those are attacks — they are what a slider sends when it is dragged to its end, or
/// what a script sends when a field is left out (2026-09-06 audio walk, findings 2, 3, 13).</para>
///
/// <para>The bounds live here, in one place, so the edit endpoint and the mixer cannot drift apart
/// about what a sane gain is. Anything outside them is a 400 with a sentence naming the field and
/// the range, because a person met this by dragging something.</para>
/// </remarks>
internal static class AudioRequestLimits
{
    /// <summary>Slowest an edit may play: a quarter speed is four times the samples, and past that the phase vocoder's cost stops being worth anybody's wait.</summary>
    public const double MinSpeedRatio = 0.25;

    /// <summary>Fastest an edit may play.</summary>
    public const double MaxSpeedRatio = 4.0;

    /// <summary>Quietest gain: below this the result is silence and the person meant Silence.</summary>
    public const double MinGainDb = -60;

    /// <summary>Loudest gain. Everything clamps to full scale anyway; this is where it stops being audio.</summary>
    public const double MaxGainDb = 24;

    /// <summary>Furthest into a mix a clip may be placed — an hour, well past any session's worth of takes.</summary>
    public const double MaxOffsetSeconds = 3600;

    /// <summary>Most tracks one mix may hold. The mixer UI offers eight lanes.</summary>
    public const int MaxMixTracks = 8;

    /// <summary>
    /// Longest name a derived file may be given.
    /// </summary>
    /// <remarks>
    /// The label becomes the new file's name (max 500) and its description (max 2000), so a longer
    /// one throws inside <c>SaveChanges</c> — after the bytes have already been written to storage,
    /// leaving a file on disk that no row points at (finding 7). 200 is the same ceiling a marker
    /// label has, which is where people are already used to it.
    /// </remarks>
    public const int MaxLabelLength = 200;

    /// <summary>Every reason an edit request would be refused, or null if there is none.</summary>
    public static string? EditProblem(AudioEditRequest request)
    {
        if (!Enum.IsDefined(request.Operation))
            return $"Unknown operation: {(int)request.Operation}.";

        if (LabelProblem(request.Label) is { } labelProblem) return labelProblem;

        if (request.Operation is AudioEditOperation.Cut or AudioEditOperation.Silence)
        {
            if (request.Start is null || request.End is null)
                return "Start and End are required for Cut/Silence.";
            if (!IsFinite(request.Start) || !IsFinite(request.End))
                return "Start and End must be real numbers of seconds.";
            if (request.Start < 0)
                return "A region cannot start before the recording does.";
            if (request.End <= request.Start)
                return "End must be greater than Start.";
        }

        if (request.Operation == AudioEditOperation.Gain)
        {
            if (request.GainDb is null) return "GainDb is required for Gain.";
            if (!IsFinite(request.GainDb)) return "GainDb must be a real number of decibels.";
            if (request.GainDb is { } gain && (gain < MinGainDb || gain > MaxGainDb))
                return $"GainDb must be between {MinGainDb} and {MaxGainDb} dB.";
        }

        if (request.Operation == AudioEditOperation.Fade)
        {
            if (!IsFinite(request.FadeInSeconds) || !IsFinite(request.FadeOutSeconds))
                return "Fade lengths must be real numbers of seconds.";
            if (request.FadeInSeconds < 0 || request.FadeOutSeconds < 0)
                return "A fade cannot be a negative length.";
            if ((request.FadeInSeconds ?? 0) == 0 && (request.FadeOutSeconds ?? 0) == 0)
                return "Give a fade-in length, a fade-out length, or both.";
        }

        if (request.Operation == AudioEditOperation.Speed)
        {
            if (request.SpeedRatio is null) return "SpeedRatio is required for Speed.";
            if (!IsFinite(request.SpeedRatio)) return "SpeedRatio must be a real number.";
            if (request.SpeedRatio is { } ratio && (ratio < MinSpeedRatio || ratio > MaxSpeedRatio))
                return $"SpeedRatio must be between {MinSpeedRatio} and {MaxSpeedRatio}.";
        }

        if (request.Operation == AudioEditOperation.Pitch)
        {
            if (request.PitchSemitones is null) return "PitchSemitones is required for Pitch.";
            if (!IsFinite(request.PitchSemitones)) return "PitchSemitones must be a real number.";
            if (request.PitchSemitones is < -24 or > 24)
                return "PitchSemitones must be between -24 and 24.";
        }

        return null;
    }

    /// <summary>Every reason a mix export would be refused, or null if there is none.</summary>
    public static string? MixProblem(IReadOnlyList<MixTrackExportInput> tracks)
    {
        if (tracks.Count == 0) return "At least one track is required.";
        if (tracks.Count > MaxMixTracks)
            return $"A mix holds at most {MaxMixTracks} tracks; this one has {tracks.Count}.";

        foreach (var track in tracks)
        {
            if (!IsFinite(track.OffsetSeconds) || !IsFinite(track.GainDb) || !IsFinite(track.Pan))
                return "Every track's offset, gain and pan must be real numbers.";
            if (track.OffsetSeconds < 0 || track.OffsetSeconds > MaxOffsetSeconds)
                return $"A clip must be placed between 0 and {MaxOffsetSeconds} seconds into the mix.";
            if (track.GainDb < MinGainDb || track.GainDb > MaxGainDb)
                return $"Track gain must be between {MinGainDb} and {MaxGainDb} dB.";
            if (track.Pan is < -1 or > 1)
                return "Track pan must be between -1 (left) and 1 (right).";
        }

        return null;
    }

    /// <summary>
    /// Every reason a marker span would be refused, or null if there is none.
    /// </summary>
    /// <remarks>
    /// Create and Update accepted spans that Review and Candidates on the same controller already
    /// rejected — an inverted span, a negative start, a label past the column's 200 characters
    /// (which throws inside <c>SaveChanges</c> rather than answering) (finding 8). One check, used
    /// by all four.
    /// </remarks>
    public static string? MarkerSpanProblem(double? start, double? end, string? label)
    {
        if (label is not null && LabelProblem(label) is { } labelProblem) return labelProblem;

        if (start is not null)
        {
            if (!IsFinite(start)) return "A marker's position must be a real number of seconds.";
            if (start < 0) return "A marker cannot start before the recording does.";
        }

        if (end is not null)
        {
            if (!IsFinite(end)) return "A marker's end must be a real number of seconds.";
            if (start is not null && end <= start) return "A span must end after it starts.";
        }

        return null;
    }

    /// <summary>Why this label cannot be used, or null.</summary>
    public static string? LabelProblem(string? label)
        => label is { Length: > MaxLabelLength }
            ? $"A name may be at most {MaxLabelLength} characters; this one is {label.Length}."
            : null;

    /// <summary>
    /// A number that means something: not null, not NaN, not an infinity.
    /// </summary>
    /// <remarks>
    /// <c>NaN</c> is the one that mattered. It survives every comparison — <c>NaN &lt; 0</c> and
    /// <c>NaN &gt; 24</c> are both false — so a range check written the obvious way lets it
    /// straight through, and it then multiplies every sample into <c>NaN</c>, writes as zero, and
    /// answers 201 with a file of silence.
    /// </remarks>
    public static bool IsFinite(double? value) => value is null || double.IsFinite(value.Value);
}
