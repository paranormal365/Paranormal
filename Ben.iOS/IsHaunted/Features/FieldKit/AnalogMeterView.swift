import SwiftUI
import BenKit

/// An analog panel meter: a swept scale, tick marks, a needle, and the two set points that
/// matter — where "normal" was measured, and where a reading becomes worth reporting.
///
/// The needle shows the DEPARTURE from base, not the absolute field, because the absolute
/// number is meaningless on its own: the Earth alone reads around 500 mG and every building
/// bends that. Once somebody sets a base, "how far from normal" is the whole question, so it is
/// what the instrument answers. Before a base is set the dial says so rather than sweeping a
/// scale nobody can interpret.
///
/// The needle is damped rather than snapping to each sample. A real meter's movement has mass;
/// more usefully, a needle that jitters at 10 Hz is unreadable in the dark, and reading it is
/// the entire point.
struct AnalogMeterView: View {

    /// What the needle points at, in the dial's units.
    var value: Double?
    /// Half the dial's span: the scale runs −range…+range.
    var range: Double
    /// Where the report level sits, in the same units. Beyond it, the arc is warning-coloured.
    var reportAt: Double
    var unit: String
    /// The absolute reading, shown as a number under the dial.
    var absoluteText: String?
    var caption: String?
    /// Nil base level: the dial explains itself instead of pretending to measure.
    var hasBaseline: Bool

    private let sweep: Double = 240   // degrees, a generous panel-meter arc

