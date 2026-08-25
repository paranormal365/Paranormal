import SwiftUI
import Charts
import MapKit
import BenKit

/// The session's readings as a trace, with the playhead and every marker on it.
///
/// Downsampled for drawing: a five-hour session is tens of thousands of readings and a chart
/// cannot show more points than the screen has pixels. The downsampling keeps the EXTREMES of
/// each bucket rather than averaging, because averaging is exactly what would erase the spike
/// somebody opened the session to look at.
struct ReadingsChart: View {
    let timeline: ReplayTimeline
    let playhead: Date
    var onScrub: (Date) -> Void

    private struct Point: Identifiable {
        let id = UUID()
        let at: Date
        let milligauss: Double
    }

    var body: some View {
        Chart {
            ForEach(points) { point in
                LineMark(x: .value("Time", point.at),
                         y: .value("mG from base", point.milligauss))
                    .foregroundStyle(Theme.ecto)
                    .interpolationMethod(.monotone)
            }

            ForEach(timeline.markers) { marker in
                RuleMark(x: .value("Marked", marker.at))
                    .foregroundStyle(marker.kind.isAutomatic
                                     ? Theme.warning.opacity(0.75) : Theme.haunt.opacity(0.75))
                    .lineStyle(.init(lineWidth: 1, dash: [3, 3]))
            }

            RuleMark(x: .value("Now", playhead))
                .foregroundStyle(Theme.bone)
                .lineStyle(.init(lineWidth: 2))
        }
        .chartYAxisLabel("mG from base")
        .chartXAxis {
            // Elapsed time on anything short: a ten-minute session labelled with the wall clock
            // reads "10:07 AM" four times over, which tells nobody anything. Longer sessions get
            // the clock back, because that is what correlates with somebody's notes.
            AxisMarks(values: .automatic(desiredCount: 4)) { value in
                AxisGridLine().foregroundStyle(Theme.fog.opacity(0.25))
                if let date = value.as(Date.self) {
                    AxisValueLabel {
                        Text(timeline.duration < 600
                             ? SessionClock.elapsed(from: timeline.startedAt, to: date)
                             : date.formatted(.dateTime.hour().minute()))
                            .font(.caption2)
                    }
                }
            }
        }
        .chartOverlay { proxy in
            GeometryReader { geometry in
                Rectangle().fill(.clear).contentShape(Rectangle())
                    .gesture(DragGesture(minimumDistance: 0).onChanged { value in
                        guard let plotFrame = proxy.plotFrame else { return }
                        let x = value.location.x - geometry[plotFrame].origin.x
                        if let date: Date = proxy.value(atX: x) { onScrub(date) }
                    })
            }
        }
        .frame(height: 180)
        .accessibilityLabel("Magnetic field over the session")
    }

    /// At most this many points reach the chart. Beyond it the line is slower to draw than it
    /// is to read.
    private static let maxPoints = 400

    private var points: [Point] {
        let readings = timeline.readings.compactMap { reading -> Point? in
            guard let field = reading.measurements?["emf"]?.numberValue,
                  let base = timeline.baselines.magneticMicrotesla
            else { return nil }
            return Point(at: reading.at, milligauss: (field - base) * 10)
        }
        guard readings.count > Self.maxPoints else { return readings }

        // Keep the highest and lowest of each bucket. Averaging would flatten the one moment
        // worth looking at into the noise around it.
        let bucketSize = Int(ceil(Double(readings.count) / Double(Self.maxPoints / 2)))
        var kept: [Point] = []
        for start in stride(from: 0, to: readings.count, by: bucketSize) {
            let bucket = readings[start..<min(start + bucketSize, readings.count)]
            guard let low = bucket.min(by: { $0.milligauss < $1.milligauss }),
                  let high = bucket.max(by: { $0.milligauss < $1.milligauss })
            else { continue }
            kept.append(contentsOf: low.at <= high.at ? [low, high] : [high, low])
        }
        return kept
    }
}

/// Where somebody walked, and where they were at the playhead.
///
/// The accuracy circle is drawn deliberately: indoors a fix is routinely tens of metres, and a
/// bare pin invites the belief that it means the room somebody was standing in.
struct MovementMap: View {
    let timeline: ReplayTimeline
    let frame: ReplayFrame
    let stills: [CaptureMark]

    @State private var camera: MapCameraPosition = .automatic

    var body: some View {
        map.overlay(alignment: .topLeading) { roomPlate }
    }

    /// The room, over the map, because it is the one thing on this screen a fix cannot tell
    /// you: the accuracy circle covers the whole building, the label says which part of it.
    @ViewBuilder
    private var roomPlate: some View {
        if let room = frame.room {
            Label(room, systemImage: "door.left.hand.open")
                .font(.caption.weight(.semibold))
                .padding(.horizontal, 8)
                .padding(.vertical, 5)
                .background(.thinMaterial, in: Capsule())
                .padding(8)
                .accessibilityIdentifier("replay-room")
        }
    }

