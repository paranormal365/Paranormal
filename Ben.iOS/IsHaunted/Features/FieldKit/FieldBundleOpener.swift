import SwiftUI
import UniformTypeIdentifiers
import BenKit

extension UTType {
    /// A `.ben` — one sealed field session. Declared in Info.plist; this is its Swift name.
    static let fieldSessionBundle = UTType(exportedAs: "com.ishaunted.field-session")
}

/// Brings a `.ben` onto this phone from wherever it arrived, and opens it.
///
/// Three doors, one room. A bundle can come from another person's device (AirDrop, Mail, Files),
/// from the Files app through the Open button, or down from the server; each of those hands a
/// file URL to this, and this does the same thing with all of them. Ben, 2026-09-16: "It should
/// be implemented by pulling from server or shared from other user's device."
@MainActor
enum FieldBundleOpener {

    /// Imports the file at `url` and opens its review screen — or says why it could not.
    static func open(_ url: URL, dependencies: AppDependencies, router: Router,
                     serverSessionId: UUID? = nil) async {
        let store = dependencies.fieldKit
        store.importProblem = nil

        // A file from another app or from Files arrives security-scoped, and is only readable
        // inside that scope. It is copied out at once so the import can take its time.
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }

        let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent("open-\(UUID().uuidString)", isDirectory: true)
            .appendingPathComponent(url.lastPathComponent)
        defer { try? FileManager.default.removeItem(at: copy.deletingLastPathComponent()) }

        do {
            try FileManager.default.createDirectory(at: copy.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            try FileManager.default.copyItem(at: url, to: copy)
            let imported = try await store.importBundle(
                at: copy, thisDeviceId: DeviceModel.vendorIdentifier(),
                serverSessionId: serverSessionId)
            router.push(.fieldSessionReview(imported.id), in: .fieldKit)
        } catch {
            // Said on the Field Kit screen, which is where the session would have appeared.
            store.importProblem = error.localizedDescription
            router.selection = .fieldKit
        }
    }
}
