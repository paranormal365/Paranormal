using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// How somebody has set the editor up to <i>hear</i> a recording.
/// </summary>
/// <remarks>
/// <para>The equaliser, the high- and low-pass filters, the compressor and the noise gate. None of
/// it changes the file — it is the listening chain, applied to the sound leaving the speakers this
/// moment — which is exactly why losing it on every open was worse than it sounds. Somebody working
/// a two-hour recording finds a filter setting that lets them hear a whisper, closes the editor to
/// look at something else, and has to find it again from scratch (2026-09-06 audio walk,
/// finding L).</para>
///
/// <para>It rides in one JSON column rather than fourteen of its own. There are fourteen numbers
/// here and the chain will grow; a column each means a migration every time a filter is added, and
/// this is a private working state rather than something anything queries.</para>
///
/// <para>Reading is deliberately tolerant, field by field: a row written before a setting existed
/// keeps everything it does have. Every value is also range-checked on the way in, because a stored
/// <c>NaN</c> would silence the output with no sign of why.</para>
/// </remarks>
public sealed record AudioListeningChain
{
    /// <summary>How many bands the equaliser has. Fixed by the component.</summary>
    public const int BandCount = 10;

    /// <summary>Gain per band in dB, quietest band first. Always <see cref="BandCount"/> long.</summary>
    public IReadOnlyList<double> EqGains { get; init; } = new double[BandCount];

    public bool   HighPassOn  { get; init; }
    public int    HighPassHz  { get; init; } = 80;
    public bool   LowPassOn   { get; init; }
    public int    LowPassHz   { get; init; } = 16_000;

    public bool   CompressorOn          { get; init; }
    public double CompressorThresholdDb { get; init; } = -24;
    public double CompressorRatio       { get; init; } = 4;
    public double CompressorAttack      { get; init; } = 0.003;
    public double CompressorRelease     { get; init; } = 0.25;

    public bool   NoiseGateOn          { get; init; }
    public double NoiseGateThresholdDb { get; init; } = -40;
    public double NoiseGateAttack      { get; init; } = 0.01;
    public double NoiseGateRelease     { get; init; } = 0.15;

    /// <summary>Where silence detection draws the line. Not part of the chain, but set beside it.</summary>
    public double SilenceThresholdDb { get; init; } = -40;

    /// <summary>A recording nobody has set up: flat equaliser, everything off.</summary>
    public static AudioListeningChain Default { get; } = new();

    /// <summary>Whether anything here differs from the defaults.</summary>
    /// <remarks>
    /// Used to avoid writing a row that says nothing — and to tell somebody that what they are
    /// hearing is not the recording as it was captured.
    /// </remarks>
    public bool IsAnythingOn =>
        HighPassOn || LowPassOn || CompressorOn || NoiseGateOn || EqGains.Any(g => Math.Abs(g) > 0.001);

    // ── The round trip ────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads a saved chain, falling back field by field.</summary>
    /// <remarks>
    /// Null, empty and unreadable all mean "nothing was saved", which is the defaults. A partial
    /// row keeps what it has: a chain written before the noise gate existed should not lose its
    /// equaliser to one missing field.
    /// </remarks>
    public static AudioListeningChain FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;

        Stored? stored;
        try { stored = JsonSerializer.Deserialize<Stored>(json, Json); }
        catch (JsonException) { return Default; }

        if (stored is null) return Default;

