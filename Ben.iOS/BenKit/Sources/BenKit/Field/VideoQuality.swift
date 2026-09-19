import Foundation

/// A smaller way to send the same footage.
///
/// **Why this exists rather than a refusal.** Ben, 2026-09-12: an upload that is too heavy should
/// offer a way through, not a locked door. There are exactly two honest ways to make a video
/// upload smaller — send less of it, or send it at a lower quality — and the trimmer already does
/// the first. This is the second, and it is offered only when the package is over the byte
/// ceiling, because degrading evidence nobody asked to degrade would be the wrong default.
///
/// **Nothing is done to the original.** The conversion writes a new file into the upload's scratch
/// directory, exactly as the trimmer does, and the phone keeps the footage it recorded at the
/// quality it recorded it.
public struct VideoQuality: Sendable, Equatable, Hashable, Identifiable {

    /// How many lines the sent copy has. `asRecorded` does not touch the picture at all.
    public enum Resolution: String, Sendable, Equatable, Hashable, CaseIterable {
        case asRecorded, hd720, hd1080

        /// The height it produces, or nil for "leave it alone".
        public var height: Int? {
            switch self {
            case .asRecorded: nil
            case .hd720: 720
            case .hd1080: 1080
            }
        }

        public var title: String {
            switch self {
            case .asRecorded: "As recorded"
            case .hd720: "720p"
            case .hd1080: "1080p"
            }
        }
    }

    /// How many frames a second the sent copy runs at.
    ///
    /// 29.97 is the NTSC broadcast rate and is genuinely 30000/1001, not 30 — it is offered
    /// because it was asked for, though converting a phone's 30 into it saves a tenth of a
    /// percent and is not worth the export. 24 is the one that actually saves anything.
    public enum FrameRate: String, Sendable, Equatable, Hashable, CaseIterable {
        case asRecorded, fps24, fps2997, fps30, fps60

        /// Frames per second as a number, or nil for "leave it alone".
        public var rate: Double? {
            switch self {
            case .asRecorded: nil
            case .fps24: 24
            case .fps2997: 30000.0 / 1001.0
            case .fps30: 30
            case .fps60: 60
            }
        }

        /// The exact rational the exporter needs. 29.97 written as 30 would drift a frame every
        /// thousand, which over a five-hour night is not a rounding error.
        public var frameDuration: (value: Int64, timescale: Int32)? {
            switch self {
            case .asRecorded: nil
            case .fps24: (1, 24)
            case .fps2997: (1001, 30000)
            case .fps30: (1, 30)
            case .fps60: (1, 60)
            }
        }

        public var title: String {
            switch self {
            case .asRecorded: "As recorded"
            case .fps24: "24 fps"
            case .fps2997: "29.97 fps"
            case .fps30: "30 fps"
            case .fps60: "60 fps"
            }
        }
    }

    public var resolution: Resolution
    public var frameRate: FrameRate

    public init(resolution: Resolution = .asRecorded, frameRate: FrameRate = .asRecorded) {
        self.resolution = resolution
        self.frameRate = frameRate
    }

    public var id: String { "\(resolution.rawValue)-\(frameRate.rawValue)" }

    /// Whether this would actually re-encode anything.
    public var changesAnything: Bool {
        resolution != .asRecorded || frameRate != .asRecorded
    }

    public var title: String {
        switch (resolution, frameRate) {
        case (.asRecorded, .asRecorded): "Send as recorded"
        case (.asRecorded, _):           frameRate.title
        case (_, .asRecorded):           resolution.title
        default:                          "\(resolution.title) · \(frameRate.title)"
        }
    }

    /// The choices offered, smallest first after the untouched original.
    ///
    /// Deliberately short. Every combination of three resolutions and five rates is fifteen rows
    /// of arithmetic nobody wants to do at midnight; these are the ones that change the answer.
    public static let offered: [VideoQuality] = [
        VideoQuality(),
        VideoQuality(resolution: .hd1080, frameRate: .asRecorded),
        VideoQuality(resolution: .hd1080, frameRate: .fps24),
        VideoQuality(resolution: .hd720,  frameRate: .asRecorded),
        VideoQuality(resolution: .hd720,  frameRate: .fps24),
    ]

    /// Roughly what a file of `bytes` becomes at this quality.
    ///
    /// Bitrate tracks pixels per second, near enough, so the estimate is the product of the two
    /// ratios. It is an estimate and every screen that shows it says "about": a scene that is
    /// mostly a dark, still room compresses far better than the ratio suggests, which means the
    /// real file is usually SMALLER than this — the safe direction to be wrong in.
    ///
    /// Upscaling is never counted as a saving: a 720p original sent "at 1080p" is the same file
    /// with more pixels of nothing, so anything that would grow is reported unchanged.
    public func approximateBytes(from bytes: Int64,
                                 sourceHeight: Int?,
                                 sourceFrameRate: Double?) -> Int64 {
        guard bytes > 0 else { return 0 }

        var factor = 1.0

        if let target = resolution.height, let source = sourceHeight, source > 0 {
            // Area, not height: half the lines is a quarter of the pixels.
            let ratio = Double(target) / Double(source)
            factor *= min(1, ratio * ratio)
        }

        if let target = frameRate.rate, let source = sourceFrameRate, source > 0 {
            factor *= min(1, target / source)
        }

        return Int64((Double(bytes) * factor).rounded())
    }
}
