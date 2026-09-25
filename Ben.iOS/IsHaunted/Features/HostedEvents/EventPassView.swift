import SwiftUI
import CoreImage.CIFilterBuiltins
import BenKit

/// The pass, drawn on the phone (item 235 phase 14).
///
/// **The code is made here from the token**, not fetched as a picture, so it is there with no signal: the last
/// pass read is kept on the phone, and this screen shows it at once and refreshes behind it. When the refresh
/// cannot reach the server, the screen says when the saved copy is from.
///
/// **A withdrawn pass is shown withdrawn** — faded, banded, with the venue's reason — never as a blank and never
/// as valid.
///
/// **The screen goes bright while it is open**, because a door scanner reading a dim phone in a dark hallway is
/// the ordinary case, and gives the brightness back on the way out.
struct EventPassView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.scenePhase) private var scenePhase

    @State private var store: HostedEventsStore?
    @State private var pass: MyHostedEventPass?
    @State private var savedAt: Date?
    @State private var noPass: String?
    @State private var failure: String?
    @State private var loading = true
    @State private var brightnessBefore: CGFloat?

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                if let pass {
                    card(pass)
                } else if loading {
                    ProgressView("Fetching your pass…").padding(.top, 60)
                } else if let noPass {
                    ContentUnavailableView("No pass yet", systemImage: "ticket", description: Text(noPass))
                } else {
                    ContentUnavailableView {
                        Label("Couldn't fetch your pass", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                    } description: {
                        Text(failure ?? "Check your connection and try again.")
                    } actions: {
                        Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                    }
                }
            }
            .padding()
        }
        .navigationTitle("Your pass")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task {
            if store == nil {
                let made = HostedEventsStore(api: dependencies.api)
                store = made
                if let saved = made.savedPass(hostedEventId) {
                    pass = saved.pass
                    savedAt = saved.savedAt
                }
            }
            await load()
        }
        .onAppear(perform: brighten)
        .onDisappear(perform: restoreBrightness)
        // The phone locked in the queue, or the app put away with the pass still up: the brightness
        // goes back then too, and comes up again when the pass is back in front. `onDisappear`
        // alone left a phone at full brightness until the next time this screen was closed.
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { brighten() } else { restoreBrightness() }
        }
    }

    @ViewBuilder
    private func card(_ pass: MyHostedEventPass) -> some View {
        let revoked = pass.pass.isRevoked
        VStack(spacing: 14) {
            Text(pass.eventName).font(.title3.weight(.semibold)).multilineTextAlignment(.center)
            if let venue = pass.venueName {
                Text(venue).font(.subheadline).foregroundStyle(Theme.fog)
            }

            ZStack {
                if let image = Self.qrImage(pass.pass.token) {
                    Image(uiImage: image)
                        .interpolation(.none)
                        .resizable()
                        .scaledToFit()
                        .frame(width: 240, height: 240)
                        .padding(16)
                        // Always black on white, whatever the theme: a scanner reads contrast, not style.
                        .background(Color.white, in: RoundedRectangle(cornerRadius: 12))
                        .opacity(revoked ? 0.4 : 1)
                        .accessibilityLabel("Pass code \(pass.pass.shortCode)")
                        .accessibilityIdentifier("event-pass-code")
                }
                if revoked {
                    Text("WITHDRAWN")
                        .font(.headline.weight(.heavy))
                        .padding(.horizontal, 16).padding(.vertical, 6)
                        .background(Theme.danger, in: Capsule())
                        .foregroundStyle(.white)
                        .accessibilityIdentifier("event-pass-withdrawn")
                }
            }

            Text(pass.pass.shortCode)
                .font(.system(.title, design: .monospaced).weight(.bold))
                .accessibilityLabel("Code \(pass.pass.shortCode.map(String.init).joined(separator: " "))")

            if revoked {
                Text(pass.pass.revokedReason.map { "This pass has been withdrawn: \($0)" } ?? "This pass has been withdrawn.")
                    .font(.footnote).foregroundStyle(Theme.danger).multilineTextAlignment(.center)
            }

            if let band = pass.band {
                HStack(spacing: 6) {
                    Circle().fill(Color(hex: band.hex) ?? Theme.ecto).frame(width: 14, height: 14)
                    Text("\(band.colour) band · \(band.meaning)").font(.subheadline)
                }
            }

            VStack(spacing: 4) {
                Text("Admits \(pass.partySize) \(pass.partySize == 1 ? "person" : "people")").font(.headline)
                Text(pass.leadName).font(.subheadline).foregroundStyle(Theme.fog)
                if pass.kind == .dayPass && pass.nights.isEmpty {
                    Text("For the day").font(.subheadline)
                }
                ForEach(pass.nights, id: \.self) { night in
                    Text(night.date.formatted(Date.FormatStyle(timeZone: TimeZone(identifier: "UTC")!)
                            .weekday(.abbreviated).month(.twoDigits).day(.twoDigits).year())
                         + (night.unitName.isEmpty ? "" : " · \(night.unitName)"))
                        .font(.subheadline)
                }
                ForEach(pass.seating ?? [], id: \.self) { line in
                    Text(line).font(.caption).foregroundStyle(Theme.fog)
                }
            }

            if let savedAt {
                Label("Saved on this phone \(savedAt.formatted(date: .abbreviated, time: .shortened)) — no signal just now.",
                      systemImage: "wifi.slash")
                    .font(.caption).foregroundStyle(Theme.warning).multilineTextAlignment(.center)
                    .accessibilityIdentifier("event-pass-saved")
            }
        }
        .frame(maxWidth: 420)
        .padding()
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 16))
    }

    private func load() async {
        guard let store else { return }
        switch await store.loadPass(hostedEventId) {
        case .live(let fresh):
            pass = fresh
            savedAt = nil
            noPass = nil
        case .saved(let saved, let when):
            pass = saved
            savedAt = when
        case .none(let reason):
            pass = nil
            savedAt = nil
            noPass = reason ?? "Your pass is issued when the venue confirms your place."
        case .failed(let reason):
            if pass == nil { failure = reason }
        }
        loading = false
    }

    // ── the code, and the light to read it by ────────────────────────────────

    /// One rendering context for every pass drawn. A `CIContext` is a GPU context and is meant to
    /// be kept; making a new one for each code drew the same pass slower each time the screen
    /// re-evaluated.
    private static let renderer = CIContext()

    /// A QR code for the token, sharp at any size: generated small and scaled with no smoothing.
    static func qrImage(_ token: String) -> UIImage? {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(token.utf8)
        filter.correctionLevel = "M"
        guard let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: 10, y: 10)),
              let cgImage = renderer.createCGImage(output, from: output.extent) else { return nil }
        return UIImage(cgImage: cgImage)
    }

    private var screen: UIScreen? {
        UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first?.screen
    }

    private func brighten() {
        guard let screen, brightnessBefore == nil else { return }
        brightnessBefore = screen.brightness
        screen.brightness = 1
    }

    private func restoreBrightness() {
        guard let screen, let before = brightnessBefore else { return }
        screen.brightness = before
        brightnessBefore = nil
    }
}

extension Color {
    /// "#1E88E5" or "1E88E5"; nil for anything else.
    init?(hex: String?) {
        guard var raw = hex?.trimmingCharacters(in: .whitespaces) else { return nil }
        if raw.hasPrefix("#") { raw.removeFirst() }
        guard raw.count == 6, let value = UInt32(raw, radix: 16) else { return nil }
        self.init(red: Double((value >> 16) & 0xFF) / 255, green: Double((value >> 8) & 0xFF) / 255, blue: Double(value & 0xFF) / 255)
    }
}
