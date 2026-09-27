import Foundation

/// Which photograph the replay shows at a moment.
///
/// Ben, 2026-09-27: "the photo never shows either when you take it during a session. I would think
/// it would display when playing back the session... even on the phone." A photo is an instant,
/// not a stretch, so it never sat on the media timeline; it was only ever a pin on the map, and
/// only when it carried coordinates — which a photo taken indoors, where most of a night is spent,
/// usually does not. So a photograph taken in the cellar appeared nowhere at all.
///
/// Now the playhead passing the moment a photo was taken puts it on screen, and it stays for
/// `holdSeconds` — long enough to be looked at at normal speed, short enough that it does not
/// stand in for a stretch of the night it was not part of.
public enum ReplayPhotos {
    public static let holdSeconds: TimeInterval = 8

    /// How long a thumbnail's border glows after the playhead passes the moment it was taken.
    ///
    /// Ben, 2026-09-27: "the photos are in small thumbnails that get highlighted when they were
    /// taken during the .ben playback or their border glows a couple of seconds". Shorter than the
    /// hold: the glow marks the MOMENT, the big picture is for looking at it.
    public static let glowSeconds: TimeInterval = 3

    /// Whether this photograph's thumbnail glows at `moment`.
    public static func isGlowing(_ photo: CaptureMark, at moment: Date,
                                 glow: TimeInterval = glowSeconds) -> Bool {
        photo.kind == .photo && photo.at <= moment && moment.timeIntervalSince(photo.at) < glow
    }

    /// The last photograph whose moment the playhead has passed — the one the strip keeps in view.
    public static func lastPassed(_ stills: [CaptureMark], at moment: Date) -> CaptureMark? {
        taken(stills).last { $0.at <= moment }
    }

    /// The most recent photograph taken at or before `moment`, if it was taken within `hold`.
    /// Anything that is not a photo — a clip whose length could not be read, pinned as a still —
    /// is left out: it has no picture to show.
    public static func onScreen(_ stills: [CaptureMark], at moment: Date,
                                hold: TimeInterval = holdSeconds) -> CaptureMark? {
        stills
            .filter { $0.kind == .photo && $0.at <= moment && moment.timeIntervalSince($0.at) < hold }
            .max { $0.at < $1.at }
    }

    /// Photographs only, in the order they were taken — the strip under the replay.
    public static func taken(_ stills: [CaptureMark]) -> [CaptureMark] {
        stills.filter { $0.kind == .photo }.sorted { $0.at < $1.at }
    }
}
