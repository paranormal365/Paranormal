using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Singleton registry of all available <see cref="IClipEffect"/> implementations.
/// Register built-in and custom effects via <see cref="Register"/> during DI setup.
/// The UI and export pipeline resolve effects by <see cref="IClipEffect.EffectId"/>.
/// </summary>
public sealed class ClipEffectRegistry
{
    private readonly Dictionary<string, IClipEffect> _effects = new(StringComparer.Ordinal);

    /// <summary>
    /// Register an effect. Throws if an effect with the same
    /// <see cref="IClipEffect.EffectId"/> is already registered.
    /// </summary>
    public void Register(IClipEffect effect)
    {
        if (!_effects.TryAdd(effect.EffectId, effect))
            throw new InvalidOperationException(
                $"An effect with id '{effect.EffectId}' is already registered.");
    }

    /// <summary>All registered effects in registration order.</summary>
    public IReadOnlyList<IClipEffect> All => [.. _effects.Values];

    /// <summary>
    /// Returns the effect for <paramref name="effectId"/>, or <c>null</c> if not found.
    /// </summary>
    public IClipEffect? GetById(string effectId)
        => _effects.TryGetValue(effectId, out var e) ? e : null;
}
