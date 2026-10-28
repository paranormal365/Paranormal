// What a moment of a field session shows — the browser's half of the shared replay rules.
//
// The rules are ProjectNotes/specs/replay-semantics.md; the ids in the comments (I1, P6, M6…) are
// its rows. The phone's replay (BenKit SessionReplay.swift) obeys the same table, and both are run
// against the same fixtures (Ben.iOS/BenKit/Tests/BenKitTests/Fixtures/replay-conformance): the
// phone by its own tests, this file by Ben.Web.Tests running it in Jint. So the web player and the
// Field Kit agree by test, not by hope.
//
// PURE. No DOM, no clock, no state: a timeline and a moment in, a frame out. The engine
// (fieldKitPlayer.js) keeps the clock and draws; this decides what is true at a moment. That split
// is what lets the arithmetic run under .NET with no browser.
//
// The timeline is the server's SessionTimeline as JSON (camelCase): times are whole milliseconds
// from the session's start, readings are columns.

/** How far before its first second a clip still covers the playhead (M6). */
export const LEAD_MS = 1000;
/** Longer than this between fixes and the marker holds rather than glides (P4). */
export const GLIDE_MS = 30000;
/** Fixes closer than this are drift, not a walk, for the track's bearing (P6). */
export const BEARING_METRES = 5;
/** How long "Motion detected" stays up (M8), and a photo glows (M9). */
export const SIGN_MS = 3000;
/** A marker this close to the playhead is the one highlighted (M10). */
export const NEAREST_MS = 1000;
/** Jumping to a marker lands this far before it (T4). */
export const LEAD_IN_MS = 3000;

/** The last index whose time is at or before ms, or -1 — binary search over a sorted column. */
export function lastAtOrBefore(times, ms) {
    let low = 0, high = times.length - 1, best = -1;
    while (low <= high) {
        const mid = (low + high) >> 1;
        if (times[mid] <= ms) { best = mid; low = mid + 1; } else { high = mid - 1; }
    }
    return best;
}

/** The fixes (readings with both coordinates), as indexes into the readings, worked out once. */
export function fixesOf(timeline) {
    if (timeline._fixes) return timeline._fixes;
    const r = timeline.readings, fixes = [];
    for (let i = 0; i < r.t.length; i++) if (r.lat[i] != null && r.lon[i] != null) fixes.push(i);
    // Cached on the object: the engine asks sixty times a second.
    Object.defineProperty(timeline, "_fixes", { value: fixes, enumerable: false });
    Object.defineProperty(timeline, "_fixTimes", { value: fixes.map(i => r.t[i]), enumerable: false });
    return fixes;
}

/** Equirectangular distance in metres — plenty for "did they leave the spot". */
export function metres(lat1, lon1, lat2, lon2) {
    const earth = 6371000;
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180 * Math.cos((lat1 + lat2) / 2 * Math.PI / 180);
    return earth * Math.sqrt(dLat * dLat + dLon * dLon);
}

/** Initial bearing from one point to another, clockwise from north, 0 to 360. */
export function bearing(lat1, lon1, lat2, lon2) {
    const p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
    const dl = (lon2 - lon1) * Math.PI / 180;
    const y = Math.sin(dl) * Math.cos(p2);
    const x = Math.cos(p1) * Math.sin(p2) - Math.sin(p1) * Math.cos(p2) * Math.cos(dl);
    return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
}

/**
 * Where they were (P1–P5): between the fixes either side, held across a long gap, clamped outside
 * the track. Null when nothing was ever located.
 */
export function positionAt(timeline, ms) {
    const fixes = fixesOf(timeline), times = timeline._fixTimes, r = timeline.readings;
    if (fixes.length === 0) return null;
    const b = lastAtOrBefore(times, ms);
    const at = i => ({ lat: r.lat[i], lon: r.lon[i], accuracy: r.accuracy[i] ?? null });
    if (b < 0) return at(fixes[0]);                         // P3: before the first fix
    if (b === fixes.length - 1) return at(fixes[b]);        // P3: after the last
    const before = fixes[b], after = fixes[b + 1];
    const span = r.t[after] - r.t[before];
    if (span <= 0 || span > GLIDE_MS) return at(before);    // P4
    const f = (ms - r.t[before]) / span;
    const accA = r.accuracy[before], accB = r.accuracy[after];
    return {                                                // P2, P5
        lat: r.lat[before] + (r.lat[after] - r.lat[before]) * f,
        lon: r.lon[before] + (r.lon[after] - r.lon[before]) * f,
        accuracy: accA == null ? (accB ?? null) : accB == null ? accA : Math.max(accA, accB),
    };
}

/** The bearing from the fix before the moment to the fix after it, when that is a walk (P6). */
export function trackBearingAt(timeline, ms) {
    const fixes = fixesOf(timeline), times = timeline._fixTimes, r = timeline.readings;
    const b = lastAtOrBefore(times, ms);
    if (b < 0 || b + 1 >= fixes.length) return null;
    const before = fixes[b], after = fixes[b + 1];
    const span = r.t[after] - r.t[before];
    if (span <= 0 || span > GLIDE_MS) return null;
    if (metres(r.lat[before], r.lon[before], r.lat[after], r.lon[after]) < BEARING_METRES) return null;
    return bearing(r.lat[before], r.lon[before], r.lat[after], r.lon[after]);
}

