import SwiftUI
import BenKit

/// A video-trimmer's in and out points, for choosing what of a session to send (item 210).
///
/// Ben's spec, 2026-09-04: *"in point out point. Initially, the in point is the start and the out
/// point is the end. Scrolling in point adjusts where it starts and same with end point. Show
/// progress of inpoint when scrolling it and show progress of end point when scrolling it. The
/// part that is to be exported should be between them and obvious… the line between is bolder and
/// maybe a slightly different color and the start point is a green dot to scroll and the end point
/// is a red dot to scroll."*
///
/// **The arithmetic is not here.** Clamping a handle against its partner, a finger dragged off the
/// end of the track, and a session with no honest end time all live in `SessionTrimRange`, where
/// they can be tested without a simulator. This view converts touches into fractions and draws
/// the result.
struct SessionTrimSlider: View {
    @Binding var range: SessionTrimRange

    /// Which handle is under the finger, so its own time can be shown while it moves.
    @State private var dragging: Handle?

    private enum Handle { case start, end }

    /// Big enough to hit with a thumb in the dark, which is where this app is used.
    private let dotSize: CGFloat = 22
    private let trackHeight: CGFloat = 6
    private let keptHeight: CGFloat = 12

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            readouts
            track
            elapsedLabels
        }
    }

    // MARK: - The readouts

    /// The time under whichever handle is moving, or the chosen span when neither is.
    ///
    /// Ben asked for the progress of each point to be shown while it is scrolled. A trimmer that
    /// only showed the result would make somebody drag, let go, read, and drag again.
    @ViewBuilder
    private var readouts: some View {
        HStack(spacing: 12) {
            readout(title: "In", at: range.inPoint, colour: Theme.success,
                    active: dragging == .start)
            readout(title: "Out", at: range.outPoint, colour: Theme.danger,
                    active: dragging == .end)

            Spacer()

            VStack(alignment: .trailing, spacing: 1) {
                Text(Self.duration(range.keptDuration))
                    .font(.callout.monospacedDigit().weight(.semibold))
                    .foregroundStyle(Theme.ecto)
                    // On the number itself, not the stack: an identifier on a container matches
                    // every label inside it, and a query for one then finds several.
                    .accessibilityIdentifier("trim-kept-duration")
                Text(range.isWholeSession ? "the whole session" : "will be sent")
                    .font(.caption2).foregroundStyle(Theme.fog)
            }
        }
    }

    private func readout(title: String, at moment: Date, colour: Color, active: Bool) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(title).font(.caption2).foregroundStyle(Theme.fog)
            Text(Self.duration(moment.timeIntervalSince(range.sessionStart)))
                .font(.callout.monospacedDigit())
                // The handle being dragged is the one being read, so it is the one that stands out.
                .foregroundStyle(active ? colour : Theme.bone)
        }
        .accessibilityIdentifier(title == "In" ? "trim-in-point" : "trim-out-point")
    }

    // MARK: - The track

    private var track: some View {
        GeometryReader { geometry in
            // The dots sit ON the ends, so the travel available to them is the width less one dot.
            let usable = max(1, geometry.size.width - dotSize)
            let inX = dotSize / 2 + usable * range.fraction(of: range.inPoint)
            let outX = dotSize / 2 + usable * range.fraction(of: range.outPoint)

            ZStack(alignment: .leading) {
                // Everything that will NOT be sent: thin and quiet.
                Capsule()
                    .fill(Theme.mist)
                    .frame(height: trackHeight)

                // What will be sent: thicker, and in the site's own accent rather than a shade of
                // the same grey — the difference has to be obvious across a dark room.
                Capsule()
                    .fill(Theme.ecto)
                    .frame(width: max(0, outX - inX), height: keptHeight)
                    .offset(x: inX)

                handle(at: inX, colour: Theme.success, which: .start, usable: usable)
                handle(at: outX, colour: Theme.danger, which: .end, usable: usable)
            }
            .frame(height: dotSize, alignment: .center)
            .frame(maxHeight: .infinity)
            // Every drag reports its position in THIS space — see handle(at:).
            .coordinateSpace(name: "trim-track")
            // A real container, not a bare identifier on a GeometryReader. Without this the
            // identifier is INHERITED by both handle buttons — they surfaced as 'trim-track' and
            // lost their own names — and no element called trim-track existed at all. The UI test
            // hierarchy dump on 2026-09-04 is what showed it.
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("trim-track")
        }
        .frame(height: 44)
    }

    private func handle(at x: CGFloat, colour: Color, which: Handle, usable: CGFloat) -> some View {
        Circle()
            .fill(colour)
            .overlay(Circle().stroke(Theme.ink, lineWidth: 2))
            .frame(width: dotSize, height: dotSize)
            // A dragged handle grows, so a thumb covering it does not hide whether it is held.
            .scaleEffect(dragging == which ? 1.25 : 1)
            .offset(x: x - dotSize / 2)
            // A Circle is decoration as far as accessibility is concerned, so it is neither
            // announced to VoiceOver nor findable by a test until it is made an element in its
            // own right. Both of those matter: this is the control the whole screen is about.
            .accessibilityElement()
            .accessibilityAddTraits(.isButton)
            .accessibilityLabel(which == .start ? "Start of what will be sent"
                                                : "End of what will be sent")
            .accessibilityValue(Self.duration(
                (which == .start ? range.inPoint : range.outPoint)
                    .timeIntervalSince(range.sessionStart)))
            // highPriorityGesture, not gesture: this sits in a Form row, and a List claims a
            // horizontal drag for itself — leftward from the trailing edge is exactly a row's
            // swipe. The out point lives at that edge and never moved until this outranked it.
            .highPriorityGesture(
                // The location is asked for in the TRACK's space, not the handle's. A dragged
                // view reports its gesture relative to where it sat before the offset, so
                // adding the handle's own position back in double-counted it: the in point ran
                // away to the far end of the track and the out point could not move at all.
                // The UI test's hierarchy dump on 2026-09-04 (In 0:08 of 0:09 for a drag to the
                // middle) is what exposed it; a screenshot of the resting slider never could.
                DragGesture(minimumDistance: 0, coordinateSpace: .named("trim-track"))
                    .onChanged { value in
                        dragging = which
                        let fraction = (value.location.x - dotSize / 2) / usable
                        switch which {
                        case .start: range.moveIn(toFraction: fraction)
                        case .end:   range.moveOut(toFraction: fraction)
                        }
                    }
                    .onEnded { _ in dragging = nil })
            .accessibilityIdentifier(which == .start ? "trim-handle-in" : "trim-handle-out")
    }

    private var elapsedLabels: some View {
        HStack {
            Text("0:00").font(.caption2).foregroundStyle(Theme.fog)
            Spacer()
            Text(Self.duration(range.duration)).font(.caption2).foregroundStyle(Theme.fog)
        }
    }

    /// h:mm:ss, or m:ss under an hour — the spelling a video scrubber uses.
    static func duration(_ seconds: TimeInterval) -> String {
        let total = Int(max(0, seconds.rounded()))
        let (h, m, s) = (total / 3600, (total % 3600) / 60, total % 60)
        return h > 0
            ? String(format: "%d:%02d:%02d", h, m, s)
            : String(format: "%d:%02d", m, s)
    }
}
