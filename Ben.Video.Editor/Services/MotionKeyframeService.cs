using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that owns all motion-path keyframe data for the current editing session.
///
/// <para>Each layer (TextOverlay, CalloutClip, ImageClip) can have one <see cref="MotionPath"/>.
/// <see cref="Evaluate"/> returns the interpolated <see cref="MotionFrame"/> at any project
/// time — linear between keyframes, or cubic bezier when control handles are set.</para>
///
/// <para>All paths are serialised into <c>ProjectFile.MotionPaths</c> via
/// <see cref="ProjectService"/> so they survive Save / Open cycles.</para>
/// </summary>
public sealed class MotionKeyframeService
{
    private readonly Dictionary<Guid, MotionPath> _paths = [];

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised whenever any path or keyframe changes.</summary>
    public event Action? OnChanged;

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>All paths in this session.</summary>
    public IReadOnlyCollection<MotionPath> AllPaths => _paths.Values;

    /// <summary>Returns the path for <paramref name="layerId"/>, or <c>null</c>.</summary>
    public MotionPath? GetPath(Guid layerId)
        => _paths.TryGetValue(layerId, out var p) ? p : null;

    /// <summary>Returns <c>true</c> when the layer has at least one keyframe.</summary>
    public bool HasPath(Guid layerId)
        => _paths.ContainsKey(layerId) && _paths[layerId].Keyframes.Count > 0;

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Add or update a keyframe for <paramref name="layerId"/>.
    /// The path is created automatically if it does not exist.
    /// Keyframes are kept sorted by <see cref="MotionKeyframe.Time"/>.
    /// </summary>
    public void UpsertKeyframe(Guid layerId, string layerType, MotionKeyframe kf)
    {
        if (!_paths.TryGetValue(layerId, out var path))
        {
            path = new MotionPath { LayerId = layerId, LayerType = layerType };
            _paths[layerId] = path;
        }

        // Replace existing keyframe at same time (within 0.001 s tolerance) or insert
        var idx = path.Keyframes.FindIndex(k => Math.Abs(k.Time - kf.Time) < 0.001);
        if (idx >= 0)
            path.Keyframes[idx] = kf;
        else
            path.Keyframes.Add(kf);

        path.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
        Notify();
    }

    /// <summary>Remove the keyframe nearest to <paramref name="time"/> (within 0.1 s).</summary>
    public void RemoveKeyframe(Guid layerId, double time)
    {
        if (!_paths.TryGetValue(layerId, out var path)) return;
        var idx = path.Keyframes.FindIndex(k => Math.Abs(k.Time - time) < 0.1);
        if (idx < 0) return;
        path.Keyframes.RemoveAt(idx);
        if (path.Keyframes.Count == 0) _paths.Remove(layerId);
        Notify();
    }

    /// <summary>Remove all keyframes for <paramref name="layerId"/>.</summary>
    public void ClearPath(Guid layerId)
    {
        if (_paths.Remove(layerId)) Notify();
    }

    /// <summary>
    /// Retimes the keyframe currently at <paramref name="oldTime"/> to <paramref name="newTime"/>
    /// (item #57 P6 — dragging a keyframe diamond on the timeline). <see cref="UpsertKeyframe"/>
    /// is keyed on time, so calling it with a new time would create a second keyframe rather than
    /// move the existing one — this removes the keyframe at its old time first, so exactly one
    /// keyframe survives. If another keyframe already sits at <paramref name="newTime"/>, it is
    /// overwritten (matches <see cref="UpsertKeyframe"/>'s own same-time replace behavior).
    /// No-ops if no keyframe exists near <paramref name="oldTime"/>.
    /// </summary>
    public void MoveKeyframeTime(Guid layerId, double oldTime, double newTime)
    {
        if (!_paths.TryGetValue(layerId, out var path)) return;
        var idx = path.Keyframes.FindIndex(k => Math.Abs(k.Time - oldTime) < 0.001);
        if (idx < 0) return;

        var kf = path.Keyframes[idx] with { Time = newTime };
        path.Keyframes.RemoveAt(idx);

        var collisionIdx = path.Keyframes.FindIndex(k => Math.Abs(k.Time - newTime) < 0.001);
        if (collisionIdx >= 0) path.Keyframes[collisionIdx] = kf;
        else path.Keyframes.Add(kf);

        path.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
        Notify();
    }

