namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// What put a region on the waveform.
/// </summary>
/// <remarks>
/// <para>The waveform carries regions from four different sources at once, and only one of them is
/// a statement of intent. Before this existed the editor tracked "not user-drawn" as a set of ids
/// it had added itself, which worked for the overlays it drew and not at all for the ones
/// JavaScript drew: silence detection adds its regions inside <c>detectSilence</c>, so their ids
/// never reached C#, every one of them arrived looking like something a person had dragged, and the
/// last one became the target that Cut and Silence would act on (2026-09-06 audio walk, finding
/// B).</para>
///
/// <para>So the kind travels with the region instead of being inferred from a list. Anything the
/// player was not told about is <see cref="User"/> — the only safe default, because a region
/// nobody registered is one somebody dragged.</para>
/// </remarks>
public enum RegionKind
{
    /// <summary>Dragged across the waveform by a person. The only kind an edit may act on.</summary>
    User,

    /// <summary>Found by silence detection. Shading, not a selection.</summary>
    Silence,

    /// <summary>An EVP marker or detector candidate.</summary>
    Marker,

    /// <summary>A clip already saved from this recording, drawn where it came from.</summary>
    Clip,

    /// <summary>Drawn by the editor for some other purpose.</summary>
    Overlay,
}

/// <summary>
/// Which region a destructive edit would act on, and what happens to the others.
/// </summary>
/// <remarks>
/// <para>Two rules, both of which the editor used to get wrong in the presence of silence
/// detection:</para>
///
/// <para><b>Only a region a person drew can be the target.</b> A silence region is the machine
/// saying "nothing here"; a marker is a claim about what was heard; a clip overlay is history.
/// Cutting any of them because it happened to be created last is a destructive edit on something
/// nobody chose.</para>
///
/// <para><b>One drawn region at a time.</b> That was already the design and is worth keeping — the
/// edit panel has room to name exactly one range — but it must be enforced against drawn regions
/// only. Enforcing it against everything is what deleted twenty silence regions and the person's
/// own selection along with them.</para>
/// </remarks>
public sealed class RegionSelection
{
    private WsRegionData? _target;

    /// <summary>The region an edit would act on, or null when nobody has drawn one.</summary>
    public WsRegionData? Target => _target;

    /// <summary>Whether a region-scoped edit (Cut, Silence) can be applied right now.</summary>
    public bool HasTarget => _target is not null;

    /// <summary>
    /// Takes account of a region that has just appeared, and says whether the caller should clear
    /// the other drawn regions to keep one-at-a-time.
    /// </summary>
    /// <returns>
    /// True when <paramref name="region"/> became the target. False when it was ignored, which is
    /// the answer for every kind except <see cref="RegionKind.User"/>.
    /// </returns>
    public bool Created(WsRegionData region)
    {
        if (region.Kind != RegionKind.User) return false;

        _target = region;
        return true;
    }

    /// <summary>
    /// Keeps the target's bounds in step when the region is dragged or resized.
    /// </summary>
    /// <remarks>
    /// Without this the target held the bounds from the moment it was first drawn, so an edit used
    /// the original range rather than what was on screen — and the readout in the edit panel
    /// disagreed with the waveform it sat under.
    /// </remarks>
    public void Updated(WsRegionData region)
    {
        if (_target?.Id == region.Id) _target = region;
    }

    /// <summary>Forgets the target when the region behind it goes away.</summary>
    /// <remarks>
    /// A stale target is worse than none: the panel still offers Cut, and the range it would cut is
    /// one the person can no longer see.
    /// </remarks>
    public void Removed(string regionId)
    {
        if (_target?.Id == regionId) _target = null;
    }

    /// <summary>Forgets the target outright — the Clear Regions button, and closing the editor.</summary>
    public void Clear() => _target = null;
}
