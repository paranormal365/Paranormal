namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// The spectrogram's dials beyond resolution and colors, as WaveSurfer 8's SpectrogramPlugin names
/// them (phase 1b). Set in the editor's Display tab and sent through
/// <see cref="WaveSurferPlayer.SetSpectrogramAnalysisAsync"/>.
/// </summary>
/// <param name="Scale">How frequencies are spaced: linear, logarithmic, mel, bark or erb.</param>
/// <param name="FrequencyMin">The lowest frequency shown, in Hz.</param>
/// <param name="FrequencyMax">The highest frequency shown, in Hz; 0 shows everything the recording holds.</param>
/// <param name="WindowFunc">The FFT window: hann is the usual choice.</param>
/// <param name="GainDb">Brightens a quiet recording.</param>
/// <param name="RangeDb">How many dB from loudest to black. Smaller shows less of the noise floor.</param>
/// <param name="PreEmphasis">dB per octave added above 1 kHz, so the upper formants of a voice show.</param>
/// <param name="SplitChannels">One picture per channel for a stereo recording.</param>
public sealed record SpectrogramAnalysis(
    string Scale,
    double FrequencyMin,
    double FrequencyMax,
    string WindowFunc,
    double GainDb,
    double RangeDb,
    double PreEmphasis,
    bool SplitChannels)
{
    /// <summary>The plugin's own defaults, except the scale, which starts linear as the editor always has.</summary>
    public static SpectrogramAnalysis Default { get; } = new("linear", 0, 0, "hann", 20, 80, 0, false);
}

/// <summary>The choices the Display tab offers, shared with anything else that needs them.</summary>
public static class AudioDisplayOptions
{
    public static IReadOnlyList<(int Value, string Label)> Resolutions { get; } =
    [
        (128,  "128 – Coarse"),
        (256,  "256 – Low"),
        (512,  "512 – Standard"),
        (1024, "1024 – High"),
        (2048, "2048 – Fine"),
        (4096, "4096 – Ultra"),
    ];

    public static IReadOnlyList<(string Value, string Label)> Colormaps { get; } =
        [("jet", "Jet"), ("viridis", "Viridis"), ("inferno", "Inferno"), ("grayscale", "Grayscale")];

    public static IReadOnlyList<(string Value, string Label)> Scales { get; } =
    [
        ("linear",      "Linear: even spacing"),
        ("mel",         "Mel: like hearing"),
        ("logarithmic", "Logarithmic"),
        ("bark",        "Bark"),
        ("erb",         "ERB"),
    ];

    public static IReadOnlyList<(string Value, string Label)> Windows { get; } =
    [
        ("hann", "Hann"), ("hamming", "Hamming"), ("blackman", "Blackman"), ("bartlett", "Bartlett"),
        ("cosine", "Cosine"), ("gauss", "Gaussian"), ("lanczoz", "Lanczos"), ("rectangular", "Rectangular"),
        ("triangular", "Triangular"),
    ];
}