    /// <summary>Replace all paths (used during project restore).</summary>
    public void RestoreAll(IEnumerable<MotionPath> paths)
    {
        _paths.Clear();
        foreach (var p in paths)
            _paths[p.LayerId] = p;
        Notify();
    }

    // ── Keyframe-aware editing (item #57, phase P2 — "the Camtasia rule") ──────

    /// <summary>
    /// THE single "write a static field, or upsert a keyframe" decision point. Every
    /// keyframe-aware editing gesture (canvas body-drag, resize handles, shape control-point
    /// handles) routes through this instead of checking <see cref="HasPath"/> itself, so the
    /// branch only has to be got right once. When the layer has no path, <paramref name="writeStatic"/>
    /// runs unchanged (today's pre-P2 behavior). When it has a path, a keyframe is upserted at
    /// <paramref name="time"/> — seeded from the already-interpolated frame there (so only the
    /// property <paramref name="mutateKeyframe"/> touches actually changes; everything else holds
    /// its current animated value) or, failing that (defensive — <see cref="Evaluate"/> shouldn't
    /// return null when <see cref="HasPath"/> is true), from <paramref name="staticSeed"/>.
    /// </summary>
    public void EditLayer(
        Guid layerId, string layerType, double time,
        Func<MotionKeyframe> staticSeed, Action<MotionKeyframe> mutateKeyframe, Action writeStatic)
    {
        if (!HasPath(layerId))
        {
            writeStatic();
            return;
        }

        UpsertKeyframeFromCurrent(layerId, layerType, time, staticSeed, mutateKeyframe);
    }

    /// <summary>
    /// Upserts a keyframe at <paramref name="time"/>, unconditionally (unlike <see cref="EditLayer"/>,
    /// which only does this when the layer already has a path) — seeded from the current
    /// interpolated frame if one exists, else from <paramref name="staticSeed"/>, with
    /// <paramref name="mutateKeyframe"/> applied on top. This is what a layer's very *first*
    /// keyframe needs (there's no path yet to check, and the caller's intent is unconditionally
    /// "add a keyframe here") — <see cref="EditLayer"/> is built on top of this for the
    /// conditional "static field vs. keyframe" case.
    /// </summary>
    public void UpsertKeyframeFromCurrent(
        Guid layerId, string layerType, double time,
        Func<MotionKeyframe> staticSeed, Action<MotionKeyframe> mutateKeyframe)
    {
        var frame = Evaluate(layerId, time);
        var kf    = frame is not null ? FrameToKeyframe(frame, time) : staticSeed() with { Time = time };
        mutateKeyframe(kf);
        UpsertKeyframe(layerId, layerType, kf);
    }

    /// <summary>Converts an interpolated <see cref="MotionFrame"/> into a fresh <see cref="MotionKeyframe"/>
    /// at <paramref name="time"/> — used to seed a new keyframe from the currently-displayed
    /// (blended) values. <see cref="MotionKeyframe.Easing"/>/bezier handles are deliberately left
    /// at their defaults (Linear/none) rather than inherited from whichever segment was
    /// interpolated — matches the pre-existing <c>MotionKeyframeEditor.AddKeyframeAtPlayhead</c>
    /// behavior this generalizes.</summary>
    private static MotionKeyframe FrameToKeyframe(MotionFrame frame, double time) => new()
    {
        Time               = time,
        X                  = frame.X,
        Y                  = frame.Y,
        Scale              = frame.Scale,
        Alpha              = frame.Alpha,
        FillColor          = frame.FillColor,
        StrokeColor        = frame.StrokeColor,
        ControlPointValues = new Dictionary<string, double>(frame.ControlPointValues),
        ShadowColor        = frame.ShadowColor,
        ShadowOffsetX      = frame.ShadowOffsetX,
        ShadowOffsetY      = frame.ShadowOffsetY,
        ShadowBlur         = frame.ShadowBlur,
    };

