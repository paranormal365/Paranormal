import CoreLocation
import BenKit

/// Where the phone is, once, for the screens that start or find sessions.
///
/// Ben, 2026-09-27: "If you use the map, you should be able to determine where they are and if they
/// did not allow you to read their position, then you should ask where they are." So this answers
/// with a position when it may, says plainly when it may not, and never asks iOS twice — after a no,
/// iOS shows nothing, and the screen offers Settings instead.
@MainActor
@Observable
final class FieldLocator: NSObject, CLLocationManagerDelegate {
    enum Access: Equatable { case notAsked, allowed, refused }

    private(set) var access: Access = .notAsked
    private(set) var point: CLLocationCoordinate2D?
    /// "1204 Elm St, Nashville" — Apple's reading of the point, when it has one.
    private(set) var address: String?
    private(set) var isLocating = false

    private let manager = CLLocationManager()
    private var waitingForFix: CheckedContinuation<CLLocationCoordinate2D?, Never>?
    private var waitingForAnswer: CheckedContinuation<Void, Never>?

    override init() {
        super.init()
        manager.delegate = self
        // A building's width: enough to name the place and to find an investigation at it,
        // without waiting for the precise fix a session's own readings take.
        manager.desiredAccuracy = kCLLocationAccuracyHundredMeters
        access = Self.access(manager.authorizationStatus)
    }

    /// Asks iOS the first time only. After that it is Settings or nothing.
    func requestAccess() async {
        guard manager.authorizationStatus == .notDetermined else {
            access = Self.access(manager.authorizationStatus)
            return
        }
        await withCheckedContinuation { continuation in
            waitingForAnswer = continuation
            manager.requestWhenInUseAuthorization()
        }
        access = Self.access(manager.authorizationStatus)
    }

    /// The position, and the address it reads as, when location is allowed.
    @discardableResult
    func locate() async -> CLLocationCoordinate2D? {
        access = Self.access(manager.authorizationStatus)
        guard access == .allowed else { return nil }
        isLocating = true
        defer { isLocating = false }

        // Once more on a miss: the first fix after launch often fails as "location unknown" and
        // the next one lands. A sheet that silently gave up on that was a blank "where are you?".
        var fix = await requestFix()
        if fix == nil {
            try? await Task.sleep(for: .seconds(1))
            fix = await requestFix()
        }
        point = fix
        if let fix {
            let placemark = try? await CLGeocoder()
                .reverseGeocodeLocation(CLLocation(latitude: fix.latitude, longitude: fix.longitude))
                .first
            address = placemark.flatMap(Self.describe)
        }
        return fix
    }

    /// One fix, or nil after `fixDeadline`. `requestLocation` can take far longer than that to give
    /// up with nothing to go on, and a spinner that never ends is as silent as a blank.
    private func requestFix() async -> CLLocationCoordinate2D? {
        let deadline = Task { @MainActor [weak self] in
            try? await Task.sleep(for: Self.fixDeadline)
            guard !Task.isCancelled, let self else { return }
            self.waitingForFix?.resume(returning: nil)
            self.waitingForFix = nil
        }
        defer { deadline.cancel() }
        return await withCheckedContinuation { continuation in
            waitingForFix = continuation
            manager.requestLocation()
        }
    }

    static let fixDeadline: Duration = .seconds(6)

    /// "1204 Elm St, Nashville", or the best part of it there is.
    private static func describe(_ placemark: CLPlacemark) -> String? {
        let street = [placemark.subThoroughfare, placemark.thoroughfare].compactMap { $0 }.joined(separator: " ")
        let parts = [street.isEmpty ? placemark.name : street, placemark.locality].compactMap { $0 }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    private static func access(_ status: CLAuthorizationStatus) -> Access {
        switch status {
        case .authorizedAlways, .authorizedWhenInUse: .allowed
        case .denied, .restricted: .refused
        default: .notAsked
        }
    }

    nonisolated func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        Task { @MainActor in
            access = Self.access(status)
            guard status != .notDetermined else { return }
            waitingForAnswer?.resume()
            waitingForAnswer = nil
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        let fix = locations.last?.coordinate
        Task { @MainActor in
            waitingForFix?.resume(returning: fix)
            waitingForFix = nil
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        Task { @MainActor in
            waitingForFix?.resume(returning: nil)
            waitingForFix = nil
        }
    }
}
