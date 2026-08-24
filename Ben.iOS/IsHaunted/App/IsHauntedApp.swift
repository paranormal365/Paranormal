import SwiftUI
import BenKit

@main
struct IsHauntedApp: App {
    @State private var dependencies = AppDependencies()
    @State private var router = Router()

    var body: some Scene {
        WindowGroup {
            RootShell()
                .environment(dependencies)
                .environment(router)
                .tint(Theme.ecto)
                .onOpenURL { url in
                    // Website URLs and ishaunted:// links land on the logically
                    // matching native screen — one URL space, two front ends.
                    if let link = DeepLinkParser.parse(url) {
                        router.open(link)
                    }
                }
                .onAppear {
                    // Automation/UI-test hook: `-openLink <url>` routes exactly
                    // like an incoming deep link, without the OS confirm dialog.
                    if let raw = UserDefaults.standard.string(forKey: "openLink"),
                       let url = URL(string: raw),
                       let link = DeepLinkParser.parse(url) {
                        router.open(link)
                    }
                    #if DEBUG
                    // Dev/UI-test hook only — never compiled into Release:
                    // `-autoSignIn "email:password"` drives the real store flow
                    // (network, Keychain, api/me) without typing.
                    if let raw = UserDefaults.standard.string(forKey: "autoSignIn"),
                       let split = raw.range(of: ":") {
                        let email = String(raw[..<split.lowerBound])
                        let password = String(raw[split.upperBound...])
                        Task { await dependencies.session.signIn(email: email, password: password) }
                    }
                    #endif
                }
        }
    }
}
