import SwiftUI
import BenKit

@main
struct IsHauntedApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var dependencies = AppDependencies()
    @State private var router = Router()

    var body: some Scene {
        WindowGroup {
            RootShell()
                .environment(dependencies)
                .environment(router)
                .tint(Theme.ecto)
                // Taps on linkified @mentions and #tags carry ishaunted:// URLs;
                // they must navigate IN the app, not bounce through the OS.
                .environment(\.openURL, OpenURLAction { url in
                    guard url.scheme == "ishaunted", let link = DeepLinkParser.parse(url) else {
                        return .systemAction
                    }
                    router.open(link)
                    return .handled
                })
                .onOpenURL { url in
                    // A file handed over — AirDrop, Files, Mail — is a session bundle to open.
                    if url.isFileURL {
                        Task { await FieldBundleOpener.open(url, dependencies: dependencies, router: router) }
                        return
                    }
                    // Website URLs and ishaunted:// links land on the logically
                    // matching native screen — one URL space, two front ends.
                    if let link = DeepLinkParser.parse(url) {
                        router.open(link)
                    }
                }
                // Push (item 252): the registrar needs both before a tap or a token can arrive.
                // Registered on launch and at every sign-in, so a token Apple rotates reaches the server.
                .onChange(of: dependencies.session.me?.userId, initial: true) { _, _ in
                    PushRegistrar.shared.dependencies = dependencies
                    PushRegistrar.shared.router = router
                    PushRegistrar.shared.refresh()
                }
                .onAppear {
                    // Automation/UI-test hook: `-openLink <url>` routes exactly
                    // like an incoming deep link, without the OS confirm dialog.
                    let testLink = UserDefaults.standard.string(forKey: "openLink")
                        .flatMap(URL.init(string:)).flatMap(DeepLinkParser.parse)
                    #if DEBUG
                    // Dev/UI-test hook only — never compiled into Release:
                    // `-autoSignIn "email:password"` drives the real store flow
                    // (network, Keychain, api/me) without typing.
                    if let raw = UserDefaults.standard.string(forKey: "autoSignIn"),
                       let split = raw.range(of: ":") {
                        let email = String(raw[..<split.lowerBound])
                        let password = String(raw[split.upperBound...])
                        Task {
                            // A session restored from the keychain — a previous test's account —
                            // would otherwise win, because sign-in is only offered signed out. Let
                            // the restore settle, then switch if it is somebody else (item 252's
                            // lead test, run after a guest's).
                            let session = dependencies.session
                            for _ in 0..<60 where session.state == .fetchingIdentity || session.state == .authenticating {
                                try? await Task.sleep(for: .milliseconds(100))
                            }
                            if let me = session.me, me.email.caseInsensitiveCompare(email) != .orderedSame {
                                await session.signOut()
                            }
                            await session.signIn(email: email, password: password)
                            // A link asked for alongside a sign-in is opened AS that person: a
                            // screen that acts on who is signed in (a QR join) must not act on the
                            // account the previous test left behind.
                            if let testLink { router.open(testLink) }
                        }
                        return
                    }
                    #endif
                    if let testLink { router.open(testLink) }
                }
        }
    }
}
