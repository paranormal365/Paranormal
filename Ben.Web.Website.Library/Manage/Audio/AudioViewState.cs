using Ben.Service.Models.Entities;

namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// How somebody has set the editor up to look at one recording.
/// </summary>
/// <remarks>
/// <para>Not the recording and not the work: the view. Whether the spectrogram is showing, which
/// colour ramp, how fine, whether the axis is mel-scaled, whether the timeline is on. Someone
/// working through a two-hour recording sets these once and then sets them again every time they
/// reopen the file, because nothing remembered them — the table to remember them in has existed
/// since 2026-07-18 and nothing has ever read or written it (2026-09-06 audio walk, finding L).</para>
///
/// <para>It rides in the audio config's existing spectrogram-options column, so remembering this
/// needs no schema change. The listening chain — EQ, filters, gate, compressor — has no column to
/// live in and waits for one.</para>
/// </remarks>
public sealed record AudioViewState
{
    public bool   SpectrogramVisible { get; init; }
    public bool   SpectrogramLabels  { get; init; } = true;
    public int    FftSamples         { get; init; } = 512;
    public string Colormap           { get; init; } = "jet";
    public bool   MelScale           { get; init; }
    public bool   TimelineVisible    { get; init; } = true;

    /// <summary>
    /// The listening chain — equaliser, filters, compressor, noise gate.
    /// </summary>
    /// <remarks>
    /// Carried here so there is one save path and one place that decides whether anything changed.
    /// It rides in its own column rather than the spectrogram's, because it is about what you hear
    /// rather than what you see (2026-09-06 audio audit, phase 5b).
    /// </remarks>
    public AudioListeningChain Chain { get; init; } = AudioListeningChain.Default;

    /// <summary>What a recording nobody has set up yet looks like.</summary>
    public static AudioViewState Default { get; } = new();

    /// <summary>
    /// Reads a saved config back, falling back field by field.
    /// </summary>
    /// <remarks>
    /// Field by field rather than all-or-nothing: a row written before this shape grew a colormap
    /// should keep the FFT size it does have rather than losing everything to one missing value.
    /// </remarks>
    public static AudioViewState From(UploadFileAudioConfigRecord? record)
    {
        if (record is null) return Default;

        var spectrogram = UploadFileAudioConfigExtensions.DeserializeSpectrogramOptions(record.SpectrogramOptionsJson);

        return new AudioViewState
        {
            SpectrogramVisible = record.EnableSpectrogram,
            TimelineVisible    = record.EnableTimeline,
            SpectrogramLabels  = spectrogram?.Labels     ?? Default.SpectrogramLabels,
            FftSamples         = spectrogram?.FftSamples ?? Default.FftSamples,
            Colormap           = spectrogram?.Colormap   ?? Default.Colormap,
            MelScale           = spectrogram?.MelScale   ?? Default.MelScale,
            Chain              = AudioListeningChain.FromJson(record.EditStateJson),
        };
    }

    /// <summary>The request that saves this, leaving every other setting on the row alone.</summary>
    /// <remarks>
    /// The upsert replaces the whole row, so anything not carried here would be wiped. The other
    /// fields are passed through from <paramref name="existing"/> — which is null the first time,
    /// and then this is the only thing that has ever written the row.
    /// </remarks>
    public UpsertAudioConfigRequest ToRequest(UploadFileAudioConfigRecord? existing)
        => new()
        {
            EnableSpectrogram = SpectrogramVisible,
            EnableTimeline    = TimelineVisible,
            EditStateJson = Chain.ToJson(),

            SpectrogramOptionsJson = UploadFileAudioConfigExtensions.SerializeSpectrogramOptions(new WsSpectrogramOptions
            {
                Labels     = SpectrogramLabels,
                FftSamples = FftSamples,
                Colormap   = Colormap,
                MelScale   = MelScale,
            }),

            // Carried, not invented.
            WaveColor      = existing?.WaveColor,
            ProgressColor  = existing?.ProgressColor,
            CursorColor    = existing?.CursorColor,
            CursorWidth    = existing?.CursorWidth,
            Height         = existing?.Height,
            BarWidth       = existing?.BarWidth,
            BarGap         = existing?.BarGap,
            BarRadius      = existing?.BarRadius,
            BarHeight      = existing?.BarHeight,
            BarAlign       = existing?.BarAlign,
            Normalize      = existing?.Normalize      ?? true,
            DragToSeek     = existing?.DragToSeek     ?? false,
            HideScrollbar  = existing?.HideScrollbar  ?? false,
            AudioRate      = existing?.AudioRate,
            EnableHover    = existing?.EnableHover    ?? true,
            EnableZoom     = existing?.EnableZoom     ?? true,
            EnableMinimap  = existing?.EnableMinimap  ?? false,
            EnableRegions  = existing?.EnableRegions  ?? true,
            EnableEnvelope = existing?.EnableEnvelope ?? false,
            EnableSpectrogramWindowed = existing?.EnableSpectrogramWindowed ?? false,
            HoverOptionsJson    = existing?.HoverOptionsJson,
            TimelineOptionsJson = existing?.TimelineOptionsJson,
            ZoomOptionsJson     = existing?.ZoomOptionsJson,
            MinimapOptionsJson  = existing?.MinimapOptionsJson,
            EnvelopeOptionsJson = existing?.EnvelopeOptionsJson,
            SpectrogramWindowedOptionsJson = existing?.SpectrogramWindowedOptionsJson,
            // Non-nullable on the request, and the model binder refuses a null with a 400 that the
            // client drops as unreadable — so the whole save failed silently until an explicit PUT
            // of the same body reproduced it (2026-09-06 audio audit, phase 5). The record's own
            // defaults are the fallback, not invented ones.
            InitialHeight  = existing?.InitialHeight is { Length: > 0 } h  ? h  : "200px",
            MinHeight      = existing?.MinHeight     is { Length: > 0 } mn ? mn : "80px",
            MaxHeight      = existing?.MaxHeight     is { Length: > 0 } mx ? mx : "800px",
            ShowControls   = existing?.ShowControls ?? true,
            MinZoom        = existing?.MinZoom ?? 10,
            MaxZoom        = existing?.MaxZoom ?? 1000,
        };

    /// <summary>
    /// Whether anything a person would notice differs between two views.
    /// </summary>
    /// <remarks>
    /// Record equality reaches <see cref="Chain"/> as well, but its equaliser is a list — and a
    /// list compares by reference, so two identical chains would read as different and every
    /// control would send a save. Compared element by element here.
    /// </remarks>
    public bool SameAs(AudioViewState other)
        => this with { Chain = AudioListeningChain.Default } == other with { Chain = AudioListeningChain.Default }
        && ChainsMatch(Chain, other.Chain);

    private static bool ChainsMatch(AudioListeningChain a, AudioListeningChain b)
        => a with { EqGains = [] } == b with { EqGains = [] }
        && a.EqGains.SequenceEqual(b.EqGains);
}
