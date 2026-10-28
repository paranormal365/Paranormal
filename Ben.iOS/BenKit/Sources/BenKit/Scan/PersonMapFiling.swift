import Foundation

/// What the person map saw in one video, filed in the session (LiDAR plan, phase 4).
///
/// A clip's figures and shapes are written into a folder of the clip's own while it records; when
/// the video is filed in the session they follow it into the session's `figures/` folder, numbered
/// on from whatever is already there, and the readings name them by those paths.
public enum PersonMapFiling {

    /// A figure as filed: its joints file and picture by their paths in the session.
    public struct Figure: Sendable, Equatable {
        public var file: String
        public var picture: String?
        public var from: Double
        public var to: Double
        public var startedAt: Date
        public var peakConfidence: Double
    }

    /// A shape as filed.
    public struct Shape: Sendable, Equatable {
        public var sighting: ShapeSighting
        public var picture: String?
    }

    public static let folder = "figures"

    /// Moves a clip's figures and shapes into the session's folder and clears the clip's own.
    /// The video is the caller's to file; this never touches it.
    public static func move(_ clip: PersonMapPipeline.Clip, into sessionFolder: URL) -> (figures: [Figure], shapes: [Shape]) {
        let manager = FileManager.default
        let destination = sessionFolder.appendingPathComponent(folder, isDirectory: true)
        try? manager.createDirectory(at: destination, withIntermediateDirectories: true)
        defer {
            // Everything worth keeping has moved; the video is already out.
            if (try? manager.contentsOfDirectory(atPath: clip.folder.path))?.allSatisfy({ !$0.hasSuffix(".mov") }) ?? true {
                try? manager.removeItem(at: clip.folder)
            }
        }

        var figures: [Figure] = []
        for sighting in clip.figures.sorted(by: { $0.from < $1.from }) {
            let name = nextName(in: destination, stem: "figure", ext: "json")
            guard (try? manager.moveItem(at: sighting.file, to: destination.appendingPathComponent(name))) != nil else { continue }
            var picture: String?
            if let source = sighting.picture {
                let pictureName = (name as NSString).deletingPathExtension + ".jpg"
                if (try? manager.moveItem(at: source, to: destination.appendingPathComponent(pictureName))) != nil {
                    picture = "\(folder)/\(pictureName)"
                }
            }
            figures.append(Figure(file: "\(folder)/\(name)", picture: picture, from: sighting.from, to: sighting.to,
                                  startedAt: sighting.startedAt, peakConfidence: Double(sighting.peakConfidence)))
        }

        var shapes: [Shape] = []
        for sighting in clip.shapes.sorted(by: { $0.from < $1.from }) {
            var picture: String?
            if let source = sighting.picture {
                let name = nextName(in: destination, stem: "shape", ext: "jpg")
                if (try? manager.moveItem(at: source, to: destination.appendingPathComponent(name))) != nil {
                    picture = "\(folder)/\(name)"
                }
            }
            shapes.append(Shape(sighting: sighting, picture: picture))
        }
        return (figures, shapes)
    }

    static func nextName(in directory: URL, stem: String, ext: String) -> String {
        let existing = Set((try? FileManager.default.contentsOfDirectory(atPath: directory.path)) ?? [])
        var index = 1
        while true {
            let name = String(format: "%@-%03d.%@", stem, index, ext)
            // A figure's picture shares its number, so a number is free only when both names are.
            let picture = String(format: "%@-%03d.jpg", stem, index)
            if !existing.contains(name), !existing.contains(picture) { return name }
            index += 1
        }
    }

    /// The figures a session's readings name, for drawing over its videos: each with its video,
    /// when in it, and its joints file.
    public static func figures(in readings: [FieldReading]) -> [Sighting] {
        readings.compactMap { reading in
            guard let figure = reading.measurements?["figure"], let file = figure.value.string,
                  let video = figure.details["video"]?.string,
                  let from = figure.details["from"]?.number, let to = figure.details["to"]?.number else { return nil }
            return Sighting(file: file, video: video, from: from, to: to, at: reading.at)
        }
        .sorted { $0.at < $1.at }
    }

    /// A figure as a session's readings name it.
    public struct Sighting: Sendable, Equatable {
        public var file: String
        public var video: String
        public var from: Double
        public var to: Double
        public var at: Date
    }
}
