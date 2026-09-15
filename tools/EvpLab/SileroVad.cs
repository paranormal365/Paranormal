using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EvpLab;

/// <summary>
/// Silero VAD (MIT, github.com/snakers4/silero-vad) over ONNX Runtime — the same runtime the API already loads on the
/// Windows/IIS box for the feed's image screener, so nothing new has to be proven to run there.
/// </summary>
/// <remarks>
/// The model reads 512 samples (32 ms at 16 kHz) at a time and carries a recurrent state between them. Its own Python
/// wrapper also prepends the previous chunk's last 64 samples; leaving that out measurably lowers its scores, so this
/// does the same. One instance per stream: the state belongs to the audio being read.
/// </remarks>
internal sealed class SileroVad : IFrameScorer, IDisposable
{
    public const int ChunkSamples = 512;
    private const int ContextSamples = 64;
    public static double ChunkSeconds => (double)ChunkSamples / Audio.Rate;

    private readonly InferenceSession _session;

    public SileroVad(string modelPath)
    {
        var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        _session = new InferenceSession(modelPath, options);
        var inputs = string.Join(",", _session.InputMetadata.Keys);
        if (inputs != "input,state,sr")
            throw new InvalidOperationException($"Unexpected Silero VAD inputs '{inputs}': this reader expects the v5/v6 model.");
    }

    /// <summary>Speech probability for each 32 ms chunk of a clip, read from a fresh state.</summary>
    public float[] Probabilities(ReadOnlySpan<float> samples)
    {
        var chunks = samples.Length / ChunkSamples;
        var result = new float[chunks];
        var state = new DenseTensor<float>(new[] { 2, 1, 128 });
        var context = new float[ContextSamples];
        var sr = new DenseTensor<long>(new long[] { Audio.Rate }, Array.Empty<int>());

        for (var c = 0; c < chunks; c++)
        {
            var input = new DenseTensor<float>(new[] { 1, ContextSamples + ChunkSamples });
            var buffer = input.Buffer.Span;
            context.CopyTo(buffer);
            samples.Slice(c * ChunkSamples, ChunkSamples).CopyTo(buffer[ContextSamples..]);

            using var outputs = _session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input", input),
                NamedOnnxValue.CreateFromTensor("state", state),
                NamedOnnxValue.CreateFromTensor("sr", sr),
            ]);
            result[c] = outputs.First(o => o.Name == "output").AsTensor<float>().GetValue(0);
            state = (DenseTensor<float>)outputs.First(o => o.Name == "stateN").AsTensor<float>().Clone();
            buffer[^ContextSamples..].CopyTo(context);
        }
        return result;
    }

    public string Title => "1b. Silero VAD (a voice-activity detector) — does this clip contain a voice?";
    public string RuleText => "Rule: a voice if the speech probability stays at or above **T** for at least **R** ms in a row (32 ms steps).";
    public double StepSeconds => ChunkSeconds;
    public double WindowSeconds => ChunkSeconds;
    public IReadOnlyList<(float T, int R)> Rules => [(0.3f, 32), (0.3f, 96), (0.3f, 192), (0.5f, 32), (0.5f, 96), (0.5f, 192), (0.7f, 32), (0.7f, 96), (0.7f, 192)];
    public (float T, int R) FileRule => (0.5f, 96);
    public float[] Score(float[] clip) => Probabilities(clip);

    public void Dispose() => _session.Dispose();
}
