namespace EvpLab;

/// <summary>A model that scores audio step by step for "a voice is here" — graded by <see cref="Bench"/> the same way whatever it is.</summary>
internal interface IFrameScorer
{
    string Title { get; }
    string RuleText { get; }
    /// <summary>Seconds between consecutive scores.</summary>
    double StepSeconds { get; }
    /// <summary>Seconds of audio each score looks at (a flagged score covers this much from its start).</summary>
    double WindowSeconds { get; }
    IReadOnlyList<(float T, int R)> Rules { get; }
    (float T, int R) FileRule { get; }
    float[] Score(float[] clip);
}