/**
 * The clip of one kind covering a moment (M6): the latest to begin, counted from a second before
 * its start. A clip whose start is not known yet is not placed; one whose length is not known
 * covers from its start on until the browser learns it.
 */
export function coveringClip(timeline, kind, ms, broken) {
    let best = null;
    for (const clip of timeline.clips) {
        if (clip.kind !== kind || clip.startMs == null) continue;
        // A recording this browser cannot play never hides one it can (10/28/2026).
        if (broken?.has(clip.fileId)) continue;
        if (ms < clip.startMs - LEAD_MS) continue;
        if (clip.durationMs != null && ms >= clip.startMs + clip.durationMs) continue;
        if (best === null || clip.startMs >= best.startMs) best = clip;
    }
    return best === null ? null : { clip: best, offsetMs: Math.max(0, ms - best.startMs) };
}

/**
 * Everything that is true at one moment. options.broken: the file ids of recordings this viewer
 * cannot play, left out of the covering choice; the phone has no such set and passes nothing.
 */
export function frameAt(timeline, ms, options) {
    const broken = options?.broken;
    const r = timeline.readings;
    const i = lastAtOrBefore(r.t, ms);                      // I1: held, not interpolated

    const emf = i >= 0 ? r.emf[i] ?? null : null;
    const emfBase = i >= 0 ? r.emfBase[i] ?? null : null;   // I3
    const base = emfBase ?? timeline.fallbackEmfBase ?? null;
    const heading = i >= 0 ? r.heading[i] ?? null : null;
    const course = i >= 0 ? r.course[i] ?? null : null;

    let facing = null;                                      // P6
    if (heading != null) facing = { kind: "looking", deg: heading };
    else if (course != null) facing = { kind: "walking", deg: course };
    else {
        const track = trackBearingAt(timeline, ms);
        if (track != null) facing = { kind: "moving", deg: track };
    }

    let motion = null;                                      // M8: the latest within the sign's time
    let nearest = null;                                     // M10: the first within a second
    for (const marker of timeline.markers) {
        if (marker.kind === "scene_motion" && marker.t <= ms && ms - marker.t < SIGN_MS) motion = marker;
        if (nearest === null && Math.abs(marker.t - ms) < NEAREST_MS) nearest = marker;
    }

    let glowing = null;                                     // M9
    for (const photo of timeline.photos)
        if (photo.t <= ms && ms - photo.t < SIGN_MS) glowing = photo;

    const roomIndex = i >= 0 ? r.room[i] : null;            // I5
    return {
        ms,
        index: i,
        emf,
        emfBase,
        fieldMg: emf != null && base != null ? (emf - base) * 10 : null,   // I2
        sound: i >= 0 ? r.sound[i] ?? null : null,                        // I4
        room: roomIndex != null ? timeline.rooms[roomIndex] ?? null : null,
        position: positionAt(timeline, ms),
        facing,
        audio: coveringClip(timeline, "audio", ms, broken),
        video: coveringClip(timeline, "video", ms, broken),
        motion,
        glowingPhoto: glowing,
        nearestMarker: nearest,
    };
}

/** Where jumping to a marker lands: a little before it, never before the start (T4). */
export function markerJump(markerMs) {
    return Math.max(0, markerMs - LEAD_IN_MS);
}

/**
 * The mark Next or Previous goes to (T4). Parked before a mark — just jumped or stepped to it, so
 * the playhead sits LEAD_IN_MS before it — the step is counted from that mark, so Next does not
 * pick it again. Anywhere else it is counted from the playhead itself: Next is the first mark
 * still ahead, Previous the last one already passed.
 *
 * Until 10/28/2026 every playhead was treated as parked, so while playing, Next skipped a mark
 * under three seconds ahead and Previous could choose one that had not happened yet.
 *
 * parkedOn: the mark the caller last jumped or stepped to, when it knows. Without it, a playhead
 * exactly at a mark's jump point counts as parked on that mark. Marks in the first three seconds
 * all jump to 0:00 and cannot be told apart that way, which is what parkedOn is for.
 * Null when there is nothing further that way.
 */
export function stepMarkerTarget(timeline, playheadMs, forward, parkedOn) {
    const markers = timeline.markers;
    const near = (a, b) => Math.abs(a - b) <= 10;
    let current = playheadMs;
    if (parkedOn != null && near(markerJump(parkedOn), playheadMs)) {
        current = parkedOn;
    } else {
        const parked = markers.find(m => m.t >= LEAD_IN_MS && near(markerJump(m.t), playheadMs));
        if (parked) current = parked.t;
    }
    if (forward) {
        for (const m of markers) if (m.t > current + 10) return m;
    } else {
        for (let k = markers.length - 1; k >= 0; k--) if (markers[k].t < current - 10) return markers[k];
    }
    return null;
}

/** Where Next or Previous lands (T4), or null. */
export function stepMarker(timeline, playheadMs, forward, parkedOn) {
    const marker = stepMarkerTarget(timeline, playheadMs, forward, parkedOn);
    return marker === null ? null : markerJump(marker.t);
}
