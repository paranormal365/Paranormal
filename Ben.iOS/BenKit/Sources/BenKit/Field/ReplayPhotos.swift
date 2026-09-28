import Foundation

/// Photographs in the replay.
///
/// Ben, 2026-09-27: "the photo never shows either when you take it during a session. I would think
/// it would display when playing back the session... even on the phone." A photo is an instant,
/// not a stretch, so it never sat on the media timeline; it was only ever a pin on the map, and
/// only when it carried coordinates — which a photo taken indoors, where most of a night is spent,
/// usually does not. So a photograph taken in the cellar appeared nowhere at all.
///
/// Now they sit in a strip of thumbnails under the player. Ben, the same day: "the photos are in
/// small thumbnails that get highlighted when they were taken during the .ben playback or their
/// border glows a couple of seconds", and "maybe they grow a little for those three seconds".
public enum ReplayPhotos {
    /// How long a thumbnail glows, and is grown, after the playhead passes its moment.
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

    /// Photographs only, in the order they were taken — the strip. Anything else pinned as a still
    /// (a clip whose length could not be read) has no picture to show.
    public static func taken(_ stills: [CaptureMark]) -> [CaptureMark] {
        stills.filter { $0.kind == .photo }.sorted { $0.at < $1.at }
    }
}