    /// <summary>
    /// True when <paramref name="time"/> sits strictly between two existing keyframes on
    /// <paramref name="layerId"/>'s path (not on/before the first or on/after the last, and not
    /// within 0.01s of an existing keyframe) — the case where upserting there captures the
    /// live-interpolated (blended) values rather than a fresh, deliberately-chosen one, silently
    /// flattening the curve past that point until the new keyframe is edited. Extracted from
    /// <c>MotionKeyframeEditor</c>'s pre-existing <c>IsAddingMidInterpolation</c> (backlog #21) so
    /// canvas-driven edits can show the same warning.
    /// </summary>
    public bool IsMidInterpolation(Guid layerId, double time)
    {
        if (!_paths.TryGetValue(layerId, out var path) || path.Keyframes.Count < 2) return false;
        const double epsilon = 0.01;
        if (path.Keyframes.Any(k => Math.Abs(k.Time - time) < epsilon)) return false;
        return time > path.Keyframes[0].Time && time < path.Keyframes[^1].Time;
    }

    // ── Evaluation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the interpolated <see cref="MotionFrame"/> for <paramref name="layerId"/>
    /// at <paramref name="time"/> seconds. Returns <c>null</c> when the layer has no path.
    ///
    /// <para>Interpolation algorithm:</para>
    /// <list type="number">
    ///   <item>If fewer than 2 keyframes exist, returns the single keyframe's values (clamped).</item>
    ///   <item>Finds the bracketing keyframes kf1 (≤ time) and kf2 (&gt; time).</item>
    ///   <item>Computes linear progress p = (time−kf1.Time) / (kf2.Time−kf1.Time).</item>
    ///   <item>Applies easing from <see cref="EasingHelper"/> (stored on kf2).</item>
    ///   <item>If kf1 has HandleOut AND kf2 has HandleIn: cubic bezier for X/Y.</item>
    ///   <item>Otherwise: linear lerp for all properties.</item>
    /// </list>
    /// </summary>
    public MotionFrame? Evaluate(Guid layerId, double time)
    {
        if (!_paths.TryGetValue(layerId, out var path) || path.Keyframes.Count == 0)
            return null;

        var kfs = path.Keyframes;

        // Single keyframe — return its values unchanged
        if (kfs.Count == 1)
            return FromKeyframe(kfs[0]);

        // Before first keyframe
        if (time <= kfs[0].Time)
            return FromKeyframe(kfs[0]);

        // After last keyframe
        if (time >= kfs[^1].Time)
            return FromKeyframe(kfs[^1]);

        // Find bracketing pair
        var i = kfs.FindLastIndex(k => k.Time <= time);
        var kf1 = kfs[i];
        var kf2 = kfs[i + 1];

        var segDur = kf2.Time - kf1.Time;
        var p = segDur > 0 ? Math.Clamp((time - kf1.Time) / segDur, 0.0, 1.0) : 1.0;

        // Apply easing (kf2 holds the easing into itself from kf1)
        var ep = EaseProgress(kf2.Easing, p);

        // Position: cubic bezier if both handles set, otherwise linear
        double x, y;
        if (kf1.HandleOutX.HasValue && kf1.HandleOutY.HasValue
            && kf2.HandleInX.HasValue && kf2.HandleInY.HasValue)
        {
            x = CubicBezier(ep, kf1.X, kf1.HandleOutX.Value, kf2.HandleInX.Value, kf2.X);
            y = CubicBezier(ep, kf1.Y, kf1.HandleOutY.Value, kf2.HandleInY.Value, kf2.Y);
        }
        else
        {
            x = Lerp(kf1.X, kf2.X, ep);
            y = Lerp(kf1.Y, kf2.Y, ep);
        }

        return new MotionFrame(
            x,
            y,
            Lerp(kf1.Scale, kf2.Scale, ep),
            Lerp(kf1.Alpha, kf2.Alpha, ep))
        {
            // Per-axis scale (item #57 P3): resolve each keyframe's own ScaleX/Y (falling back to
            // its Scale when unset) before lerping, so a segment where only one side ever set
            // per-axis values still interpolates sensibly instead of needing both sides to agree.
            ScaleX              = Lerp(kf1.ScaleX ?? kf1.Scale, kf2.ScaleX ?? kf2.Scale, ep),
            ScaleY              = Lerp(kf1.ScaleY ?? kf1.Scale, kf2.ScaleY ?? kf2.Scale, ep),
            // Rotation only lerps (ClipArt-only) when at least one bracketing keyframe animates
            // it — otherwise stays null so callers fall back to the layer's static Rotation,
            // matching pre-P3 behavior for every layer that never touches rotation keyframes.
            Rotation            = (kf1.Rotation, kf2.Rotation) is (null, null)
                ? null
                : Lerp(kf1.Rotation ?? kf2.Rotation!.Value, kf2.Rotation ?? kf1.Rotation!.Value, ep),
            FillColor           = LerpColor(kf1.FillColor, kf2.FillColor, ep),
            StrokeColor         = LerpColor(kf1.StrokeColor, kf2.StrokeColor, ep),
            ControlPointValues  = LerpControlPoints(kf1.ControlPointValues, kf2.ControlPointValues, ep),
            ShadowColor         = LerpColor(kf1.ShadowColor, kf2.ShadowColor, ep),
            ShadowOffsetX       = Lerp(kf1.ShadowOffsetX, kf2.ShadowOffsetX, ep),
            ShadowOffsetY       = Lerp(kf1.ShadowOffsetY, kf2.ShadowOffsetY, ep),
            ShadowBlur          = Lerp(kf1.ShadowBlur, kf2.ShadowBlur, ep),
        };
    }

