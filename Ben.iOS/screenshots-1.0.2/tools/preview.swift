import Foundation
import AVFoundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

// preview probe <in>                         -> prints duration and size
// preview poster <in> <seconds> <out.jpg>    -> one frame, for checking the cut
// preview cut <in> <out.mp4> <w> <h> <start> <length>
//   Scales to fill w×h (centre-cropped), H.264, video only, from <start> for <length> seconds.
//   Apple's App Preview specs: iPhone 6.5" 886×1920, iPad 13" 1200×1600, 15–30 s.
let args = CommandLine.arguments
func fail(_ m: String) -> Never { FileHandle.standardError.write((m + "\n").data(using: .utf8)!); exit(1) }
guard args.count >= 3 else { fail("usage: probe|poster|cut …") }
let asset = AVURLAsset(url: URL(fileURLWithPath: args[2]))
let sem = DispatchSemaphore(value: 0)
var exitCode: Int32 = 0

Task {
    do {
        let duration = try await asset.load(.duration).seconds
        guard let track = try await asset.loadTracks(withMediaType: .video).first else { fail("no video track") }
        let size = try await track.load(.naturalSize)
        let transform = try await track.load(.preferredTransform)
        let fps = try await track.load(.nominalFrameRate)
        let oriented = size.applying(transform)
        let srcW = abs(oriented.width), srcH = abs(oriented.height)

        switch args[1] {
        case "probe":
            print(String(format: "duration %.2fs  size %.0fx%.0f  fps %.1f", duration, srcW, srcH, fps))
        case "poster":
            let at = Double(args[3])!, out = URL(fileURLWithPath: args[4])
            let gen = AVAssetImageGenerator(asset: asset); gen.appliesPreferredTrackTransform = true
            gen.requestedTimeToleranceBefore = .zero; gen.requestedTimeToleranceAfter = .zero
            let (img, _) = try await gen.image(at: CMTime(seconds: at, preferredTimescale: 600))
            let dest = CGImageDestinationCreateWithURL(out as CFURL, UTType.jpeg.identifier as CFString, 1, nil)!
            CGImageDestinationAddImage(dest, img, [kCGImageDestinationLossyCompressionQuality: 0.85] as CFDictionary)
            CGImageDestinationFinalize(dest); print("poster \(out.path)")
        case "cut":
            let out = URL(fileURLWithPath: args[3]); let w = Double(args[4])!, h = Double(args[5])!
            let start = Double(args[6])!, length = Double(args[7])!
            try? FileManager.default.removeItem(at: out)
            let scale = max(w / srcW, h / srcH)               // fill, then crop the excess
            let tx = (w - srcW * scale) / 2, ty = (h - srcH * scale) / 2
            let comp = AVMutableVideoComposition()
            comp.renderSize = CGSize(width: w, height: h)
            comp.frameDuration = CMTime(value: 1, timescale: 30)
            let instr = AVMutableVideoCompositionInstruction()
            instr.timeRange = CMTimeRange(start: .zero, duration: try await asset.load(.duration))
            let layer = AVMutableVideoCompositionLayerInstruction(assetTrack: track)
            layer.setTransform(transform.concatenating(CGAffineTransform(scaleX: scale, y: scale))
                .concatenating(CGAffineTransform(translationX: tx, y: ty)), at: .zero)
            instr.layerInstructions = [layer]; comp.instructions = [instr]
            guard let export = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetHighestQuality) else { fail("no export session") }
            export.videoComposition = comp
            export.timeRange = CMTimeRange(start: CMTime(seconds: start, preferredTimescale: 600),
                                           duration: CMTime(seconds: length, preferredTimescale: 600))
            export.shouldOptimizeForNetworkUse = true
            try await export.export(to: out, as: .mp4)
            let probe = AVURLAsset(url: out)
            let d = try await probe.load(.duration).seconds
            let s = try await probe.loadTracks(withMediaType: .video).first!.load(.naturalSize)
            print(String(format: "wrote %@  %.2fs  %.0fx%.0f", out.path, d, s.width, s.height))
        default: fail("unknown command")
        }
    } catch { FileHandle.standardError.write("error: \(error)\n".data(using: .utf8)!); exitCode = 2 }
    sem.signal()
}
sem.wait(); exit(exitCode)