        return new AudioListeningChain
        {
            EqGains = Bands(stored.EqGains),

            HighPassOn = stored.HighPassOn ?? Default.HighPassOn,
            HighPassHz = (int)Clamp(stored.HighPassHz, Default.HighPassHz, 10, 2_000),
            LowPassOn  = stored.LowPassOn  ?? Default.LowPassOn,
            LowPassHz  = (int)Clamp(stored.LowPassHz, Default.LowPassHz, 500, 22_000),

            CompressorOn          = stored.CompressorOn ?? Default.CompressorOn,
            CompressorThresholdDb = Clamp(stored.CompressorThresholdDb, Default.CompressorThresholdDb, -100, 0),
            CompressorRatio       = Clamp(stored.CompressorRatio,       Default.CompressorRatio,        1, 20),
            CompressorAttack      = Clamp(stored.CompressorAttack,      Default.CompressorAttack,       0, 1),
            CompressorRelease     = Clamp(stored.CompressorRelease,     Default.CompressorRelease,      0, 2),

            NoiseGateOn          = stored.NoiseGateOn ?? Default.NoiseGateOn,
            NoiseGateThresholdDb = Clamp(stored.NoiseGateThresholdDb, Default.NoiseGateThresholdDb, -100, 0),
            NoiseGateAttack      = Clamp(stored.NoiseGateAttack,      Default.NoiseGateAttack,       0, 1),
            NoiseGateRelease     = Clamp(stored.NoiseGateRelease,     Default.NoiseGateRelease,      0, 2),

            SilenceThresholdDb = Clamp(stored.SilenceThresholdDb, Default.SilenceThresholdDb, -100, 0),
        };
    }

    /// <summary>Writes this chain in the shape <see cref="FromJson"/> reads.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Stored
    {
        EqGains               = [.. EqGains],
        HighPassOn            = HighPassOn,
        HighPassHz            = HighPassHz,
        LowPassOn             = LowPassOn,
        LowPassHz             = LowPassHz,
        CompressorOn          = CompressorOn,
        CompressorThresholdDb = CompressorThresholdDb,
        CompressorRatio       = CompressorRatio,
        CompressorAttack      = CompressorAttack,
        CompressorRelease     = CompressorRelease,
        NoiseGateOn           = NoiseGateOn,
        NoiseGateThresholdDb  = NoiseGateThresholdDb,
        NoiseGateAttack       = NoiseGateAttack,
        NoiseGateRelease      = NoiseGateRelease,
        SilenceThresholdDb    = SilenceThresholdDb,
    }, Json);

    /// <summary>
    /// Exactly <see cref="BandCount"/> gains, whatever was stored.
    /// </summary>
    /// <remarks>
    /// A shorter array is padded flat and a longer one trimmed, because the component indexes ten
    /// sliders directly: a nine-band row read back would throw on render, and an eleven-band one
    /// would silently drop a band a future version had added.
    /// </remarks>
    private static double[] Bands(double[]? stored)
    {
        var bands = new double[BandCount];
        if (stored is null) return bands;

        for (var i = 0; i < BandCount && i < stored.Length; i++)
            bands[i] = Clamp(stored[i], 0, -24, 24);

        return bands;
    }

    /// <summary>
    /// A stored value, or the fallback when it is missing or not a real number in range.
    /// </summary>
    /// <remarks>
    /// <c>NaN</c> is the one that matters, as everywhere else in this editor: it survives both
    /// halves of a range test, and a NaN gain multiplies the whole output into silence with nothing
    /// to show why.
    /// </remarks>
    private static double Clamp(double? value, double fallback, double min, double max)
        => value is { } v && double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>The wire shape: every field nullable, so "absent" and "zero" stay different.</summary>
    private sealed record Stored
    {
        public double[]? EqGains { get; init; }
        public bool?    HighPassOn { get; init; }
        public double?  HighPassHz { get; init; }
        public bool?    LowPassOn { get; init; }
        public double?  LowPassHz { get; init; }
        public bool?    CompressorOn { get; init; }
        public double?  CompressorThresholdDb { get; init; }
        public double?  CompressorRatio { get; init; }
        public double?  CompressorAttack { get; init; }
        public double?  CompressorRelease { get; init; }
        public bool?    NoiseGateOn { get; init; }
        public double?  NoiseGateThresholdDb { get; init; }
        public double?  NoiseGateAttack { get; init; }
        public double?  NoiseGateRelease { get; init; }
        public double?  SilenceThresholdDb { get; init; }
    }
}