    private var map: some View {
        Map(position: $camera) {
            if track.count > 1 {
                MapPolyline(coordinates: track)
                    .stroke(Theme.ecto.opacity(0.75), lineWidth: 3)
            }

            ForEach(stills) { still in
                if let coordinate = still.coordinate {
                    Annotation("", coordinate: coordinate) {
                        Image(systemName: "photo.fill")
                            .font(.caption2)
                            .padding(4)
                            .background(Theme.mist, in: Circle())
                            .foregroundStyle(Theme.bone)
                    }
                }
            }

            ForEach(markersWithPlaces) { marker in
                if let coordinate = marker.coordinate {
                    Annotation("", coordinate: coordinate) {
                        Image(systemName: marker.kind.isAutomatic ? "bolt.fill" : "flag.fill")
                            .font(.caption2)
                            .foregroundStyle(marker.kind.isAutomatic ? Theme.warning : Theme.haunt)
                    }
                }
            }

            if let here = frame.position?.coordinate {
                // The circle IS the honesty: it shows how much of the building this fix
                // actually covers.
                if let accuracy = frame.position?.accuracyMeters, accuracy > 0 {
                    MapCircle(center: here, radius: accuracy)
                        .foregroundStyle(Theme.ecto.opacity(0.12))
                        .stroke(Theme.ecto.opacity(0.4), lineWidth: 1)
                }
                Annotation("", coordinate: here) {
                    ZStack {
                        Circle().fill(Theme.ecto).frame(width: 14, height: 14)
                        if let heading = frame.headingDegrees {
                            Image(systemName: "location.north.fill")
                                .font(.system(size: 9))
                                .foregroundStyle(Theme.ink)
                                .rotationEffect(.degrees(heading))
                        }
                    }
                }
            }
        }
        .frame(height: 220)
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .overlay(alignment: .bottomLeading) {
            if track.isEmpty {
                Text("No position was recorded for this session.")
                    .font(.caption2)
                    .padding(8)
                    .background(Theme.mist.opacity(0.9), in: Capsule())
                    .foregroundStyle(Theme.fog)
                    .padding(8)
            }
        }
        .accessibilityLabel("Where this session went")
    }

    private var track: [CLLocationCoordinate2D] {
        timeline.track.compactMap { $0.position.coordinate }
    }

    private var markersWithPlaces: [FieldMarkerRecord] {
        timeline.markers.filter { $0.latitude != nil && $0.longitude != nil }
    }
}

extension FieldReading.Position {
    var coordinate: CLLocationCoordinate2D? {
        guard let latitude, let longitude else { return nil }
        return CLLocationCoordinate2D(latitude: latitude, longitude: longitude)
    }
}

extension FieldMarkerRecord {
    var coordinate: CLLocationCoordinate2D? {
        guard let latitude, let longitude else { return nil }
        return CLLocationCoordinate2D(latitude: latitude, longitude: longitude)
    }
}

extension CaptureMark {
    var coordinate: CLLocationCoordinate2D? {
        guard let latitude, let longitude else { return nil }
        return CLLocationCoordinate2D(latitude: latitude, longitude: longitude)
    }
}

/// Play, scrub, speed, and stepping between the moments somebody marked.
struct ReplayTransport: View {
    let replay: SessionReplay

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                Text(elapsedText).font(.caption.monospacedDigit()).foregroundStyle(Theme.fog)
                Spacer()
                Text(replay.playhead, format: .dateTime.hour().minute().second())
                    .font(.caption.monospacedDigit()).foregroundStyle(Theme.bone)
                Spacer()
                Text(totalText).font(.caption.monospacedDigit()).foregroundStyle(Theme.fog)
            }

            Slider(value: Binding(
                get: { replay.timeline.fraction(of: replay.playhead) },
                set: { replay.seek(fraction: $0) }), in: 0...1)
                .tint(Theme.ecto)
                .accessibilityIdentifier("replay-scrubber")

            HStack(spacing: 22) {
                Button { replay.stepMarker(forward: false) } label: {
                    Image(systemName: "backward.end.fill")
                }
                .accessibilityLabel("Previous marker")

                Button { replay.togglePlaying() } label: {
                    Image(systemName: replay.isPlaying ? "pause.circle.fill" : "play.circle.fill")
                        .font(.system(size: 40))
                }
                .accessibilityIdentifier("replay-play")
                .accessibilityLabel(replay.isPlaying ? "Pause" : "Play")

                Button { replay.stepMarker(forward: true) } label: {
                    Image(systemName: "forward.end.fill")
                }
                .accessibilityLabel("Next marker")

                Menu {
                    ForEach([1.0, 2.0, 4.0, 8.0, 16.0, 32.0], id: \.self) { rate in
                        Button("\(Int(rate))×") { replay.setRate(rate) }
                    }
                } label: {
                    Text("\(Int(replay.rate))×")
                        .font(.callout.monospacedDigit())
                        .padding(.horizontal, 10).padding(.vertical, 5)
                        .background(Theme.mist, in: Capsule())
                }
                .accessibilityIdentifier("replay-speed")
            }
            .foregroundStyle(Theme.ecto)
        }
    }

    private var elapsedText: String {
        SessionClock.elapsed(from: replay.timeline.startedAt, to: replay.playhead)
    }

    private var totalText: String {
        SessionClock.elapsed(from: replay.timeline.startedAt, to: replay.timeline.endedAt)
    }
}
