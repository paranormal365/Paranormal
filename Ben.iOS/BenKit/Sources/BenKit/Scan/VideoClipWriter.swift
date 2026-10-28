import Foundation
import AVFoundation
import CoreVideo

/// A video written from frames handed to it one at a time (LiDAR plan, phase 4).
///
/// While the person map is on, ARKit has the camera and the session's own capture stands down,
/// so the session's video is written from ARKit's frames instead. Picture only, as the session's
/// video always is: the session's own recording keeps the sound running underneath it.
///
/// Used from one place at a time — the person map's pipeline — never shared.
public final class VideoClipWriter: @unchecked Sendable {
    public enum Problem: Error, LocalizedError {
        case cannotWrite
        case nothingWritten

        public var errorDescription: String? {
            switch self {
            case .cannotWrite: "This phone couldn't start writing the video."
            case .nothingWritten: "The camera sent no frames, so there is no video to keep."
            }
        }
    }

    public let url: URL
    public let turn: PictureTurn
    public let width: Int
    public let height: Int
    private let writer: AVAssetWriter
    private let input: AVAssetWriterInput
    private let adaptor: AVAssetWriterInputPixelBufferAdaptor
    /// The camera's clock at the first frame: the start of the video.
    public private(set) var startTime: Double?
    public private(set) var lastTime: Double?
    public private(set) var frames = 0

    /// - Parameters:
    ///   - width, height: the frames as the camera gives them.
    ///   - turn: how they are turned to stand upright, written as the video's own turn.
    public init(url: URL, width: Int, height: Int, turn: PictureTurn, averageBitRate: Int = 8_000_000) throws {
        try? FileManager.default.removeItem(at: url)
        writer = try AVAssetWriter(outputURL: url, fileType: .mov)
        input = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: width,
            AVVideoHeightKey: height,
            AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: averageBitRate],
        ])
        input.expectsMediaDataInRealTime = true
        input.transform = turn.transform(width: Double(width), height: Double(height))
        adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: input, sourcePixelBufferAttributes: nil)
        guard writer.canAdd(input) else { throw Problem.cannotWrite }
        writer.add(input)
        self.url = url
        self.turn = turn
        self.width = width
        self.height = height
    }

    /// Adds a frame taken at `seconds` on the camera's clock. A frame the encoder is not ready for
    /// is dropped rather than waited for: the camera does not wait either.
    @discardableResult
    public func append(_ buffer: CVPixelBuffer, at seconds: Double) -> Bool {
        let time = CMTime(seconds: seconds, preferredTimescale: 6000)
        if startTime == nil {
            guard writer.startWriting() else { return false }
            writer.startSession(atSourceTime: time)
            startTime = seconds
        }
        guard writer.status == .writing, input.isReadyForMoreMediaData,
              seconds > (lastTime ?? -.infinity) else { return false }
        guard adaptor.append(buffer, withPresentationTime: time) else { return false }
        lastTime = seconds
        frames += 1
        return true
    }

    /// Seconds into the video for a time on the camera's clock.
    public func videoTime(_ seconds: Double) -> Double? {
        startTime.map { seconds - $0 }
    }

    /// Closes the file and hands back where it is.
    public func finish() async throws -> URL {
        guard startTime != nil, frames > 0 else {
            if writer.status == .writing { writer.cancelWriting() }
            throw Problem.nothingWritten
        }
        input.markAsFinished()
        let writer = writer
        await withCheckedContinuation { (done: CheckedContinuation<Void, Never>) in
            writer.finishWriting { done.resume() }
        }
        if writer.status != .completed { throw writer.error ?? Problem.cannotWrite }
        return url
    }

    /// Gives up on the file.
    public func cancel() {
        if writer.status == .writing { writer.cancelWriting() }
        try? FileManager.default.removeItem(at: url)
    }
}
