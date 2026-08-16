namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// One standalone audio clip participating in the export mix — item #70 phase 162.
///
/// <para>Carries the clip's <b>already-computed</b> filter chain rather than its raw properties.
/// That is deliberate: the chain is built by <c>ExportArgBuilders.BuildAudioClipFilterChain</c>
/// from volume automation, per-channel balance and fades, and duplicating that derivation
/// server-side would create exactly the kind of drift this arc has been eliminating everywhere
/// else. The browser derives it once; the sidecar runs it.</para>
///
/// <para><see cref="Start"/>/<see cref="End"/> are the clip's trim points in its own source
/// timebase. Timeline positioning is <b>already baked into <see cref="FilterChain"/></b> as an
/// <c>adelay</c>, which is why the amix step needs no offset math of its own.</para>
/// </summary>
public sealed record AudioMixClipDto(
    Guid ClipId,
    string SourceExt,
    double Start,
    double End,
    string FilterChain);

/// <summary>Audio half of an assemble request. Absent entirely when the timeline has no standalone
/// audio clips, in which case the job is concat-only.</summary>
public sealed record ExportAudioMixDto(IReadOnlyList<AudioMixClipDto> Clips);

/// <summary>
/// Assemble a finished export body from retained segments — item #70 phase 162.
///
/// <para>Runs the concat and (when <see cref="Audio"/> is present) the per-clip audio segments plus
/// the amix, as <b>one job producing one result</b>. Combining them matters: the intermediate
/// concat output is large and would otherwise have to be downloaded and re-uploaded between two
/// separate jobs, which would cost more than the offload saves.</para>
///
/// <para>The result is the export's video body <i>before</i> overlays. The browser resumes its
/// existing pipeline at the overlay step, which is safe because every overlay pass stream-copies
/// audio through untouched (<c>-map 0:a? -c:a copy</c>) — audited before this phase was built.</para>
/// </summary>
public sealed record ExportAssembleRequest(
    IReadOnlyList<Guid> SegmentIds,
    ExportQualityDto Quality,
    ExportAudioMixDto? Audio = null);
