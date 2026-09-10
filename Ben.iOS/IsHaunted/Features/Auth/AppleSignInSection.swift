import SwiftUI
import AuthenticationServices
import BenKit

/// The Sign in with Apple button, and the one form it can lead to.
///
/// Apple hands over a person's real name exactly ONCE — on the first authorization for this app,
/// and never again, not even after deleting and reinstalling. So when the server says an account
/// still needs a name and a handle, whatever Apple gave is carried into that form immediately;
/// dropping it there means it is gone for good.
///
/// That form has TWO doors, and the second is offered always (item 226). The server can only join
/// an Apple identity to an existing account when Apple's own verified address happens to match one,
/// and a Hide My Email relay address never will — nor will an Apple ID that is simply at a
/// different address from the one somebody signed up with. Before this door, every one of those
/// people got a second account holding none of their cases, groups or history, silently.
struct AppleSignInSection: View {
    @Environment(AppDependencies.self) private var dependencies
    var onSignedIn: () -> Void

    // Held from the moment Apple's sheet closes, through the profile sheet, into the second
    // attempt. Both doors need it, and there is no asking Apple for another.
    @State private var pendingToken: String?
    @State private var pendingCode: String?
    @State private var suggestedName: String?
    @State private var handleProblem: String?

    // The server said the address already has an account here, so creating a second one is not an
    // option and the create form is put away.
    @State private var addressTaken: String?

    // Set only after the server says the account being claimed has a second factor. Asking up
    // front leaves most people wondering what to type, and sending an empty code spends a failed
    // attempt against an account that may have no second factor at all.
    @State private var linkNeedsTwoFactor = false
    @State private var linkMessage: String?

    @State private var collecting = false
    @State private var busy = false
    @State private var errorMessage: String?

