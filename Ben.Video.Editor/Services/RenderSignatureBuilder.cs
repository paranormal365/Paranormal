using System.Security.Cryptography;
using System.Text;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Builds a content signature for a primary-track clip's preview render region — a hash of every
/// input that affects the segment's rendered bytes. Two clips with equal signatures are guaranteed
/// to render identical preview output; <see cref="RenderService.RenderRegionTracker"/> uses
/// signature equality (not clip mutation events) to decide when a cached render is stale.
/// Deliberately excludes <see cref="TrackItem.TimelinePosition"/> and track order — repositioning
/// or reordering a clip never changes what it renders to, only where the result gets assembled.
/// </summary>
public static class RenderSignatureBuilder
{
    public static string ForVideoClip(VideoClip clip, int previewWidth, int previewHeight)
    {
        var sb = new StringBuilder();
        sb.Append("video|");
        sb.Append(clip.MemFsName).Append('|');
        sb.Append(clip.StartTrim.ToString("R")).Append('|');
        sb.Append(clip.EndTrim.ToString("R")).Append('|');
        sb.Append(clip.Speed.ToString("R")).Append('|');
        sb.Append(clip.MuteAudio).Append('|');
        sb.Append(previewWidth).Append('x').Append(previewHeight).Append('|');
        AppendEffects(sb, clip.Effects);
        AppendAppliedEffects(sb, clip.AppliedEffects);
        AppendVolumeAutomation(sb, clip.Volume, clip.VolumeAutomation);
        return Hash(sb.ToString());
    }

    public static string ForImageClip(ImageClip clip, int previewWidth, int previewHeight)
    {
        var sb = new StringBuilder();
        sb.Append("image|");
        sb.Append(clip.MemFsName).Append('|');
        sb.Append(clip.Duration.ToString("R")).Append('|');
        sb.Append(clip.Width).Append('x').Append(clip.Height).Append('|');
        sb.Append(previewWidth).Append('x').Append(previewHeight).Append('|');
        AppendEffects(sb, clip.Effects);
        AppendAppliedEffects(sb, clip.AppliedEffects);
        return Hash(sb.ToString());
    }

    private static void AppendEffects(StringBuilder sb, ClipEffects effects)
    {
        sb.Append(effects.Brightness.ToString("R")).Append(',')
          .Append(effects.Contrast.ToString("R")).Append(',')
          .Append(effects.Saturation.ToString("R")).Append(',')
          .Append(effects.FadeInSeconds.ToString("R")).Append(',')
          .Append(effects.FadeOutSeconds.ToString("R")).Append('|');
    }

    private static void AppendAppliedEffects(StringBuilder sb, List<Effects.AppliedEffect> appliedEffects)
    {
        // Order matters — ffmpeg filters are applied sequentially — so no sorting here.
        foreach (var effect in appliedEffects)
        {
            sb.Append(effect.EffectId).Append('(');
            foreach (var (key, value) in effect.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.Append(key).Append('=').Append(value.ToString("R")).Append(';');
            sb.Append(')');
        }
        sb.Append('|');
    }

    private static void AppendVolumeAutomation(StringBuilder sb, double scalarVolume, List<VolumeKeyframe> automation)
    {
        sb.Append(scalarVolume.ToString("R")).Append('|');
        foreach (var kf in automation)
            sb.Append(kf.Position.ToString("R")).Append(':').Append(kf.Volume.ToString("R")).Append(';');
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
