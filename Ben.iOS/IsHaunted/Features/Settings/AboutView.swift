import SwiftUI
import BenKit

/// What this app is, who it is for, and what it does with what you give it.
///
/// Apple requires a privacy policy reachable from inside the app as well as from App Store
/// Connect, and a reviewer looks for it — an app that collects an email address and a location
/// with no in-app statement about either is a routine rejection under Guideline 5.1.1. The
/// short version lives here in full rather than behind the web link, because a link is not an
/// answer when the reviewer (or a user) has no signal.
///
/// The wording tracks `/privacy` on the website. When one changes the other has to: two
/// statements about the same handling that disagree is worse than one that is merely brief.
struct AboutView: View {
    /// The public privacy policy. Anonymous by design — this is the exact URL given to App
    /// Store Connect, and a version of it behind a sign-in is a rejected submission.
    private static let privacyURL = URL(string: "https://ishaunted.com/privacy")!
    private static let contactURL = URL(string: "https://ishaunted.com/contact")!
    private static let helpURL = URL(string: "https://ishaunted.com/help")!

    private var version: String {
        let bundle = Bundle.main
        let short = bundle.infoDictionary?["CFBundleShortVersionString"] as? String ?? "—"
        let build = bundle.infoDictionary?["CFBundleVersion"] as? String ?? "—"
        return "\(short) (\(build))"
    }

    var body: some View {
        List {
            Section {
                Text("IsHaunted is the field companion for paranormal investigators. Run an "
                   + "investigation from your phone: record a session with the sensors and "
                   + "microphone already in your pocket, capture photos, audio and notes as they "
                   + "happen, and file them straight onto the case they belong to.")
                Text("It is built for the people who do this work — investigation groups and "
                   + "their members, ghost-walk guides running public tours, and clients who "
                   + "have asked a group to look at their property. Anyone can browse the public "
                   + "feed and upcoming events without an account.")
            } header: {
                Text("What this app is")
            }

            Section {
                Label("A field session records sensor readings, audio, photos and location while "
                    + "you have it running — and stops when you stop it.",
                      systemImage: "gauge.with.needle")
                Label("Everything a session captures stays on this device until you choose to "
                    + "upload it to your group.", systemImage: "iphone")
                Label("Cases, evidence and messages sync with your group on ishaunted.com.",
                      systemImage: "folder")
            } header: {
                Text("What it does")
            }

            Section {
                Text("We collect nothing about you except what you give us directly. There is no "
                   + "advertising, no analytics or tracking service, no data broker, and nothing "
                   + "that follows you to other apps or websites. If you did not type it, upload "
                   + "it, or ask the app to record it, we do not have it.")
                Text("Location is stamped on readings only while a session is running, and only "
                   + "if you allowed it. The app never asks for background location and never "
                   + "watches where you are between sessions.")
                Link(destination: Self.privacyURL) {
                    Label("Read the full privacy policy", systemImage: "hand.raised")
                }
                .accessibilityIdentifier("about-privacy-policy")
            } header: {
                Text("Privacy")
            } footer: {
                Text("The same statement, in full, at ishaunted.com/privacy.")
            }

            Section {
                Link(destination: Self.helpURL) {
                    Label("Help and guides", systemImage: "questionmark.circle")
                }
                Link(destination: Self.contactURL) {
                    // No account needed on the other end — which is the point for somebody who
                    // cannot sign in, and for a reviewer who has no account at all.
                    Label("Contact support", systemImage: "envelope")
                }
                .accessibilityIdentifier("about-contact-support")
            } header: {
                Text("Support")
            }

            Section {
                LabeledContent("Version", value: version)
            }
        }
        .navigationTitle("About & Privacy")
    }
}
