import SwiftUI
import UserNotifications
import BenKit

/// This phone's part in push notifications (item 252): asking at a sensible moment, telling the
/// server which phone to push for whoever is signed in, forgetting it at sign-out, and opening
/// what a tapped notification points at.
///
/// **Asked for, never taken.** Permission is requested at moments a push would plainly help — the
/// person has something coming up that a lead may launch — not at first launch, where "allow
/// notifications?" means nothing yet. Once allowed, the phone is registered on every launch and
/// sign-in, so a token Apple rotates reaches the server.
@MainActor
final class PushRegistrar {
    static let shared = PushRegistrar()

    /// Set by the app once its dependencies and router exist.
    var dependencies: AppDependencies?
    var router: Router?

    /// The last token Apple gave, kept so a sign-out after a relaunch can still remove it.
    private static let tokenKey = "push.deviceToken"
    private var token: String? {
        get { UserDefaults.standard.string(forKey: Self.tokenKey) }
        set { UserDefaults.standard.set(newValue, forKey: Self.tokenKey) }
    }

    /// A build run from Xcode is signed for Apple's sandbox; TestFlight and the App Store for production.
    private static var sandbox: Bool {
        #if DEBUG
        true
        #else
        false
        #endif
    }

    private var actions: PushDeviceActions? { dependencies.map { PushDeviceActions(api: $0.api) } }

    /// On launch and sign-in: if the person already allowed notifications, ask Apple for the token
    /// (it comes back through the app delegate). Never asks for permission itself.
    func refresh() {
        Task {
            guard dependencies?.session.me != nil else { return }
            let settings = await UNUserNotificationCenter.current().notificationSettings()
            guard settings.authorizationStatus == .authorized || settings.authorizationStatus == .provisional else { return }
            UIApplication.shared.registerForRemoteNotifications()
        }
    }

    /// At a moment a push would plainly help: ask once, and register if allowed.
    func askIfUseful() {
        Task {
            guard dependencies?.session.me != nil else { return }
            let center = UNUserNotificationCenter.current()
            let settings = await center.notificationSettings()
            if settings.authorizationStatus == .notDetermined {
                guard (try? await center.requestAuthorization(options: [.alert, .sound, .badge])) == true else { return }
            } else if settings.authorizationStatus != .authorized && settings.authorizationStatus != .provisional {
                return
            }
            UIApplication.shared.registerForRemoteNotifications()
        }
    }

    /// Apple's answer: the address this phone is pushed at.
    func tokenArrived(_ data: Data) {
        let hex = PushDeviceActions.hex(data)
        token = hex
        guard dependencies?.session.me != nil, let actions else { return }
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        Task { await actions.register(token: hex, sandbox: Self.sandbox, appVersion: version) }
    }

    /// Before sign-out finishes: this phone stops being pushed for the person leaving it.
    func signingOut() async {
        guard let token, let actions else { return }
        await actions.remove(token: token)
    }

    /// A notification was tapped: open what it points at. A launch carries its `link`; a seat
    /// reminder (a local notification) carries its event's id.
    func opened(_ userInfo: [AnyHashable: Any]) {
        guard let router else { return }
        if let raw = userInfo["link"] as? String, let url = URL(string: raw), let link = DeepLinkParser.parse(url) {
            router.open(link)
        } else if let raw = userInfo["eventId"] as? String, let id = UUID(uuidString: raw) {
            router.open(.eventDetail(id))
        }
    }
}

/// The UIKit end of push: Apple hands the token and taps to an app delegate, not to SwiftUI.
final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    func application(_ application: UIApplication, didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
        MainActor.assumeIsolated { PushRegistrar.shared.tokenArrived(deviceToken) }
    }

    func application(_ application: UIApplication, didFailToRegisterForRemoteNotificationsWithError error: Error) {
        // The simulator without a push-capable setup, or no network: nothing to do but try next launch.
    }

    /// In the app already: still show it — "the walk is starting" is the point, wherever you are.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                            withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound, .list])
    }

    /// A notification was tapped.
    ///
    /// The completion-handler form, finished on the main thread, on purpose. The `async` form of
    /// this method let iOS complete the response off the main thread, and UIKit aborts the app when
    /// it does (found by the push end-to-end test, 2026-09-28: tapping a launch's notification
    /// crashed the app).
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                            withCompletionHandler completionHandler: @escaping () -> Void) {
        let info = response.notification.request.content.userInfo
        var opened: [String: String] = [:]
        if let link = info["link"] as? String { opened["link"] = link }
        if let eventId = info["eventId"] as? String { opened["eventId"] = eventId }
        nonisolated(unsafe) let done = completionHandler
        DispatchQueue.main.async {
            MainActor.assumeIsolated { PushRegistrar.shared.opened(opened) }
            done()
        }
    }
}
