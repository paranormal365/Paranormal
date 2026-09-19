import Foundation
import AVFoundation

/// Cuts a recording down to the stretch worth sending, without touching the original (item 210).
///
/// **The original is never opened for writing.** Every path here reads the file on the phone and
/// writes a new one into a scratch directory that the upload deletes afterwards. That is the whole
/// promise the screen makes — *the full recording stays on this phone* — and it is kept by
/// construction rather than by remembering not to.
///
/// **Passthrough, not a re-encode.** `AVAssetExportPresetPassthrough` copies the sample data and
/// rebuilds the container around the chosen range, so a twelve-minute cut of an hour costs a file
/// copy rather than twelve minutes of CPU on a phone that has been recording all night — and,
/// more to the point for evidence, the audio and video are not degraded. Where passthrough cannot
/// serve the container, the cut is abandoned and the whole file is sent.
///
/// **Every failure sends the whole file.** No usable asset, an unsupported container, a failed
/// export, an empty output: all return nil, and the caller uploads the original. Losing a
/// recording to a failed trim would be far worse than uploading more than was asked for, and the
/// person is told which files were sent whole.
public struct SessionMediaTrimmer: Sendable {

    public init() {}

    /// What a trim attempt produced.
    public enum Outcome: Sendable, Equatable {
        /// A cut copy, at this URL. The caller deletes it after the upload.
        case cut(URL)
        /// Nothing was cut, and the original should be sent as it stands.
        case sendOriginal(reason: String)
    }

    /// Cuts `original` to `duration` seconds starting `from` seconds in.
    ///
    /// - Parameter scratch: a directory the caller owns and clears. Never the session's own
    ///   media directory — a cut written beside the original is one rename away from replacing it.
    public func cut(_ original: URL,
                    from start: TimeInterval,
                    duration: TimeInterval,
                    into scratch: URL) async -> Outcome {
        guard duration > 0 else { return .sendOriginal(reason: "the window keeps none of it") }
        guard FileManager.default.fileExists(atPath: original.path) else {
            return .sendOriginal(reason: "the file is no longer on this phone")
        }

        let asset = AVURLAsset(url: original)

        // Asked of the asset rather than assumed from the extension: a file that will not load is
        // one the export would fail on anyway, and finding out here gives a reason worth showing.
        guard let assetDuration = try? await asset.load(.duration), assetDuration.seconds > 0 else {
            return .sendOriginal(reason: "this recording's length could not be read")
        }

        // Clamped to what the file actually holds. A window computed from a duration that has
        // since changed — a file replaced, a length mis-measured — would otherwise ask for a range
        // past the end and fail the export outright.
        let from = max(0, min(start, assetDuration.seconds))
        let keep = min(duration, assetDuration.seconds - from)
        guard keep > 0 else { return .sendOriginal(reason: "the window falls outside this recording") }

        guard let session = AVAssetExportSession(
            asset: asset, presetName: AVAssetExportPresetPassthrough) else {
            return .sendOriginal(reason: "this recording's format cannot be cut on this device")
        }

        let fileType = Self.outputType(for: session, matching: original)
        guard let fileType else {
            return .sendOriginal(reason: "this recording's format cannot be cut on this device")
        }

        try? FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
        let destination = scratch.appendingPathComponent(
            "cut-\(UUID().uuidString.lowercased())\(original.pathExtension.isEmpty ? "" : "." + original.pathExtension)")
        try? FileManager.default.removeItem(at: destination)

        let scale = CMTimeScale(600)
        let range = CMTimeRange(
            start: CMTime(seconds: from, preferredTimescale: scale),
            duration: CMTime(seconds: keep, preferredTimescale: scale))

        // The range is set on the session rather than passed to the export call; the newer
        // export(to:as:) takes no range of its own.
        session.timeRange = range

        do {
            // The modern export call reports failure by throwing, so a silent zero-byte result
            // cannot be mistaken for a success — which is how the older status-polling API let a
            // failed export through as an empty file.
            try await session.export(to: destination, as: fileType)
        } catch {
            try? FileManager.default.removeItem(at: destination)
            return .sendOriginal(reason: "this recording could not be cut")
        }

        let size = (try? FileManager.default.attributesOfItem(atPath: destination.path))?[.size] as? Int64
        guard (size ?? 0) > 0 else {
            try? FileManager.default.removeItem(at: destination)
            return .sendOriginal(reason: "cutting this recording produced nothing")
        }

        return .cut(destination)
    }

    /// The output container, preferring the one the original already uses.
    ///
    /// Passthrough can only write the formats it lists, and writing an m4a's samples into a
    /// .mov would hand the server a file whose extension lies about it.
    static func outputType(for session: AVAssetExportSession, matching original: URL) -> AVFileType? {
        let supported = session.supportedFileTypes
        guard !supported.isEmpty else { return nil }

        let preferred: AVFileType? = switch original.pathExtension.lowercased() {
        case "m4a": .m4a
        case "mp4": .mp4
        case "mov": .mov
        case "caf": .caf
        case "wav": .wav
        default: nil
        }

        if let preferred, supported.contains(preferred) { return preferred }
        return supported.first
    }
}
