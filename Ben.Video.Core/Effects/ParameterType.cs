namespace Ben.Video.Editor.Effects;

/// <summary>
/// Declares the UI control type rendered for an effect parameter.
/// </summary>
public enum ParameterType
{
    /// <summary>A continuous numeric range rendered as a slider.</summary>
    Range,

    /// <summary>A boolean toggle rendered as a checkbox.</summary>
    Toggle,

    /// <summary>
    /// A fixed list of named options rendered as a dropdown.
    /// The stored value is the zero-based index into <see cref="ClipEffectParameter.Options"/>.
    /// </summary>
    Select,

    /// <summary>
    /// An RGBA colour rendered as a <c>TelerikColorGradient</c> with opacity editor.
    /// The stored value is a packed 32-bit ARGB integer cast to double:
    /// <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c>.
    /// Use <see cref="Ben.Video.Editor.Effects.ColorHelper"/> for conversions.
    /// </summary>
    Color,
}
