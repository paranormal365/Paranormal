import SwiftUI

/// "Sign in", as a button that opens the sign-in sheet right where the person is standing.
///
/// Every signed-out screen used to SAY "Sign in to see your cases" and offer nothing to press —
/// the only way in was to know to go to Profile (found walking the app signed out, 2026-10-02).
/// This is the thing to press, on every screen that needs an account.
struct SignInButton: View {
    var title: String = "Sign in"
    @Environment(AppDependencies.self) private var dependencies
    @State private var showing = false

    var body: some View {
        Button(title) { showing = true }
            .buttonStyle(.signalPrimary)
            .accessibilityIdentifier("sign-in-from-prompt")
            .sheet(isPresented: $showing) {
                SignInView().environment(dependencies)
            }
    }
}
