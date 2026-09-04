import SwiftUI
import BenKit

/// A segmented sound-level meter, the way a mixing desk draws one.
///
/// Scale is dBFS — decibels relative to full scale — so silence is around −60 and 0 is the
/// loudest the hardware can represent. The base level and the report level are drawn on the
/// scale, because "is that loud?" is only answerable against what the room was doing before.
struct AudioLevelMeter: View {
    var dbfs: Double?
    var peakDbfs: Double?
    var baselineDbfs: Double?
    var reportAtDb: Double

    private let floor: Double = -60

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Label("Sound", systemImage: "waveform")
                    .font(.caption).foregroundStyle(Theme.fog)
                Spacer()
                Text(dbfs.map { String(format: "%.0f dB", $0) } ?? "—")
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(Theme.bone)
            }

            GeometryReader { geometry in
                let width = geometry.size.width
                ZStack(alignment: .leading) {
                    Capsule().fill(Theme.mist)

                    Capsule()
                        .fill(barColour)
                        .frame(width: width * fraction(dbfs))

                    // Peak, held a moment — a transient that never shows on an average meter is
                    // exactly the knock somebody is listening for.
                    if let peakDbfs {
                        Rectangle()
                            .fill(Theme.bone.opacity(0.9))
                            .frame(width: 2)
                            .offset(x: max(0, width * fraction(peakDbfs) - 1))
                    }

                    if let baselineDbfs {
                        marker(at: width * fraction(baselineDbfs), colour: Theme.bone)
                        marker(at: width * fraction(baselineDbfs + reportAtDb),
                               colour: Theme.warning)
                    }
                }
            }
            .frame(height: 14)

            if let baselineDbfs {
                Text(String(format: "base %.0f dB · reports at %+.0f dB",
                            baselineDbfs, reportAtDb))
                    .font(.caption2).foregroundStyle(Theme.fog)
            } else {
                Text("No base level set — sound won't be reported.")
                    .font(.caption2).foregroundStyle(Theme.fog)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Sound level")
        .accessibilityValue(dbfs.map { String(format: "%.0f decibels", $0) } ?? "no reading")
    }

    private var barColour: Color {
        guard let dbfs, let baselineDbfs else { return Theme.ecto }
        return dbfs - baselineDbfs >= reportAtDb ? Theme.warning : Theme.ecto
    }

    private func fraction(_ value: Double?) -> Double {
        guard let value else { return 0 }
        return ((value - floor) / (0 - floor)).clamped(to: 0...1)
    }

    private func marker(at x: Double, colour: Color) -> some View {
        Rectangle()
            .fill(colour)
            .frame(width: 2)
            .offset(x: max(0, x - 1))
    }
}

/// The wall clock, and how long this session has been running.
///
/// Both are on screen at once for a reason: a marker is worth nothing later unless it can be
/// matched to something — a note somebody wrote, another device's log, the time a door was
/// heard. `TimelineView` redraws these without a timer object of our own.
struct SessionClock: View {
    var startedAt: Date
    var isRecording: Bool
    /// Open on the live screen but not yet started (item 215). The elapsed counter holds at
    /// zero — `startedAt` is still the creation time and would otherwise count set-up time as
    /// if it were the session — and the caption says "not started" rather than "stopped",
    /// which would claim something ran and ended.
    var isPending: Bool = false

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(context.date, format: .dateTime.hour().minute().second())
                        .font(.system(size: 30, weight: .semibold, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(Theme.bone)
                    Text(context.date, format: .dateTime.weekday(.abbreviated)
                            .month(.abbreviated).day())
                        .font(.caption2)
                        .foregroundStyle(Theme.fog)
                }

                Spacer()

                VStack(alignment: .trailing, spacing: 1) {
                    HStack(spacing: 5) {
                        if isRecording {
                            Circle().fill(Theme.danger).frame(width: 8, height: 8)
                                .accessibilityHidden(true)
                        }
                        Text(isPending ? "00:00:00" : Self.elapsed(from: startedAt, to: context.date))
                            .font(.system(size: 26, weight: .semibold, design: .rounded))
                            .monospacedDigit()
                            .foregroundStyle(isRecording ? Theme.danger : Theme.bone)
                    }
                    Text(isRecording ? "recording" : isPending ? "not started" : "stopped")
                        .font(.caption2)
                        .foregroundStyle(Theme.fog)
                }
            }
            .accessibilityElement(children: .combine)
            .accessibilityLabel(isRecording ? "Recording" : isPending ? "Not started" : "Stopped")
            .accessibilityValue(isPending ? "00:00:00" : Self.elapsed(from: startedAt, to: context.date))
        }
    }

    static func elapsed(from start: Date, to now: Date) -> String {
        let total = max(0, Int(now.timeIntervalSince(start)))
        return String(format: "%02d:%02d:%02d", total / 3600, (total % 3600) / 60, total % 60)
    }
}

/// Where you are, how high, and which way you are pointing.
///
/// Accuracy is shown, always. Indoors a fix is routinely 20–50 m out — the width of the whole
/// building — and a coordinate presented without that number invites somebody to believe it
/// means the room they are standing in.
struct PositionReadout: View {
    var sample: PositionSample?
    var headingDegrees: Double?
    var relativeAltitudeMeters: Double?
    var isEnabled: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Label("Position", systemImage: "location")
                    .font(.caption).foregroundStyle(Theme.fog)
                Spacer()
                if let headingDegrees {
                    Text("\(Self.compass(headingDegrees)) \(Int(headingDegrees))°")
                        .font(.caption.monospacedDigit()).foregroundStyle(Theme.bone)
                }
            }

            if !isEnabled {
                Text("Location is switched off for this session.")
                    .font(.caption2).foregroundStyle(Theme.fog)
            } else if let sample {
                Text(String(format: "%.5f, %.5f", sample.latitude, sample.longitude))
                    .font(.caption.monospacedDigit()).foregroundStyle(Theme.bone)
                HStack(spacing: 10) {
                    if let accuracy = sample.accuracyMeters {
                        Label(String(format: "±%.0f m", accuracy),
                              systemImage: accuracy > 20 ? "exclamationmark.circle" : "checkmark.circle")
                            .font(.caption2)
                            .foregroundStyle(accuracy > 20 ? Theme.warning : Theme.fog)
                    }
                    if let relativeAltitudeMeters {
                        Label(String(format: "%+.1f m", relativeAltitudeMeters),
                              systemImage: "arrow.up.and.down")
                            .font(.caption2).foregroundStyle(Theme.fog)
                    }
                }
            } else {
                Text("Waiting for a fix. Indoors this can take a while, or never come.")
                    .font(.caption2).foregroundStyle(Theme.fog)
            }
        }
    }

    static func compass(_ degrees: Double) -> String {
        let points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
        let index = Int(((degrees.truncatingRemainder(dividingBy: 360) + 360)
                         .truncatingRemainder(dividingBy: 360) / 45).rounded()) % 8
        return points[index]
    }
}
