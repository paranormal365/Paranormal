using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>Model → wire-DTO conversions shared by every caller that builds a
/// <see cref="SegmentRenderSpec"/> — <see cref="NativeSidecarBackend"/> (preview) and
/// <see cref="NativeClipEncoder"/> (export, item #38 phase 124).</summary>
internal static class SidecarDtoMapping
{
    public static ClipEffectsDto ToDto(ClipEffects e) =>
        new(e.Brightness, e.Contrast, e.Saturation, e.FadeInSeconds, e.FadeOutSeconds);

    public static AppliedEffectDto ToDto(AppliedEffect e) =>
        new(e.EffectId, new Dictionary<string, double>(e.Parameters));
}
