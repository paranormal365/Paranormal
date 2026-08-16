using System.Globalization;
using System.Text.Json;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Turns <c>ffprobe -print_format json -show_streams</c> output into a typed
/// <see cref="MediaProbeInfo"/> — item #70 phase 159.
///
/// <para><b>Deliberately mirrors the browser's own parsing</b> in <c>ffmpegInterop.js</c>'s
/// <c>getMetadata</c>, including its audio-duration fallback: an audio-only file (mp3/wav/…) has no
/// video stream at all, so taking the video stream's duration would silently yield a 0-second clip.
/// The two paths must agree — a clip imported with the sidecar connected and the same clip imported
/// without it have to produce identical metadata, or the same project would behave differently
/// depending on whether a companion process happened to be running.</para>
///
/// <para>Tolerant by construction: every field is optional, malformed or missing values fall back
/// to zero rather than throwing. ffprobe output shape varies across builds and container formats,
/// and a probe that returns a slightly-wrong duration is far better than one that fails the import.</para>
/// </summary>
public static class FfprobeOutputParser
{
    /// <summary>Returns null only when the payload isn't parseable JSON at all — callers treat
    /// that as "probe failed, fall back to wasm" rather than as a zero-length clip.</summary>
    public static MediaProbeInfo? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? video = null, audio = null;
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (video is null && type == "video") video = stream;
                else if (audio is null && type == "audio") audio = stream;
            }

            // Same precedence as the JS: video duration, else audio duration, else 0.
            var duration = ReadDouble(video, "duration") ?? ReadDouble(audio, "duration") ?? 0.0;
            var width    = ReadInt(video, "width")  ?? 0;
            var height   = ReadInt(video, "height") ?? 0;

            return new MediaProbeInfo(duration, width, height);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>ffprobe emits numbers as JSON strings ("12.34") for some fields and as real
    /// numbers for others depending on build/format — accept either.</summary>
    private static double? ReadDouble(JsonElement? element, string property)
    {
        if (element is not { } e || !e.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement? element, string property)
    {
        if (element is not { } e || !e.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var n) ? n : (int)value.GetDouble(),
            JsonValueKind.String when int.TryParse(
                value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