    var body: some View {
        Section {
            SignInWithAppleButton(.signIn) { request in
                // The name is only ever offered on a first authorization; asking costs nothing
                // and not asking makes the account-creation form guess.
                request.requestedScopes = [.fullName, .email]
            } onCompletion: { result in
                Task { await handle(result) }
            }
            .signInWithAppleButtonStyle(.black)
            .frame(height: 46)
            .disabled(busy)
            .accessibilityIdentifier("sign-in-with-apple")

            if busy { ProgressView().frame(maxWidth: .infinity) }

            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .foregroundStyle(Theme.danger).font(.callout)
            }
        } footer: {
            Text("Uses your Apple Account. If you already have an account here, you'll be asked to sign in to it rather than making a second one — even if it's under a different email address.")
        }
        .sheet(isPresented: $collecting) {
            AppleProfileSheet(
                suggestedName: suggestedName,
                handleProblem: handleProblem,
                addressTaken: addressTaken,
                linkNeedsTwoFactor: linkNeedsTwoFactor,
                linkMessage: linkMessage,
                busy: busy,
                onCreate: { name, handle in
                    await finish(displayName: name, handle: handle)
                },
                onLink: { email, password, code, isRecovery in
                    await link(email: email, password: password, code: code, isRecovery: isRecovery)
                }
            )
        }
    }

    private func handle(_ result: Result<ASAuthorization, Error>) async {
        errorMessage = nil
        switch result {
        case .failure(let error):
            // A cancel is not a failure and must not be reported as one.
            if (error as? ASAuthorizationError)?.code == .canceled { return }
            errorMessage = "That sign-in didn't finish. Try again."
        case .success(let authorization):
            guard let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
                  let tokenData = credential.identityToken,
                  let token = String(data: tokenData, encoding: .utf8)
            else {
                errorMessage = "Apple didn't return a sign-in to send. Try again."
                return
            }
            pendingToken = token
            // Apple's one-shot authorization code, kept for the server to exchange: it is what
            // lets this person's Apple tokens be revoked when they delete their account (item 229).
            pendingCode = credential.authorizationCode.flatMap { String(data: $0, encoding: .utf8) }
            suggestedName = credential.fullName.flatMap {
                let formatted = PersonNameComponentsFormatter.localizedString(from: $0, style: .default)
                return formatted.isEmpty ? nil : formatted
            }
            await send(displayName: suggestedName, handle: nil)
        }
    }

    private func send(displayName: String?, handle: String?) async {
        guard let pendingToken else { return }
        busy = true
        defer { busy = false }

        switch await dependencies.appleSignIn.signIn(
            identityToken: pendingToken, displayName: displayName, handle: handle,
            authorizationCode: pendingCode) {
        case .signedIn:
            collecting = false
            await dependencies.session.adoptExternalSignIn()
            onSignedIn()
        case .needsProfile(let name, _, let problem):
            suggestedName = name ?? suggestedName
            handleProblem = problem
            addressTaken = nil
            collecting = true
        case .addressTaken(let reason):
            // Put the create form away and open the sheet straight onto the other door. A handle
            // complaint would be about a handle that is perfectly fine.
            addressTaken = reason
            handleProblem = nil
            collecting = true
        case .needsTwoFactor:
            // Only a LINK can answer this; a plain sign-in never does. Kept honest rather than
            // silently ignored, since an exhaustive switch is what makes the compiler catch a new
            // case the next time the enum grows.
            errorMessage = "That sign-in couldn't be completed. Try again."
        case .failed(let reason):
            collecting = false
            errorMessage = reason
        }
    }

    private func finish(displayName: String, handle: String) async {
        await send(displayName: displayName, handle: handle)
    }

    /// The second door: joining this Apple identity to an account that already exists here.
    ///
    /// The address is the ACCOUNT's, whatever Apple said. The password is what proves that account
    /// is theirs; holding an Apple token proves only the Apple identity, and the server refuses
    /// without both — and, for an account that has one, without the second factor too.
    private func link(email: String, password: String, code: String, isRecovery: Bool) async {
        guard let pendingToken else { return }
        busy = true
        linkMessage = nil
        defer { busy = false }

        // Nil, not empty: an empty string is an attempt with a wrong code.
        let trimmed = code.trimmingCharacters(in: .whitespaces)
        let sendCode = linkNeedsTwoFactor && !trimmed.isEmpty

        switch await dependencies.appleSignIn.link(
            identityToken: pendingToken,
            email: email,
            password: password,
            twoFactorCode: sendCode && !isRecovery ? trimmed : nil,
            recoveryCode: sendCode && isRecovery ? trimmed : nil,
            authorizationCode: pendingCode) {
        case .signedIn:
            collecting = false
            linkNeedsTwoFactor = false
            await dependencies.session.adoptExternalSignIn()
            onSignedIn()
        case .needsTwoFactor:
            // The password was right. Show the code field and say so - reporting this as a
            // failure leaves somebody hunting for a mistake they did not make.
            linkNeedsTwoFactor = true
            linkMessage = "That account uses two-step verification. Enter the code from your authenticator app."
        case .failed(let reason):
            linkMessage = reason
        case .needsProfile, .addressTaken:
            // Not answers a link can give. Named rather than swallowed, for the same exhaustiveness
            // reason as above.
            linkMessage = "That account couldn't be linked. Try again."
        }
    }
}

/// Collects what an account cannot exist without — OR claims one that already does.
///
/// The handle is permanent, and this screen says so: it is the last moment anyone can choose it.
/// The second section is offered ALWAYS, not only when the server spots a matching address. It can
/// only spot one when Apple's address happens to equal an existing account's, which is precisely
/// the case that was never broken. The person knows whether they have an account; the server
/// cannot.
private struct AppleProfileSheet: View {
    let suggestedName: String?
    let handleProblem: String?
    let addressTaken: String?
    let linkNeedsTwoFactor: Bool
    let linkMessage: String?
    let busy: Bool
    var onCreate: (String, String) async -> Void
    var onLink: (_ email: String, _ password: String, _ code: String, _ isRecovery: Bool) async -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var handle = ""

    @State private var linkEmail = ""
    @State private var linkPassword = ""
    @State private var linkCode = ""
    @State private var useRecoveryCode = false

    private var canCreate: Bool {
        addressTaken == nil
            && name.trimmingCharacters(in: .whitespaces).count >= 2
            && !handle.trimmingCharacters(in: .whitespaces).isEmpty && !busy
    }

