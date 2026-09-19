import Foundation
import SwiftData

/// The SwiftData container for field sessions.
///
/// This is the app's FIRST local database. It holds only low-volume, editable, relational rows —
/// sessions, markers, captures. Readings never enter it; they stream to `ReadingLog`.
///
/// A container that fails to open is reported, never fatal: a broken store must show a person
/// what happened, not take the app down with it. The same doctrine the network layer follows —
/// a refusal is a state, not a crash.
public struct FieldSessionDatabase: Sendable {

    public let container: ModelContainer

    public init(container: ModelContainer) {
        self.container = container
    }

    /// The real on-disk store, in Application Support beside the session directories.
    public static func onDisk(at url: URL? = nil) throws -> FieldSessionDatabase {
        let storeURL: URL
        if let url {
            storeURL = url
        } else {
            let base = try FileManager.default.url(
                for: .applicationSupportDirectory, in: .userDomainMask,
                appropriateFor: nil, create: true)
            storeURL = base.appendingPathComponent("field-sessions.store")
        }

        let configuration = ModelConfiguration(url: storeURL)
        return FieldSessionDatabase(
            container: try ModelContainer(
                for: FieldSession.self, FieldMarker.self, FieldCapture.self,
                configurations: configuration))
    }

    /// A throwaway store for tests — no file, no cleanup, no interference between suites.
    public static func inMemory() throws -> FieldSessionDatabase {
        let configuration = ModelConfiguration(isStoredInMemoryOnly: true)
        return FieldSessionDatabase(
            container: try ModelContainer(
                for: FieldSession.self, FieldMarker.self, FieldCapture.self,
                configurations: configuration))
    }
}
