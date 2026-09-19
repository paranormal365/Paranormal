import SwiftUI
import AuthenticationServices
import BenKit

/// Everything Sign in with Apple needs to remember between Apple's sheet, the server, and the one
/// form it can lead to.
///
/// **Why this is not `@State` on the section.** It used to be, and the sheet hung off the `Section`
/// itself. That shape does not survive the section's own rows changing: the spinner appears while
/// the request is in flight and disappears as it finishes, which is the same instant the sheet is
/// asked for — and SwiftUI answers by tearing the presentation down and taking the enclosing
/// sign-in sheet with it. Nothing appeared, and the app fell back to a signed-out profile with no
/// message. That is precisely what App Review saw on 2026-09-12: *"the app came back to login page
/// when we logged in with Apple."* Reproduced on an iPhone 17 Pro Max simulator, iOS 26.5, and
/// fixed by moving the presentation up to the form (see `SignInView`), which leaves this object as
/// the one place the flow's state lives.
@Observable
@MainActor
final class AppleSignInFlow {
    /// Held from the moment Apple's sheet closes, through the profile sheet, into the second
    /// attempt. Both doors need it, and there is no asking Apple for another.
    var pendingToken: String?
    var pendingCode: String?
    var suggestedName: String?
    var handleProblem: String?

    /// The server said the address already has an account here, so creating a second one is not an
    /// option and the create form is put away.
    var addressTaken: String?

    /// Set only after the server says the account being claimed has a second factor. Asking up
    /// front leaves most people wondering what to type, and sending an empty code spends a failed
    /// attempt against an account that may have no second factor at all.
    var linkNeedsTwoFactor = false
    var linkMessage: String?

    /// Whether the name-and-handle form is up.
    var collecting = false
    var busy = false
    /// Shown on the sign-in form itself, for a failure that happened before that form opened.
    var errorMessage: String?

    // MARK: - Apple's own sheet

    /// Reads Apple's answer and posts it. Returns true when the app is signed in.
    func begin(_ result: Result<ASAuthorization, Error>, using dependencies: AppDependencies) async -> Bool {
        errorMessage = nil
        switch result {
        case .failure(let error):
            // A cancel is not a failure and must not be reported as one.
            if (error as? ASAuthorizationError)?.code == .canceled { return false }
            errorMessage = "That sign-in didn't finish. Try again."
            return false

        case .success(let authorization):
            guard let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
                  let tokenData = credential.identityToken,
                  let token = String(data: tokenData, encoding: .utf8)
            else {
                errorMessage = "Apple didn't return a sign-in to send. Try again."
                return false
            }
            pendingToken = token
            // Apple's one-shot authorization code, kept for the server to exchange: it is what
            // lets this person's Apple tokens be revoked when they delete their account (item 229).
            pendingCode = credential.authorizationCode.flatMap { String(data: $0, encoding: .utf8) }
            suggestedName = credential.fullName.flatMap {
                let formatted = PersonNameComponentsFormatter.localizedString(from: $0, style: .default)
                return formatted.isEmpty ? nil : formatted
            }
            return await send(displayName: suggestedName, handle: nil, using: dependencies)
        }
    }

    // MARK: - The server

    /// Posts the identity token, with a name and handle when an account has to be made.
    /// Returns true when the app is signed in — and only then.
    @discardableResult
    func send(displayName: String?, handle: String?, using dependencies: AppDependencies) async -> Bool {
        guard let pendingToken else { return false }
        busy = true
        defer { busy = false }

        switch await dependencies.appleSignIn.signIn(
            identityToken: pendingToken, displayName: displayName, handle: handle,
            authorizationCode: pendingCode) {
        case .signedIn:
            return await adopt(using: dependencies)

        case .needsProfile(let name, _, let problem):
            suggestedName = name ?? suggestedName
            handleProblem = problem
            addressTaken = nil
            collecting = true
            return false

        case .addressTaken(let reason):
            // Put the create form away and open the sheet straight onto the other door. A handle
            // complaint would be about a handle that is perfectly fine.
            addressTaken = reason
            handleProblem = nil
            collecting = true
            return false

        case .needsTwoFactor:
            // Only a LINK can answer this; a plain sign-in never does. Kept honest rather than
            // silently ignored, since an exhaustive switch is what makes the compiler catch a new
            // case the next time the enum grows.
            report("That sign-in couldn't be completed. Try again.")
            return false

        case .failed(let reason):
            report(reason)
            return false
        }
    }

    /// The second door: joining this Apple identity to an account that already exists here.
    ///
    /// The address is the ACCOUNT's, whatever Apple said. The password is what proves that account
    /// is theirs; holding an Apple token proves only the Apple identity, and the server refuses
    /// without both — and, for an account that has one, without the second factor too.
    @discardableResult
    func link(
        email: String, password: String, code: String, isRecovery: Bool,
        using dependencies: AppDependencies
    ) async -> Bool {
        guard let pendingToken else { return false }
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
            linkNeedsTwoFactor = false
            return await adopt(using: dependencies)

        case .needsTwoFactor:
            // The password was right. Show the code field and say so - reporting this as a
            // failure leaves somebody hunting for a mistake they did not make.
            linkNeedsTwoFactor = true
            linkMessage = "That account uses two-step verification. Enter the code from your authenticator app."
            return false

        case .failed(let reason):
            linkMessage = reason
            return false

        case .needsProfile, .addressTaken:
            // Not answers a link can give. Named rather than swallowed, for the same exhaustiveness
            // reason as above.
            linkMessage = "That account couldn't be linked. Try again."
            return false
        }
    }

    /// Lets the state machine catch up with the token that has just been adopted.
    ///
    /// The `api/me` call behind this can fail — a revoked token, a server that answers the sign-in
    /// and not the identity — and when it does the app is NOT signed in. Saying so here is the
    /// difference between an explanation and a sign-in form that closes itself and leaves somebody
    /// exactly where they started.
    private func adopt(using dependencies: AppDependencies) async -> Bool {
        await dependencies.session.adoptExternalSignIn()
        if case .signedIn = dependencies.session.state {
            collecting = false
            return true
        }
        report(dependencies.session.errorMessage ?? "That sign-in couldn't be completed. Try again.")
        return false
    }

    /// Puts a failure where the person is actually looking: under the form when it is open, and on
    /// the sign-in screen when it is not.
    private func report(_ reason: String) {
        if collecting { linkMessage = reason } else { errorMessage = reason }
    }
}

