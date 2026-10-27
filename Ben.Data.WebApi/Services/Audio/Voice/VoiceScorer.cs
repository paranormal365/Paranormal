using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Ben.Data.WebApi.Services.Audio.Voice;

/// <summary>
/// How voice-shaped a second of sound is, 0–1 (audio phase 9b, 10/27/2026).
/// </summary>
/// <remarks>
/// <para>A second opinion beside the detector's score, never instead of it: the detector says how far a
/// sound stood out from the room; this says how much it is shaped like a human voice. Neither says it is
/// a voice, and nothing is ever dropped or hidden because of this number.</para>
/// </remarks>
public interface IVoiceScorer
{
    /// <summary>True when a model is loaded and can be asked.</summary>
    bool IsAvailable { get; }

    /// <summary>The model's version, stored with every scan it measured: <c>voicenet-v1</c>.</summary>
    string? ModelName { get; }

    /// <summary>Why nothing is measured, when <see cref="IsAvailable"/> is false; a sentence for people.</summary>
    string? UnavailableReason { get; }

    /// <summary>One reading per window; each window is exactly <see cref="VoiceScoring.WindowSamples"/> samples at 16 kHz.</summary>
    float[] Score(IReadOnlyList<float[]> windows);
}

/// <summary>
/// VoiceNet over ONNX Runtime: trained in <c>tools/EvpLearn</c> on LibriSpeech test-clean (CC BY 4.0) and
/// synthetic room noise, and committed beside the API (1.1 MB) rather than fetched.
/// </summary>
/// <remarks>
/// The model's front end (peak normalising, the spectrogram, removing each band's floor) is inside the
/// network, so this passes plain samples and has nothing of its own to get subtly wrong. Input
/// <c>audio</c> <c>[batch, 16000]</c>, output <c>voice</c> <c>[batch]</c>.
/// </remarks>
public sealed class OnnxVoiceScorer : IVoiceScorer, IDisposable
{
    /// <summary>The model in use. A retrain ships as a new file and a new name, so readings are never mixed.</summary>
    public const string CurrentModel = "voicenet-v1";

    public static string ModelRelativePath(string model = CurrentModel) => Path.Combine("Models", $"{model}.onnx");

    /// <summary>Windows per call: bounded so one long candidate list can't ask for a huge tensor.</summary>
    private const int Batch = 64;

    private readonly Lazy<InferenceSession> _session;

    public OnnxVoiceScorer(string modelPath, string modelName = CurrentModel)
    {
        ModelName = modelName;
        _session = new Lazy<InferenceSession>(() =>
        {
            using var options = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1 };
            return new InferenceSession(modelPath, options);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => true;
    public string? ModelName { get; }
    public string? UnavailableReason => null;

    public float[] Score(IReadOnlyList<float[]> windows)
    {
        var results = new float[windows.Count];
        for (var start = 0; start < windows.Count; start += Batch)
        {
            var count = Math.Min(Batch, windows.Count - start);
            var input = new DenseTensor<float>([count, VoiceScoring.WindowSamples]);
            var buffer = input.Buffer.Span;
            for (var i = 0; i < count; i++)
                windows[start + i].AsSpan(0, VoiceScoring.WindowSamples).CopyTo(buffer.Slice(i * VoiceScoring.WindowSamples));

            using var output = _session.Value.Run([NamedOnnxValue.CreateFromTensor("audio", input)]);
            var voice = output.First(o => o.Name == "voice").AsEnumerable<float>().ToArray();
            for (var i = 0; i < count; i++) results[start + i] = Math.Clamp(voice[i], 0f, 1f);
        }
        return results;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated) _session.Value.Dispose();
    }
}

/// <summary>Registered when the model file is missing: nothing is measured, and it says why.</summary>
public sealed class NullVoiceScorer(string reason) : IVoiceScorer
{
    public const string MissingModelReason = "Voice-likeness is not measured on this server: the model is not installed.";

    public bool IsAvailable => false;
    public string? ModelName => null;
    public string? UnavailableReason { get; } = reason;
    public float[] Score(IReadOnlyList<float[]> windows) => throw new InvalidOperationException(UnavailableReason);
}
