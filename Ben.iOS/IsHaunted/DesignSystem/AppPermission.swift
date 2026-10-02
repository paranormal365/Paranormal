import SwiftUI
import AVFoundation
import CoreLocation
import Speech
import UserNotifications
import BenKit

/// Something iOS asks the person before the app may use it, and what to say when they said no.
///
/// Ben, 2026-09-27: "if they don't allow camera use by the app... if they try to turn it on, notify
/// them they have not allowed us to use the camera and open the settings for them to be able to
/// turn it on. Do the same with any settings they don't have turned on but are trying to use."
///
/// iOS asks once. After a no it never shows the question again, and asking again does nothing —
/// so the only way back is this app's page in Settings, and the only honest thing to do when
/// somebody reaches for a refused feature is to say so and take them there.
enum AppPermission: String, Identifiable, Sendable {
    case camera, microphone, location, speech, notifications

    var id: String { rawValue }

    var title: String {
        switch self {
        case .camera: "the camera"
        case .microphone: "the microphone"
        case .location: "your location"
        case .speech: "speech recognition"
        case .notifications: "notifications"
        }
    }

    /// Why the thing they just tried needs it.
    var why: String {
        switch self {
        case .camera: "Video, photos and Watch for Motion all need the camera."
        case .microphone: "Recording sound and EVP sessions need the microphone. Without it the recording would be silent."
        case .location: "Finding where you are, nearby places and sessions, and stamping readings with a position need location."
        case .speech: "Dictating a note turns your voice into text on this phone, which needs speech recognition."
        case .notifications: "Reminders about seats you've booked arrive as notifications."
        }
    }

    /// Where Settings shows the switch, in the words Settings uses.
    var settingsName: String {
        switch self {
        case .camera: "Camera"
        case .microphone: "Microphone"
        case .location: "Location"
        case .speech: "Speech Recognition"
        case .notifications: "Notifications"
        }
    }

    /// True when the person has said no (or a device policy has), so asking again would do
    /// nothing. "Not asked yet" is not refused: the system question still works then.
    var isRefused: Bool {
        switch self {
        case .camera:
            let status = AVCaptureDevice.authorizationStatus(for: .video)
            return status == .denied || status == .restricted
        case .microphone:
            return AVAudioApplication.shared.recordPermission == .denied
        case .location:
            let status = CLLocationManager().authorizationStatus
            return status == .denied || status == .restricted
        case .speech:
            let status = SFSpeechRecognizer.authorizationStatus()
            return status == .denied || status == .restricted
        case .notifications:
            // Asynchronous on the system side; `isRefusedNow` is the one to call.
            return false
        }
    }

    /// The same, for the permissions iOS only answers asynchronously.
    func isRefusedNow() async -> Bool {
        if self == .notifications {
            return await UNUserNotificationCenter.current().notificationSettings()
                .authorizationStatus == .denied
        }
        return isRefused
    }
}

extension AppPermission {
    /// Makes sure the app may use this, asking iOS now if it has never asked.
    ///
    /// Ben, 2026-10-02: "if they decline permission for video and click the toggle button... ask
    /// for permission again and open the permission for them to set it correctly. Same with other
    /// toggles." A switch used to turn on whatever iOS would later say, and a "no" given at the
    /// system question left it on, recording nothing. Every permission switch now calls this
    /// first: `true` turns it on; `false` — refused now or before — leaves it off, and the caller
    /// shows ``permissionRefusedAlert(_:)``, which takes the person to Settings. iOS shows its own
    /// question only once, so after a "no" Settings is the only way to change the answer.
    @MainActor
    func ensure() async -> Bool {
        if await isRefusedNow() { return false }
        switch self {
        case .camera:
            guard AVCaptureDevice.authorizationStatus(for: .video) == .notDetermined else { return true }
            return await AVCaptureDevice.requestAccess(for: .video)
        case .microphone:
            guard AVAudioApplication.shared.recordPermission == .undetermined else { return true }
            return await AVAudioApplication.requestRecordPermission()
        case .location:
            return await LocationAsker().ask()
        case .speech:
            guard SFSpeechRecognizer.authorizationStatus() == .notDetermined else { return true }
            return await withCheckedContinuation { continuation in
                SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0 == .authorized) }
            }
        case .notifications:
            return (try? await UNUserNotificationCenter.current()
                .requestAuthorization(options: [.alert, .sound, .badge])) ?? false
        }
    }
}

/// Asks for when-in-use location once and reports the answer — the delegate dance, awaited.
@MainActor
private final class LocationAsker: NSObject, @preconcurrency CLLocationManagerDelegate {
    private let manager = CLLocationManager()
    private var continuation: CheckedContinuation<Bool, Never>?
    private var keepAlive: LocationAsker?

    func ask() async -> Bool {
        switch manager.authorizationStatus {
        case .authorizedWhenInUse, .authorizedAlways: return true
        case .denied, .restricted: return false
        default: break
        }
        return await withCheckedContinuation { continuation in
            self.continuation = continuation
            keepAlive = self
            manager.delegate = self
            manager.requestWhenInUseAuthorization()
        }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        guard status != .notDetermined, let continuation else { return }
        self.continuation = nil
        keepAlive = nil
        continuation.resume(returning: status == .authorizedWhenInUse || status == .authorizedAlways)
    }
}

extension View {
    /// Says a permission was refused, and offers this app's page in Settings.
    ///
    /// Set `refused` to the permission a person just reached for; the alert clears it.
    func permissionRefusedAlert(_ refused: Binding<AppPermission?>) -> some View {
        modifier(PermissionRefusedAlert(refused: refused))
    }
}

private struct PermissionRefusedAlert: ViewModifier {
    @Binding var refused: AppPermission?
    @Environment(\.openURL) private var openURL

    func body(content: Content) -> some View {
        content.alert(
            refused.map { "IsHaunted isn't allowed to use \($0.title)" } ?? "",
            isPresented: Binding(get: { refused != nil }, set: { if !$0 { refused = nil } }),
            presenting: refused
        ) { permission in
            Button("Open Settings") {
                // Cleared BEFORE leaving for Settings. The app goes to the background while the
                // alert is closing, the alert's own binding is never told, and every later refusal
                // on the same screen was then swallowed without a word (found walking it, 2026-09-27).
                refused = nil
                if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
            }
            .accessibilityIdentifier("open-settings-for-\(permission.rawValue)")
            Button("Not now", role: .cancel) { refused = nil }
        } message: { permission in
            Text("\(permission.why) Turn on \(permission.settingsName) for IsHaunted in Settings, then come back.")
        }
    }
}

extension AppPermission {
    /// What a Field Kit channel needs before it can record anything.
    static func needed(for channel: CaptureChannels) -> AppPermission? {
        switch channel {
        case .video: .camera
        case .audio: .microphone
        case .location: .location
        default: nil
        }
    }
}

/// "Open Settings", for a line that already says what is refused.
struct OpenSettingsButton: View {
    @Environment(\.openURL) private var openURL
    var body: some View {
        Button("Open Settings") {
            if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
        }
        .font(.caption.bold())
        .accessibilityIdentifier("open-settings")
    }
}
