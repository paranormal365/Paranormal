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
                } footer: {
                    Text("Registration and password reset arrive in a later slice — use the website for those for now.")
                }
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
    }

    private func startCountdown(from retryAfter: TimeInterval?) {
        countdownTask?.cancel()
        guard let retryAfter, retryAfter > 0 else {
            countdown = 0
            return
        }
        countdown = Int(retryAfter.rounded(.up))
        countdownTask = Task {
            while countdown > 0, !Task.isCancelled {
                try? await Task.sleep(for: .seconds(1))
                countdown -= 1
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
