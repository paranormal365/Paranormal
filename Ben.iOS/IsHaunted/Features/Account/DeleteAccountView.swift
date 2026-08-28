import SwiftUI
import BenKit

/// Deleting your account, from inside the app.
///
/// Required by App Review Guideline 5.1.1(v) — an app that lets you create an account must let
/// you delete it here, and a link to a web form does not satisfy it.
///
/// The screen loads the check BEFORE showing anything destructive. Exactly one owner exists per
/// organization, and anonymising them would leave a group with nobody able to administer it, so
/// an owner is refused — and told which groups to hand over. A refusal that does not say what to
/// do about it is a dead end, which Apple rejects and a person cannot act on either.
struct DeleteAccountView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    @State private var check: AccountClosureCheck?
    /// Nil while loading; false once we know we simply could not ask.
    @State private var couldAsk: Bool?
    @State private var typed = ""
    @State private var isBusy = false
    @State private var failure: String?
    @State private var didDelete = false

    private var confirmationMatches: Bool {
        typed.trimmingCharacters(in: .whitespaces) == AccountClosureCheck.confirmationWord
    }

    var body: some View {
        List {
            if didDelete {
                Section {
                    Label("Your account has been deleted.", systemImage: "checkmark.circle")
                        .foregroundStyle(Theme.success)
                    Text("You have been signed out on this device. Anything you posted for a "
                       + "group stays with that group, no longer under your name.")
                        .font(.callout).foregroundStyle(Theme.fog)
                }
            } else if couldAsk == false {
                Section {
                    // Not "you have nothing blocking you" — we do not know that, and guessing a
                    // yes here would take somebody to a confirmation screen that then fails.
                    Label("Couldn't check your account just now.", systemImage: "wifi.exclamationmark")
                        .foregroundStyle(Theme.warning)
                    Text("Check your connection and try again.")
                        .font(.callout).foregroundStyle(Theme.fog)
                    Button("Try again") { Task { await load() } }
                }
            } else if let check {
                if check.canClose {
                    deletableSections
                } else {
                    blockedSection(check.ownedOrganizations)
                }
            } else {
                Section { ProgressView() }
            }
        }
        .navigationTitle("Delete account")
        .task { await load() }
    }

    // ── The path that can go through ──────────────────────────────────────────

    @ViewBuilder
    private var deletableSections: some View {
        Section {
            Text("Deleting your account removes your name, your sign-in, your photo and your "
               + "contact details. It cannot be undone.")
            Text("What you posted for a group — cases, evidence, reports and messages — stays "
               + "with that group, because it is their record and often a client's too. It will "
               + "no longer be under your name.")
                .font(.callout).foregroundStyle(Theme.fog)
        } header: {
            Text("What happens")
        }

        Section {
            TextField(AccountClosureCheck.confirmationWord, text: $typed)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .accessibilityIdentifier("delete-account-confirmation")

            Button(role: .destructive) {
                Task { await deleteAccount() }
            } label: {
                if isBusy { ProgressView() } else { Text("Delete my account") }
            }
            .disabled(!confirmationMatches || isBusy)
            .accessibilityIdentifier("delete-account-confirm")

            if let failure {
                Text(failure).font(.callout).foregroundStyle(Theme.danger)
            }
        } header: {
            Text("Confirm")
        } footer: {
            Text("Type \(AccountClosureCheck.confirmationWord) to turn on the button.")
        }
    }

    // ── The path that is blocked, and what to do about it ─────────────────────

    @ViewBuilder
    private func blockedSection(_ organizations: [AccountClosureCheck.BlockingOrganization]) -> some View {
        Section {
            Label("You still own a group.", systemImage: "person.2.badge.gearshape")
                .foregroundStyle(Theme.warning)
            ForEach(organizations) { org in
                LabeledContent(org.name, value: "Owner")
            }
        } header: {
            Text("One thing first")
        }

        Section {
            Text("A group must always have an owner. Make someone else the owner, or close the "
               + "group, and then you can delete your account.")
            Text("Ownership is handed over on the website, under the group's settings.")
                .font(.callout).foregroundStyle(Theme.fog)
        } footer: {
            Text("ishaunted.com — your group → Settings → Members")
        }
    }

    // ── Work ──────────────────────────────────────────────────────────────────

    private func load() async {
        couldAsk = nil
        let result = await dependencies.accountActions.accountClosureCheck()
        check = result
        couldAsk = result != nil
    }

    private func deleteAccount() async {
        isBusy = true
        failure = nil
        defer { isBusy = false }

        switch await dependencies.accountActions.closeAccount() {
        case .success:
            didDelete = true
            // Signing out locally is not optional: the token still authenticates for its
            // remaining lifetime, and leaving the app looking signed in to an account that no
            // longer exists is the zombie-session shape all over again.
            await dependencies.session.signOut()
        case .failure(let error):
            switch error {
            case .failed(let reason):
                // The server's sentence names the groups to hand over. Refresh the check too, so
                // the screen redraws as the blocked variant rather than only showing red text.
                failure = reason ?? "Couldn't delete your account just now."
                await load()
            case .sessionEnded:
                failure = "Your session ended. Sign in again and retry."
            case .rateLimited:
                failure = "Too many attempts. Wait a moment and try again."
            }
        }
    }
}
