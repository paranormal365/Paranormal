import SwiftUI
import BenKit

// Asking and listening, on screen (Ben, 10/20/2026): "there should be a button to say that you are
// done asking and should be recording audio for a response. Then the button to say you are asking
// the next question and press again to say you are done and listening for a response. All the
// while, on that page, it should show the current readings for emf and audio db — so it keeps the
// investigator informed."

/// The magnetic field and the sound, as they are now — small enough to sit on any screen.
struct LiveReadingsStrip: View {
    let session: ActiveFieldSession

    var body: some View {
        HStack(spacing: 10) {
            reading(title: "EMF", systemImage: "gauge.with.needle", value: emfText,
                    alert: session.isReportingNow)
                .accessibilityLabel("Magnetic field, \(emfSpoken)")
                .accessibilityIdentifier("live-emf")
            reading(title: "Sound", systemImage: "waveform", value: soundText, alert: false)
                .accessibilityLabel("Sound, \(soundSpoken)")
                .accessibilityIdentifier("live-sound")
        }
    }

    private func reading(title: String, systemImage: String, value: String, alert: Bool) -> some View {
        HStack(spacing: 8) {
            Image(systemName: systemImage)
                .foregroundStyle(alert ? Theme.warning : Theme.ecto)
            VStack(alignment: .leading, spacing: 0) {
                Text(title).font(.caption2).foregroundStyle(Theme.fog)
                Text(value)
                    .font(.title3.weight(.semibold).monospacedDigit())
                    .foregroundStyle(alert ? Theme.warning : Theme.bone)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12).padding(.vertical, 8)
        .frame(maxWidth: .infinity)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
        .accessibilityElement(children: .ignore)
    }

    // MARK: - Words

    private var emfText: String {
        guard session.channels.contains(.magnetic) else { return "off" }
        if let deviation = session.magneticDeviationMilligauss {
            return ReadingText.signedWhole(deviation, unit: "mG")
        }
        if let total = session.sample.magneticMilligauss { return "\(ReadingText.whole(total)) mG" }
        return "—"
    }

    private var emfSpoken: String {
        guard session.channels.contains(.magnetic) else { return "switched off" }
        if let deviation = session.magneticDeviationMilligauss {
            return ReadingText.fromBase(deviation, unit: "milligauss")
        }
        if let total = session.sample.magneticMilligauss { return "\(ReadingText.whole(total)) milligauss" }
        return "no reading yet"
    }

    private var soundText: String {
        guard session.channels.contains(.audio) else { return "off" }
        return session.sample.soundDbfs.map { "\(ReadingText.whole($0)) dB" } ?? "—"
    }

    private var soundSpoken: String {
        guard session.channels.contains(.audio) else { return "switched off" }
        return session.sample.soundDbfs.map { "\(ReadingText.whole($0)) decibels" } ?? "no reading yet"
    }
}

/// "Motion photos on", and a moment of "Photo taken" each time one is.
///
/// Ben, 10/20/2026: "it was not obvious that it was supposed to be recording pictures when
/// detecting movement."
struct MotionPhotoBadge: View {
    let session: ActiveFieldSession

    /// How long "Photo taken" stays up after a photo.
    static let photoSeconds: TimeInterval = 2.5

    var body: some View {
        if session.channels.contains(.video), session.watchForMotion {
            TimelineView(.periodic(from: .now, by: 0.5)) { context in
                let justTook = session.lastMotionPhotoAt
                    .map { context.date.timeIntervalSince($0) < Self.photoSeconds } ?? false
                Label(justTook ? "Photo taken" : label, systemImage: justTook ? "camera.fill" : "camera.viewfinder")
                    .font(.caption2.bold())
                    .padding(.horizontal, 8).padding(.vertical, 4)
                    .background(justTook ? AnyShapeStyle(Theme.warning) : AnyShapeStyle(.black.opacity(0.55)),
                                in: Capsule())
                    .foregroundStyle(justTook ? .black : Theme.warning)
                    .animation(.easeOut(duration: 0.2), value: justTook)
                    .accessibilityLabel(justTook ? "Motion photo taken" : spoken)
            }
            .accessibilityIdentifier("motion-photos-badge")
        }
    }

    private var label: String {
        session.motionPhotoCount == 0 ? "Motion photos on" : "Motion photos on · \(session.motionPhotoCount)"
    }

    private var spoken: String {
        switch session.motionPhotoCount {
        case 0: "Motion photos are on. A photo is taken when the camera sees movement."
        case 1: "Motion photos are on. 1 photo taken."
        default: "Motion photos are on. \(session.motionPhotoCount) photos taken."
        }
    }
}

/// A white flash over the camera's picture when a motion photo is taken, like a camera's own.
struct MotionPhotoFlash: View {
    let session: ActiveFieldSession
    @State private var flashing = false

