namespace Ben.Video.Editor.Services;

/// <summary>
/// The single source of truth for which built-in <see cref="IClipEffect"/> plugins ship with the
/// editor. Extracted (item #38 phase 123) from what used to be an inline registration list in
/// <c>ServiceCollectionExtensions.AddBenVideoEditor</c> — the sidecar needs the exact same
/// effect-id → filter-builder mapping to turn an <c>AppliedEffectDto.EffectId</c> back into a real
/// ffmpeg filter fragment, and a second, hand-maintained copy of this list would silently drift
/// from the browser's (an effect added to one but not the other would either be invisible in the
/// UI or rejected as "unknown effect" by the sidecar). Both sides call
/// <see cref="CreateDefault"/>; a host app registering third-party <see cref="IClipEffect"/>s via
/// <c>AddBenVideoEditor</c> still only affects the browser's registry — those are out of scope for
/// the sidecar until a future phase lets a host register its own custom effects on both sides.
/// </summary>
public static class DefaultEffectRegistry
{
    public static ClipEffectRegistry CreateDefault()
    {
        var registry = new ClipEffectRegistry();

        // ── Existing video effects ─────────────────────────────────────────────
        registry.Register(new Plugins.Video.ColorGradingEffect());
        registry.Register(new Plugins.Video.FadeInEffect());
        registry.Register(new Plugins.Video.FadeOutEffect());
        registry.Register(new Plugins.Video.FadeToBlackEffect());
        registry.Register(new Plugins.Video.FadeToWhiteEffect());
        registry.Register(new Plugins.Video.GrayscaleEffect());

        // ── Phase 41: New video effects (animate.css inspired) ────────────────
        registry.Register(new Plugins.Video.SlideInFromLeftEffect());
        registry.Register(new Plugins.Video.SlideInFromRightEffect());
        registry.Register(new Plugins.Video.SlideInFromBottomEffect());
        registry.Register(new Plugins.Video.ZoomInEffect());
        registry.Register(new Plugins.Video.ZoomOutEffect());
        registry.Register(new Plugins.Video.KenBurnsEffect());
        registry.Register(new Plugins.Video.FadeInDownEffect());
        registry.Register(new Plugins.Video.FadeInUpEffect());
        registry.Register(new Plugins.Video.FlashEffect());
        registry.Register(new Plugins.Video.ShakeEffect());
        registry.Register(new Plugins.Video.BlurEffect());
        registry.Register(new Plugins.Video.RotateInEffect());
        registry.Register(new Plugins.Video.VignetteEffect());
        registry.Register(new Plugins.Video.SepiaEffect());

        // ── Existing image effects ─────────────────────────────────────────────
        registry.Register(new Plugins.Image.FlyInFromTopEffect());

        // ── Phase 41: New image effects ───────────────────────────────────────
        registry.Register(new Plugins.Image.ZoomInEffect());
        registry.Register(new Plugins.Image.ZoomOutEffect());
        registry.Register(new Plugins.Image.KenBurnsEffect());
        registry.Register(new Plugins.Image.SlideInFromLeftEffect());
        registry.Register(new Plugins.Image.SlideInFromRightEffect());
        registry.Register(new Plugins.Image.SlideInFromBottomEffect());
        registry.Register(new Plugins.Image.PulseEffect());

        // ── Phase 43: Colour effects ───────────────────────────────────────────
        registry.Register(new Plugins.Video.FadeFromColorEffect());
        registry.Register(new Plugins.Video.FadeToColorEffect());

        return registry;
    }
}
