import Foundation
import AVFoundation

/// Writes a smaller copy of a video for the upload, leaving the recording alone.
///
/// The sibling of ``SessionMediaTrimmer``, and it keeps that file's two promises. The original is
/// never opened for writing: this reads the recording and writes a new file into the scratch
/// directory the upload owns and clears. And every failure sends the original rather than
/// nothing — an upload that carried more than was asked for is a nuisance, and one that silently
/// dropped a night's footage is not.
///
/// Unlike the trimmer this DOES re-encode, because that is the whole point. It is therefore slow
/// and it is only ever reached when somebody has chosen it in answer to a package that is too
/// heavy to send.
public struct SessionVideoConverter: Sendable {

    public init() {}

    public enum Outcome: Sendable, Equatable {
        /// A smaller copy, at this URL. The caller deletes it after the upload.
        case converted(URL)
        /// Nothing was converted, and the original should be sent as it stands.
        case sendOriginal(reason: String)
    }

    /// What the source actually is, which is what makes an estimate worth showing.
    public struct SourceVideo: Sendable, Equatable {
        public var height: Int?
        public var frameRate: Double?
        public init(height: Int?, frameRate: Double?) {
            self.height = height
            self.frameRate = frameRate
        }
    }

    /// Reads the picture's shape without decoding it, for the size estimates on the upload screen.
    public func describe(_ url: URL) async -> SourceVideo {
        let asset = AVURLAsset(url: url)
        guard let track = try? await asset.loadTracks(withMediaType: .video).first else {
            return SourceVideo(height: nil, frameRate: nil)
        }
        let size = try? await track.load(.naturalSize)
        let transform = try? await track.load(.preferredTransform)
        let rate = try? await track.load(.nominalFrameRate)

        // The natural size is before rotation: a portrait clip reports a landscape size and a
        // transform that turns it. Taking the height off the untransformed size would call a
        // 1080-wide portrait clip "1920p" and estimate every conversion wrongly.
        let applied = (size ?? .zero).applying(transform ?? .identity)
        let height = Int(abs(applied.height).rounded())

        return SourceVideo(height: height > 0 ? height : nil,
                           frameRate: (rate.map(Double.init)).flatMap { $0 > 0 ? $0 : nil })
    }

    /// Writes `original` again at `quality`.
    ///
    /// - Parameter scratch: a directory the caller owns and clears. Never the session's own media
    ///   directory, for the same reason the trimmer never writes there.
    public func convert(_ original: URL,
                        to quality: VideoQuality,
                        into scratch: URL) async -> Outcome {
        guard quality.changesAnything else {
            return .sendOriginal(reason: "nothing was asked to change")
        }
        guard FileManager.default.fileExists(atPath: original.path) else {
            return .sendOriginal(reason: "the file is no longer on this phone")
        }

        let asset = AVURLAsset(url: original)
        guard let duration = try? await asset.load(.duration), duration.seconds > 0 else {
            return .sendOriginal(reason: "this recording's length could not be read")
        }
        guard (try? await asset.loadTracks(withMediaType: .video))?.isEmpty == false else {
            return .sendOriginal(reason: "this file holds no video to convert")
        }

        // A preset that the device cannot serve for this asset is not a failure to hide: it is
        // exactly the case where sending the original is right.
        let preset = Self.preset(for: quality.resolution)
        let compatible = await AVAssetExportSession.compatibility(ofExportPreset: preset,
                                                                  with: asset, outputFileType: .mp4)
        guard compatible, let session = AVAssetExportSession(asset: asset, presetName: preset) else {
            return .sendOriginal(reason: "this device cannot re-encode that recording")
        }

        if let frames = quality.frameRate.frameDuration,
           let composition = try? await AVMutableVideoComposition.videoComposition(withPropertiesOf: asset) {
            composition.frameDuration = CMTime(value: frames.value, timescale: frames.timescale)
            session.videoComposition = composition
        }

        try? FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
        let destination = scratch.appendingPathComponent(
            "smaller-\(UUID().uuidString.lowercased()).mp4")
        try? FileManager.default.removeItem(at: destination)

        do {
            try await session.export(to: destination, as: .mp4)
        } catch {
            try? FileManager.default.removeItem(at: destination)
            return .sendOriginal(reason: "this recording could not be re-encoded")
        }

        let size = (try? FileManager.default.attributesOfItem(atPath: destination.path))?[.size] as? Int64
        guard let size, size > 0 else {
            try? FileManager.default.removeItem(at: destination)
            return .sendOriginal(reason: "re-encoding this recording produced nothing")
        }

        // A "smaller" copy that came out bigger is a conversion that achieved the opposite of what
        // it was chosen for. Send the original and spend nothing.
        let originalSize = (try? FileManager.default.attributesOfItem(atPath: original.path))?[.size] as? Int64
        if let originalSize, size >= originalSize {
            try? FileManager.default.removeItem(at: destination)
            return .sendOriginal(reason: "re-encoding would not have made it smaller")
        }

        return .converted(destination)
    }

    static func preset(for resolution: VideoQuality.Resolution) -> String {
        switch resolution {
        case .hd720:  AVAssetExportPreset1280x720
        case .hd1080: AVAssetExportPreset1920x1080
        // Only the frame rate is changing, so the picture is asked for at the highest quality the
        // device will pass through — anything smaller would degrade it for no reason.
        case .asRecorded: AVAssetExportPresetHighestQuality
        }
    }
}