    private var linkButton: some View {
        Button {
            Task { await onLink(linkEmail.trimmingCharacters(in: .whitespaces),
                                linkPassword, linkCode, useRecoveryCode) }
        } label: {
            if busy { ProgressView().frame(maxWidth: .infinity) }
            else {
                Text(linkNeedsTwoFactor ? "Verify and join them" : "Sign in and join them")
                    .frame(maxWidth: .infinity)
            }
        }
        .disabled(!canLink)
        .accessibilityIdentifier("apple-link-submit")
    }

    private var canLink: Bool {
        !linkEmail.trimmingCharacters(in: .whitespaces).isEmpty
            && !linkPassword.isEmpty
            && (!linkNeedsTwoFactor || !linkCode.trimmingCharacters(in: .whitespaces).isEmpty)
            && !busy
    }

    var body: some View {
        NavigationStack {
            Form {
                if let addressTaken {
                    Section {
                        Label(addressTaken, systemImage: "person.crop.circle.badge.checkmark")
                            .foregroundStyle(Theme.fog)
                            .accessibilityIdentifier("apple-address-taken")
                    }
                } else {
                    Section {
                        TextField("The name people see", text: $name)
                            .textContentType(.name)
                            .accessibilityIdentifier("apple-create-name")
                    } header: {
                        Text("Create an account")
                    } footer: {
                        Text("You can change this later.")
                    }

                    Section {
                        TextField("@name", text: $handle)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .accessibilityIdentifier("apple-create-handle")
                    } header: {
                        Text("Your @name")
                    } footer: {
                        // Said before it is chosen, not after.
                        Text(handleProblem ?? "This is permanent — it can't be changed once your account exists.")
                            .foregroundStyle(handleProblem == nil ? Theme.fog : Theme.danger)
                    }

                    Section {
                        Button {
                            Task { await onCreate(name.trimmingCharacters(in: .whitespaces),
                                                  handle.trimmingCharacters(in: .whitespaces)) }
                        } label: {
                            if busy { ProgressView().frame(maxWidth: .infinity) }
                            else { Text("Create my account").frame(maxWidth: .infinity) }
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(!canCreate)
                        .accessibilityIdentifier("apple-create-submit")
                    }
                }

                Section {
                    TextField("Email address", text: $linkEmail)
                        .textContentType(.username)
                        .keyboardType(.emailAddress)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("apple-link-email")

                    SecureField("Password", text: $linkPassword)
                        .textContentType(.password)
                        .accessibilityIdentifier("apple-link-password")

                    if linkNeedsTwoFactor {
                        TextField(useRecoveryCode ? "Recovery code" : "Code from your authenticator app",
                                  text: $linkCode)
                            .textContentType(.oneTimeCode)
                            .keyboardType(useRecoveryCode ? .default : .numberPad)
                            .autocorrectionDisabled()
                            .accessibilityIdentifier("apple-link-code")

                        Toggle("Use one of my recovery codes instead", isOn: $useRecoveryCode)
                            .accessibilityIdentifier("apple-link-recovery")
                    }

                    // Prominent only when it is the ONLY door left. Two prominent buttons on one
                    // sheet would say nothing about which one to press.
                    if addressTaken == nil {
                        linkButton.buttonStyle(.bordered)
                    } else {
                        linkButton.buttonStyle(.borderedProminent)
                    }
                } header: {
                    Text(addressTaken == nil ? "Already have an account here?" : "Sign in to it to continue")
                } footer: {
                    if let linkMessage {
                        Text(linkMessage)
                            .foregroundStyle(linkNeedsTwoFactor ? Theme.fog : Theme.danger)
                            .accessibilityIdentifier("apple-link-message")
                    } else {
                        Text("Sign in to it once and we'll join the two, so you keep everything that's already yours. It doesn't matter if it's under a different email address from your Apple ID.")
                    }
                }
            }
            .navigationTitle(addressTaken == nil ? "Almost there" : "One more step")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(busy)
                }
            }
            .onAppear { if name.isEmpty { name = suggestedName ?? "" } }
        }
        .interactiveDismissDisabled(busy)
    }
}