    var body: some View {
        VStack(spacing: 8) {
            ZStack {
                dialFace
                if hasBaseline, let value {
                    needle(for: value)
                }
            }
            .frame(maxWidth: 320)
            .aspectRatio(1.3, contentMode: .fit)
            .animation(.interpolatingSpring(stiffness: 90, damping: 14), value: value)

            // Below the dial, not inside it. A readout drawn over the pivot sits on top of the
            // needle exactly when the needle is doing something worth reading.
            readout

            if let caption {
                Text(caption)
                    .font(.caption)
                    .foregroundStyle(Theme.fog)
                    .multilineTextAlignment(.center)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Magnetic field")
        .accessibilityValue(accessibilityValue)
    }

    private var accessibilityValue: String {
        guard hasBaseline, let value else {
            return absoluteText.map { "\($0), no base level set" } ?? "No reading"
        }
        let direction = value >= 0 ? "above" : "below"
        return String(format: "%.0f %@ %@ base", abs(value), unit, direction)
    }

    // MARK: - Face

    private var dialFace: some View {
        GeometryReader { geometry in
            let size = min(geometry.size.width, geometry.size.height * 1.3)
            let centre = CGPoint(x: geometry.size.width / 2, y: geometry.size.height * 0.78)
            let radius = size * 0.44

            ZStack {
                // The safe band, then the two shoulders where a reading is worth reporting.
                arc(centre: centre, radius: radius,
                    from: fraction(for: -reportAt), to: fraction(for: reportAt))
                    .stroke(Theme.ecto.opacity(0.55), style: .init(lineWidth: 10, lineCap: .round))
                arc(centre: centre, radius: radius, from: 0, to: fraction(for: -reportAt))
                    .stroke(Theme.warning.opacity(0.5), style: .init(lineWidth: 10, lineCap: .round))
                arc(centre: centre, radius: radius, from: fraction(for: reportAt), to: 1)
                    .stroke(Theme.warning.opacity(0.5), style: .init(lineWidth: 10, lineCap: .round))

                ticks(centre: centre, radius: radius)
                tickLabels(centre: centre, radius: radius)

                // The base level sits at dead centre by definition — that is what setting it
                // means — so the mark is drawn where the needle rests when nothing is happening.
                marker(centre: centre, radius: radius, at: 0, color: Theme.bone)

                Circle()
                    .fill(Theme.bone)
                    .frame(width: 12, height: 12)
                    .position(centre)
            }
        }
    }

    private func fraction(for value: Double) -> Double {
        guard range > 0 else { return 0.5 }
        return ((value + range) / (range * 2)).clamped(to: 0...1)
    }

    private func angle(for fraction: Double) -> Angle {
        // 0 → left shoulder, 1 → right shoulder, with the sweep centred on straight up.
        .degrees(-90 - sweep / 2 + sweep * fraction)
    }

    private func arc(centre: CGPoint, radius: Double, from: Double, to: Double) -> Path {
        Path { path in
            path.addArc(center: centre, radius: radius,
                        startAngle: angle(for: from), endAngle: angle(for: to),
                        clockwise: false)
        }
    }

    private func ticks(centre: CGPoint, radius: Double) -> some View {
        ForEach(0..<21, id: \.self) { index in
            let fraction = Double(index) / 20
            let isMajor = index % 5 == 0
            Path { path in
                let a = angle(for: fraction).radians
                let outer = CGPoint(x: centre.x + cos(a) * (radius - 8),
                                    y: centre.y + sin(a) * (radius - 8))
                let inner = CGPoint(x: centre.x + cos(a) * (radius - (isMajor ? 24 : 16)),
                                    y: centre.y + sin(a) * (radius - (isMajor ? 24 : 16)))
                path.move(to: outer)
                path.addLine(to: inner)
            }
            .stroke(isMajor ? Theme.bone.opacity(0.8) : Theme.fog.opacity(0.5),
                    lineWidth: isMajor ? 2 : 1)
        }
    }

    private func tickLabels(centre: CGPoint, radius: Double) -> some View {
        ForEach([0.0, 0.25, 0.5, 0.75, 1.0], id: \.self) { fraction in
            let labelValue = -range + range * 2 * fraction
            let a = angle(for: fraction).radians
            Text(labelValue == 0 ? "0" : String(format: "%+.0f", labelValue))
                .font(.system(size: 11, weight: .medium, design: .rounded))
                .foregroundStyle(Theme.fog)
                .position(x: centre.x + cos(a) * (radius - 40),
                          y: centre.y + sin(a) * (radius - 40))
        }
    }

    private func marker(centre: CGPoint, radius: Double, at value: Double, color: Color) -> some View {
        Path { path in
            let a = angle(for: fraction(for: value)).radians
            path.move(to: CGPoint(x: centre.x + cos(a) * (radius + 6),
                                  y: centre.y + sin(a) * (radius + 6)))
            path.addLine(to: CGPoint(x: centre.x + cos(a) * (radius - 4),
                                     y: centre.y + sin(a) * (radius - 4)))
        }
        .stroke(color, lineWidth: 3)
    }

    private func needle(for value: Double) -> some View {
        GeometryReader { geometry in
            let size = min(geometry.size.width, geometry.size.height * 1.3)
            let centre = CGPoint(x: geometry.size.width / 2, y: geometry.size.height * 0.78)
            let radius = size * 0.44
            let a = angle(for: fraction(for: value)).radians
            let pegged = abs(value) > range

            Path { path in
                path.move(to: CGPoint(x: centre.x - cos(a) * 14, y: centre.y - sin(a) * 14))
                path.addLine(to: CGPoint(x: centre.x + cos(a) * (radius - 12),
                                         y: centre.y + sin(a) * (radius - 12)))
            }
            // A needle at the stop is a reading whose real value is unknown, and it says so by
            // colour rather than quietly resting at the maximum.
            .stroke(pegged ? Theme.danger : Theme.ecto,
                    style: .init(lineWidth: 3, lineCap: .round))
        }
    }

    private var readout: some View {
        VStack(spacing: 2) {
            if hasBaseline, let value {
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    Text(String(format: "%+.0f", value))
                        .font(.system(size: 40, weight: .semibold, design: .rounded))
                        .monospacedDigit()
                    Text(unit)
                        .font(.headline)
                }
                .foregroundStyle(abs(value) >= reportAt ? Theme.warning : Theme.bone)
                Text("from base")
                    .font(.caption2)
                    .foregroundStyle(Theme.fog)
            } else {
                Image(systemName: "target")
                    .font(.title2)
                    .foregroundStyle(Theme.fog)
                Text("Set a base level")
                    .font(.caption)
                    .foregroundStyle(Theme.fog)
            }
            if let absoluteText {
                Text(absoluteText)
                    .font(.caption2)
                    .monospacedDigit()
                    .foregroundStyle(Theme.fog)
                    .padding(.top, 2)
            }
        }
    }
}

extension Comparable {
    func clamped(to limits: ClosedRange<Self>) -> Self {
        min(max(self, limits.lowerBound), limits.upperBound)
    }
}
