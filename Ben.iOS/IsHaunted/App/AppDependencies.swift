import Foundation
import SwiftUI
import BenKit

/// The app's composition root. One instance for the process; environments can
/// be switched at runtime from Developer Settings (which clears the session).
@Observable
@MainActor
final class AppDependencies {
    private let environmentStore = APIEnvironmentStore()

    private(set) var environment: APIEnvironment
    let tokens: TokenSession
    let api: APIClient
    let session: SessionStore
    let appleSignIn: AppleSignInClient
    /// Field sessions live on the DEVICE, so this one is built here rather than per-screen: a
    /// recording session has to outlive whatever screen started it.
    let fieldKit: FieldSessionStore
    let fieldUpload: FieldUploadClient
    /// The feed's write surface (item 186 F2–F7) — one instance, shared by every screen
    /// that can post, like, follow or report.
    let feedActions: FeedActions
    /// What's waiting on the signed-in person — shared, because the tab badge and the
    /// notifications screen must never show different numbers.
    let notifications: NotificationsStore
    /// Images behind a bearer token (case files) — AsyncImage cannot carry one.
    let imageLoader: AuthenticatedImageLoader
    /// Getting an account and looking after it (Slice 8).
    let accountActions: AccountActions
    let archiveActions: ArchiveActions

    /// The guest's own copy of what they offered at somebody's public event (2026-08-31).
    let evidenceActions: EvidenceActions

    /// Which parts of the app apply to this person — the server decides, the shell renders it.
    let surfaces: SurfacesStore

    /// Written by the shared holder so every request follows a switch instantly.
    private let environmentBox: EnvironmentBox

    init() {
        let box = EnvironmentBox(environmentStore.load())
        self.environmentBox = box
        self.environment = box.value

        let transport = URLSessionTransport()
        let tokens = TokenSession(
            storage: KeychainTokenStorage(),
            transport: transport,
            environment: { box.value })
        self.tokens = tokens
        let api = APIClient(
            environment: { box.value },
            transport: transport,
            tokens: tokens)
        self.api = api
        self.session = SessionStore(
            auth: IdentityAuthClient(environment: { box.value }, transport: transport),
            tokens: tokens,
            api: api)
        self.feedActions = FeedActions(api: api)
        self.notifications = NotificationsStore(api: api)
        self.imageLoader = AuthenticatedImageLoader(api: api)
        self.accountActions = AccountActions(api: api)
        self.archiveActions = ArchiveActions(api: api)
        self.evidenceActions = EvidenceActions(api: api)
        self.surfaces = SurfacesStore(api: api)
        self.appleSignIn = AppleSignInClient(api: api, tokens: tokens)
// The instruments are built ONCE, here on the main actor, because CoreMotion and UIDevice
        // want it — then handed to the store as a value it can hold. A simulator has no
        // magnetometer, so a debug build with `-fieldKitFakeSensors` gets scripted ones instead;
        // a gauge nobody can watch move is a gauge nobody has checked.
        let suite: SensorSuite
        #if DEBUG
        suite = FakeSensors.isEnabled ? FakeSensors.suite() : LiveSensors.suite()
        #else
        suite = LiveSensors.suite()
        #endif
        self.fieldKit = FieldSessionStore.live(sensors: { suite })
        self.fieldUpload = FieldUploadClient(api: api)
    }

    /// Switching environments must clear the session — a Dev token means
    /// nothing to UAT, and serving it there would just burn a 401.
    func switchEnvironment(to newEnvironment: APIEnvironment) async {
        environmentBox.value = newEnvironment
        environment = newEnvironment
        environmentStore.save(newEnvironment)
        await session.signOut()
        URLSession.benShared.configuration.urlCache?.removeAllCachedResponses()
    }
}

/// A tiny lock-guarded box so the `@Sendable` environment closures handed to
/// the actors always read the current choice.
final class EnvironmentBox: @unchecked Sendable {
    private let lock = NSLock()
    private var _value: APIEnvironment

    init(_ value: APIEnvironment) {
        self._value = value
    }

    var value: APIEnvironment {
        get { lock.withLock { _value } }
        set { lock.withLock { _value = newValue } }
    }
}
