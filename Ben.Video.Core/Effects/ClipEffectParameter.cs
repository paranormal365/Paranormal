namespace Ben.Video.Editor.Effects;

/// <summary>
/// Describes a single configurable parameter for an <see cref="IClipEffect"/>.
/// Used by the UI to render the appropriate control (slider, checkbox, etc.).
/// </summary>
public sealed record ClipEffectParameter
{
    /// <summary>Unique key used to look up this parameter in <c>AppliedEffect.Parameters</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label shown in the effects panel.</summary>
    public required string Label { get; init; }

    /// <summary>Determines which UI control is rendered.</summary>
    public ParameterType Type { get; init; } = ParameterType.Range;

    /// <summary>Minimum value (for <see cref="ParameterType.Range"/> parameters).</summary>
    public double Min { get; init; } = 0.0;

    /// <summary>Maximum value (for <see cref="ParameterType.Range"/> parameters).</summary>
    public double Max { get; init; } = 1.0;

    /// <summary>Slider small step (for <see cref="ParameterType.Range"/> parameters).</summary>
    public double Step { get; init; } = 0.05;

    /// <summary>Slider large step (for <see cref="ParameterType.Range"/> parameters).</summary>
    public double LargeStep { get; init; } = 0.25;

    /// <summary>Value used when the effect is first added to a clip.</summary>
    public double DefaultValue { get; init; } = 0.0;

    /// <summary>
    /// For <see cref="ParameterType.Select"/> parameters: the ordered list of option labels.
    /// The stored numeric value is the zero-based index of the selected option.
    /// </summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}
