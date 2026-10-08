using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EvpLab;

/// <summary>
/// The model tools/EvpLearn trains: one second of 16 kHz audio in (input <c>audio</c> [batch, samples]), the probability
/// a human voice is present out (<c>voice</c> [batch]). Its spectrogram is inside the ONNX file, so this only slices
/// seconds — exactly what the API would do on the Windows server.
/// </summary>
internal sealed class VoiceNet : IFrameScorer, IDisposable
{
    private const int Window = Audio.Rate;       // 1 s
    private const int Hop = Audio.Rate / 4;      // 0.25 s
    private readonly InferenceSession _session;

    public VoiceNet(string modelPath)
    {
        _session = new InferenceSession(modelPath, new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 });
        if (!_session.InputMetadata.ContainsKey("audio") || !_session.OutputMetadata.ContainsKey("voice"))
            throw new InvalidOperationException("Not a VoiceNet export: expected input 'audio' and output 'voice'.");
    }

    public string Title => "1c. VoiceNet (trained here, tools/EvpLearn) — does this clip contain a voice?";
    public string RuleText => "Rule: a voice if any one-second window (0.25 s steps) scores at or above **T** (R is unused: one window is enough).";
    public double StepSeconds => (double)Hop / Audio.Rate;
    public double WindowSeconds => (double)Window / Audio.Rate;
    public IReadOnlyList<(float T, int R)> Rules => [(0.3f, 0), (0.5f, 0), (0.7f, 0), (0.9f, 0)];
    public (float T, int R) FileRule => (0.9f, 0);   // over a whole file every window is a chance to be wrong; 0.5 raised 4.2 false alarms a minute

    public float[] Score(float[] clip)
    {
        if (clip.Length < Window) clip = [.. clip, .. new float[Window - clip.Length]];
        var starts = new List<int>();
        for (var s = 0; s + Window <= clip.Length; s += Hop) starts.Add(s);
        var scores = new float[starts.Count];
        const int batch = 64;
        for (var b = 0; b < starts.Count; b += batch)
        {
            var n = Math.Min(batch, starts.Count - b);
            var input = new DenseTensor<float>(new[] { n, Window });
            for (var i = 0; i < n; i++) clip.AsSpan(starts[b + i], Window).CopyTo(input.Buffer.Span.Slice(i * Window, Window));
            using var outputs = _session.Run([NamedOnnxValue.CreateFromTensor("audio", input)]);
            var voice = outputs.First().AsTensor<float>();
            for (var i = 0; i < n; i++) scores[b + i] = voice.GetValue(i);
        }
        return scores;
    }

    public void Dispose() => _session.Dispose();
}