/// The Sign in with Apple button.
///
/// Apple hands over a person's real name exactly ONCE — on the first authorization for this app,
/// and never again, not even after deleting and reinstalling. So when the server says an account
/// still needs a name and a handle, whatever Apple gave is carried into that form immediately;
/// dropping it there means it is gone for good.
///
/// The form itself is presented by ``SignInView``, not from here — see ``AppleSignInFlow``.
struct AppleSignInSection: View {
    @Environment(AppDependencies.self) private var dependencies

    let flow: AppleSignInFlow
    var onSignedIn: () -> Void

    var body: some View {
        Section {
            SignInWithAppleButton(.signIn) { request in
                // The name is only ever offered on a first authorization; asking costs nothing
                // and not asking makes the account-creation form guess.
                request.requestedScopes = [.fullName, .email]
            } onCompletion: { result in
                Task {
                    if await flow.begin(result, using: dependencies) { onSignedIn() }
                }
            }
            .signInWithAppleButtonStyle(.black)
            .frame(height: 46)
            .disabled(flow.busy)
            .accessibilityIdentifier("sign-in-with-apple")

            if flow.busy { ProgressView().frame(maxWidth: .infinity) }

            if let errorMessage = flow.errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .foregroundStyle(Theme.danger).font(.callout)
                    .accessibilityIdentifier("apple-sign-in-error")
            }
        } footer: {
            Text("Uses your Apple Account. If you already have an account here, you'll be asked to sign in to it rather than making a second one — even if it's under a different email address.")
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
struct AppleProfileSheet: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let flow: AppleSignInFlow
    var onSignedIn: () -> Void

    @State private var name = ""
    @State private var handle = ""

    @State private var linkEmail = ""
    @State private var linkPassword = ""
    @State private var linkCode = ""
    @State private var useRecoveryCode = false

    private var canCreate: Bool {
        flow.addressTaken == nil
            && name.trimmingCharacters(in: .whitespaces).count >= 2
            && !handle.trimmingCharacters(in: .whitespaces).isEmpty && !flow.busy
    }

    private var linkButton: some View {
        Button {
            Task {
                let signedIn = await flow.link(
                    email: linkEmail.trimmingCharacters(in: .whitespaces),
                    password: linkPassword, code: linkCode, isRecovery: useRecoveryCode,
                    using: dependencies)
                if signedIn { onSignedIn() }
            }
        } label: {
            if flow.busy { ProgressView().frame(maxWidth: .infinity) }
            else {
                Text(flow.linkNeedsTwoFactor ? "Verify and join them" : "Sign in and join them")
                    .frame(maxWidth: .infinity)
            }
        }
        .disabled(!canLink)
        .accessibilityIdentifier("apple-link-submit")
    }

    private var canLink: Bool {
        !linkEmail.trimmingCharacters(in: .whitespaces).isEmpty
            && !linkPassword.isEmpty
            && (!flow.linkNeedsTwoFactor || !linkCode.trimmingCharacters(in: .whitespaces).isEmpty)
            && !flow.busy
    }

    var body: some View {
        NavigationStack {
            Form {
                if let addressTaken = flow.addressTaken {
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
                        Text(flow.handleProblem ?? "This is permanent — it can't be changed once your account exists.")
                            .foregroundStyle(flow.handleProblem == nil ? Theme.fog : Theme.danger)
                    }

                    Section {
                        Button {
                            Task {
                                let signedIn = await flow.send(
                                    displayName: name.trimmingCharacters(in: .whitespaces),
                                    handle: handle.trimmingCharacters(in: .whitespaces),
                                    using: dependencies)
                                if signedIn { onSignedIn() }
                            }
                        } label: {
                            if flow.busy { ProgressView().frame(maxWidth: .infinity) }
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

                    if flow.linkNeedsTwoFactor {
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
                    if flow.addressTaken == nil {
                        linkButton.buttonStyle(.bordered)
                    } else {
                        linkButton.buttonStyle(.borderedProminent)
                    }
                } header: {
                    Text(flow.addressTaken == nil ? "Already have an account here?" : "Sign in to it to continue")
                } footer: {
                    if let linkMessage = flow.linkMessage {
                        Text(linkMessage)
                            .foregroundStyle(flow.linkNeedsTwoFactor ? Theme.fog : Theme.danger)
                            .accessibilityIdentifier("apple-link-message")
                    } else {
                        Text("Sign in to it once and we'll join the two, so you keep everything that's already yours. It doesn't matter if it's under a different email address from your Apple ID.")
                    }
                }
            }
            .navigationTitle(flow.addressTaken == nil ? "Almost there" : "One more step")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(flow.busy)
                }
            }
            .onAppear { if name.isEmpty { name = flow.suggestedName ?? "" } }
        }
        .interactiveDismissDisabled(flow.busy)
    }
}