    var body: some View {
        Rectangle()
            .fill(.white)
            .opacity(flashing ? 0.7 : 0)
            .allowsHitTesting(false)
            .accessibilityHidden(true)
            .onChange(of: session.lastMotionPhotoAt) { _, taken in
                guard taken != nil else { return }
                flashing = true
                withAnimation(.easeOut(duration: 0.45)) { flashing = false }
            }
    }
}

/// The line across the top of the live page while asking or listening: which half of the cycle
/// this is, what is being typed or how long the room has had to answer, and the readings.
struct CallAndResponseBanner: View {
    let call: AskAndListen
    let session: ActiveFieldSession
    /// The next step — the same thing the side column's microphone button does — so the step is
    /// on the page in words, not only as an icon at the edge (the iPhone Duo and iPad).
    var onNextStep: () -> Void = {}

    var body: some View {
        if call.phase != .ready {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 10) {
                        AskListenIcon(phase: call.phase, size: 22)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(AskListenWords.title(call.phase))
                                .font(.callout.weight(.bold))
                                .foregroundStyle(AskListenWords.color(call.phase))
                            Text(line(at: context.date))
                                .font(.callout).foregroundStyle(Theme.bone)
                                .lineLimit(2)
                        }
                        Spacer(minLength: 0)
                    }
                    if call.phase == .asking, !call.isDictating {
                        TextField("Type the question (optional)",
                                  text: Binding(get: { call.heard }, set: { call.type($0) }))
                            .textFieldStyle(.roundedBorder)
                            .accessibilityLabel("Question being asked")
                            .accessibilityIdentifier("call-question-text")
                    }
                    HStack(spacing: 10) {
                        Button(action: onNextStep) {
                            Text(call.primaryTitle).font(.callout.bold()).frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(call.phase == .asking ? Theme.success : Theme.danger)
                        .accessibilityHint(call.primaryHint)
                        .accessibilityIdentifier("call-next-step")
                        if call.phase == .listening {
                            Button("Done listening") { Task { await call.stopListening(session: session) } }
                                .buttonStyle(.signalSecondary)
                                .accessibilityHint("Stops listening without asking another question.")
                                .accessibilityIdentifier("done-listening")
                        }
                    }
                    LiveReadingsStrip(session: session)
                }
                .padding(12)
                .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
                .overlay(RoundedRectangle(cornerRadius: 12)
                    .strokeBorder(AskListenWords.color(call.phase), lineWidth: 2))
            }
            // Contain, so the strip's own name doesn't replace Done listening's for VoiceOver.
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("call-and-response")
        }
    }

    private func line(at now: Date) -> String {
        switch call.phase {
        case .asking:
            return call.heard.isEmpty ? "Ask your question, then press the microphone again to listen." : call.heard
        case .listening:
            let clock = AskListenWords.clock(since: session.questionOpenedAt, now: now)
            return call.heard.isEmpty ? clock : "\(clock) · \(call.heard)"
        case .ready:
            return ""
        }
    }
}

/// The picture for each half of the cycle, so the state can be read at a glance in the dark.
struct AskListenIcon: View {
    let phase: AskAndListen.Phase
    var size: CGFloat = 44

    var body: some View {
        Image(systemName: AskListenWords.symbol(phase))
            .font(.system(size: size, weight: .semibold))
            .foregroundStyle(AskListenWords.color(phase))
            .symbolEffect(.pulse, options: .repeating, isActive: phase != .ready)
            .accessibilityHidden(true)
    }
}

/// The words and colors each state uses, in one place so the EVP screen and the banner agree.
enum AskListenWords {
    static func title(_ phase: AskAndListen.Phase) -> String {
        switch phase {
        case .ready: "Ready"
        case .asking: "Asking"
        case .listening: "Listening for an answer"
        }
    }

    static func symbol(_ phase: AskAndListen.Phase) -> String {
        switch phase {
        case .ready: "questionmark.bubble"
        case .asking: "mic.fill"
        case .listening: "ear.fill"
        }
    }

    static func color(_ phase: AskAndListen.Phase) -> Color {
        switch phase {
        case .ready: Theme.fog
        case .asking: Theme.danger
        case .listening: Theme.success
        }
    }

    /// "0:42" since `start`.
    static func clock(since start: Date?, now: Date) -> String {
        let seconds = max(0, Int(now.timeIntervalSince(start ?? now)))
        return String(format: "%d:%02d", seconds / 60, seconds % 60)
    }
}