    /// <summary>Builds a <see cref="MotionFrame"/> carrying one keyframe's values unchanged (no interpolation).</summary>
    private static MotionFrame FromKeyframe(MotionKeyframe kf) => new(kf.X, kf.Y, kf.Scale, kf.Alpha)
    {
        ScaleX              = kf.ScaleX ?? kf.Scale,
        ScaleY              = kf.ScaleY ?? kf.Scale,
        Rotation            = kf.Rotation,
        FillColor          = kf.FillColor,
        StrokeColor        = kf.StrokeColor,
        ControlPointValues = kf.ControlPointValues,
        ShadowColor         = kf.ShadowColor,
        ShadowOffsetX       = kf.ShadowOffsetX,
        ShadowOffsetY       = kf.ShadowOffsetY,
        ShadowBlur          = kf.ShadowBlur,
    };

    /// <summary>Linearly interpolates two packed ARGB colours, channel by channel.</summary>
    private static double LerpColor(double from, double to, double t)
    {
        var (r1, g1, b1, a1) = ColorHelper.Unpack(from);
        var (r2, g2, b2, a2) = ColorHelper.Unpack(to);
        byte L(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return ColorHelper.Pack(L(r1, r2), L(g1, g2), L(b1, b2), L(a1, a2));
    }

    /// <summary>
    /// Interpolates shape control points key-by-key. A key present in both keyframes lerps linearly;
    /// present in only one, it holds that keyframe's value (no fabricated intermediate).
    /// </summary>
    private static Dictionary<string, double> LerpControlPoints(
        IReadOnlyDictionary<string, double> from, IReadOnlyDictionary<string, double> to, double t)
    {
        var result = new Dictionary<string, double>();
        foreach (var key in from.Keys.Union(to.Keys))
        {
            var hasFrom = from.TryGetValue(key, out var fv);
            var hasTo   = to.TryGetValue(key, out var tv);
            result[key] = (hasFrom, hasTo) switch
            {
                (true, true)  => fv + (tv - fv) * t,
                (true, false) => fv,
                _             => tv,
            };
        }
        return result;
    }

    // ── ffmpeg expression generation ──────────────────────────────────────────

    /// <summary>
    /// Builds an ffmpeg expression string for <paramref name="property"/> (x, y, alpha, scale)
    /// across all keyframes using nested <c>if(lt(t,T),V,...)</c> branches.
    ///
    /// <para><b>Easing limitation</b>: The generated ffmpeg expression uses LINEAR
    /// interpolation only, regardless of the keyframe's <c>Easing</c> setting.
    /// ffmpeg's expression language does not support the polynomial/trigonometric
    /// curves used by the in-editor <c>Evaluate()</c> method. The preview will show
    /// easing correctly; the export will render linear motion between keyframes.
    /// A future improvement could pre-bake discrete frame values into a lookup table.</para>
    /// For <c>x</c> the expression is wrapped in <c>(W*...)</c> to convert canvas fraction
    /// to pixel coordinates. Likewise <c>y</c> is wrapped in <c>(H*...)</c>.
    /// Returns <c>null</c> when the layer has no path (use static value instead).
    /// </summary>
    public string? BuildFfmpegExpression(Guid layerId, string property, double staticFallback = 0)
    {
        if (!_paths.TryGetValue(layerId, out var path) || path.Keyframes.Count == 0)
            return null;

        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var kfs = path.Keyframes;

        if (kfs.Count == 1)
        {
            var val = GetPropValue(kfs[0], property).ToString("F4", ic);
            return WrapPixel(property, val);
        }

        var expr = GetPropValue(kfs[^1], property).ToString("F4", ic);

        for (var i = kfs.Count - 2; i >= 0; i--)
        {
            var kf1 = kfs[i];
            var kf2 = kfs[i + 1];
            var v1  = GetPropValue(kf1, property);
            var v2  = GetPropValue(kf2, property);
            var t1  = kf1.Time.ToString("F3", ic);
            var t2  = kf2.Time.ToString("F3", ic);
            var dur = (kf2.Time - kf1.Time).ToString("F3", ic);
            var v1s = v1.ToString("F4", ic);
            var dv  = (v2 - v1).ToString("F4", ic);

            var prog = $"min(max((t-{t1})/{dur},0),1)";
            var seg  = $"{v1s}+{dv}*{prog}";
            expr = $"if(lt(t,{t2}),{seg},{expr})";
        }

        var firstVal = GetPropValue(kfs[0], property).ToString("F4", ic);
        var raw = $"if(lt(t,{kfs[0].Time.ToString("F3", ic)}),{firstVal},{expr})";
        return WrapPixel(property, raw);
    }

    /// <summary>Wraps x/y expressions in W/H pixel multiplier for ffmpeg drawtext/drawbox.</summary>
    private static string WrapPixel(string property, string expr) => property switch
    {
        "x"     => $"(W*({expr}))",
        "y"     => $"(H*({expr}))",
        _       => expr,
    };

    // ── Private helpers ───────────────────────────────────────────────────────

    private static double GetPropValue(MotionKeyframe kf, string property) => property switch
    {
        "x"     => kf.X,
        "y"     => kf.Y,
        "scale" => kf.Scale,
        "alpha" => kf.Alpha,
        _       => 0
    };

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Cubic Bezier B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3</summary>
    private static double CubicBezier(double t, double p0, double p1, double p2, double p3)
    {
        var u = 1 - t;
        return u * u * u * p0
             + 3 * u * u * t * p1
             + 3 * u * t * t * p2
             + t * t * t * p3;
    }

    private static double EaseProgress(string easing, double p) => easing switch
    {
        "Ease In"     => p * p,
        "Ease Out"    => 1 - (1 - p) * (1 - p),
        "Ease In/Out" => p < 0.5 ? 2 * p * p : 1 - 2 * (1 - p) * (1 - p),
        "Bounce Out"  => 1 - Math.Abs(Math.Cos(Math.PI * p * 2.5)) * Math.Pow(1 - p, 2),
        "Elastic Out" => Math.Clamp(Math.Pow(2, -10 * p) * Math.Sin((p - 0.075) * 2 * Math.PI / 0.3) + 1, 0, 1),
        _             => p, // Linear
    };

    private void Notify() => OnChanged?.Invoke();
}
