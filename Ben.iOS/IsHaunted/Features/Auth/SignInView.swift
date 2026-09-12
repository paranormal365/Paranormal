import SwiftUI
import BenKit

/// The sign-in form, presented as a sheet over browsable content — never a
/// wall. Handles the 2FA branch, wrong-password prose, and the auth
/// endpoint's 429 window as a countdown on the button.
struct SignInView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    @State private var email = ""
    @State private var password = ""
    @State private var countdown: Int = 0
    @State private var countdownTask: Task<Void, Never>?

    /// Sign in with Apple's whole flow, owned HERE rather than by the section that draws the
    /// button, so the form it leads to is presented by the form — see ``AppleSignInFlow``.
    @State private var apple = AppleSignInFlow()

    private var session: SessionStore { dependencies.session }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Email", text: $email)
                        .textContentType(.username)
                        .keyboardType(.emailAddress)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    SecureField("Password", text: $password)
                        .textContentType(.password)
                }

                if let message = session.errorMessage {
                    Section {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(Theme.danger)
                            .font(.callout)
                    }
                }

                Section {
                    Button {
                        Task { await session.signIn(email: email, password: password) }
                    } label: {
                        if session.state == .authenticating || session.state == .fetchingIdentity {
                            ProgressView().frame(maxWidth: .infinity)
                        } else if countdown > 0 {
                            Text("Too many tries — wait \(countdown)s")
                                .frame(maxWidth: .infinity)
                        } else {
                            Text("Sign in").frame(maxWidth: .infinity)
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(email.isEmpty || password.isEmpty || countdown > 0
                              || session.state == .authenticating
                              || session.state == .fetchingIdentity)
                }

                AppleSignInSection(flow: apple) { dismiss() }
            }
            .navigationTitle("Sign in")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .sheet(isPresented: Binding(
                get: { session.state == .twoFactorChallenge },
                set: { if !$0 { session.cancelTwoFactor() } })
            ) {
                TwoFactorChallengeView()
                    .environment(dependencies)
            }
            .onChange(of: session.state) { _, newState in
                if case .signedIn = newState { dismiss() }
            }
            .onChange(of: session.retryAfter) { _, newValue in
                startCountdown(from: newValue)
            }
            .onDisappear { countdownTask?.cancel() }
        }
        // On the navigation stack, NOT on the Apple section, and not stacked on the form beside
        // the two-factor sheet either. A presentation asked for by a section whose own rows are
        // changing that same instant — the spinner going away as the answer arrives — is torn down
        // along with the sign-in sheet that contains it. Nothing appeared and the app fell back to
        // a signed-out profile with no message, which is what App Review reported on 2026-09-12.
        .sheet(isPresented: $apple.collecting) {
            AppleProfileSheet(flow: apple) { dismiss() }
                .environment(dependencies)
        }
    }

    private func startCountdown(from retryAfter: TimeInterval?) {
        countdownTask?.cancel()
        guard let retryAfter, retryAfter > 0 else {
            countdown = 0
            return
        }
        countdown = Int(retryAfter.rounded(.up))
        countdownTask = Task {
            // Measured against a deadline rather than counted down a tick at a time. A sleep
            // promises "at least", so a phone that is busy — which after several failed sign-ins
            // it may well be — makes a counted countdown run slow, and somebody sits watching a
            // lock that lifted a while ago.
            let began = ContinuousClock.now
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(250))
                countdown = max(0, Int((retryAfter - (ContinuousClock.now - began).inSeconds)
                                           .rounded(.up)))
                if countdown == 0 { return }
            }
        }
    }
}

/// The second step for a 2FA account: the same /login call retried with a
/// code. Offers the recovery-code fallback.
struct TwoFactorChallengeView: View {
    @Environment(AppDependencies.self) private var dependencies
    @State private var code = ""
    @State private var usingRecoveryCode = false

    private var session: SessionStore { dependencies.session }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField(usingRecoveryCode ? "Recovery code" : "6-digit code", text: $code)
                        .textContentType(.oneTimeCode)
                        .keyboardType(usingRecoveryCode ? .default : .numberPad)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                } header: {
                    Text("Two-factor authentication")
                } footer: {
                    Text(usingRecoveryCode
                         ? "Enter one of the recovery codes you saved when enabling two-factor."
                         : "Enter the current code from your authenticator app.")
                }

                if let message = session.errorMessage {
                    Section {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(Theme.danger)
                            .font(.callout)
                    }
                }

                Section {
                    Button {
                        Task { await session.submitTwoFactor(code: code, isRecoveryCode: usingRecoveryCode) }
                    } label: {
                        if session.state == .authenticating {
                            ProgressView().frame(maxWidth: .infinity)
                        } else {
                            Text("Verify").frame(maxWidth: .infinity)
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(code.isEmpty || session.state == .authenticating)

                    Button(usingRecoveryCode ? "Use authenticator code instead" : "Use a recovery code") {
                        usingRecoveryCode.toggle()
                        code = ""
                    }
                }
            }
            .navigationTitle("Verify it's you")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { session.cancelTwoFactor() }
                }
            }
        }
        .interactiveDismissDisabled()
    }
}
