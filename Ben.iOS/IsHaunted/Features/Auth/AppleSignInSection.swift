import SwiftUI
import AuthenticationServices
import BenKit

/// The Sign in with Apple button, and the one form it can lead to.
///
/// Apple hands over a person's real name exactly ONCE — on the first authorization for this app,
/// and never again, not even after deleting and reinstalling. So when the server says an account
/// still needs a name and a handle, whatever Apple gave is carried into that form immediately;
/// dropping it there means it is gone for good.
struct AppleSignInSection: View {
    @Environment(AppDependencies.self) private var dependencies
    var onSignedIn: () -> Void

    @State private var pendingToken: String?
    @State private var suggestedName: String?
    @State private var handleProblem: String?
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
            Text("Uses your Apple Account. If you already have an account here with the same email, this signs you into it rather than making a second one.")
        }
        .sheet(isPresented: $collecting) {
            AppleProfileSheet(
                suggestedName: suggestedName,
                handleProblem: handleProblem,
                busy: busy
            ) { name, handle in
                await finish(displayName: name, handle: handle)
            }
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
            identityToken: pendingToken, displayName: displayName, handle: handle) {
        case .signedIn:
            collecting = false
            await dependencies.session.adoptExternalSignIn()
            onSignedIn()
        case .needsProfile(let name, _, let problem):
            suggestedName = name ?? suggestedName
            handleProblem = problem
            collecting = true
        case .failed(let reason):
            collecting = false
            errorMessage = reason
        }
    }

    private func finish(displayName: String, handle: String) async {
        await send(displayName: displayName, handle: handle)
    }
}

/// Collects the two things an account cannot exist without. The handle is permanent, and this
/// screen says so — it is the last moment anyone can choose it.
private struct AppleProfileSheet: View {
    let suggestedName: String?
    let handleProblem: String?
    let busy: Bool
    var onSubmit: (String, String) async -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var handle = ""

    private var canSubmit: Bool {
        name.trimmingCharacters(in: .whitespaces).count >= 2
            && !handle.trimmingCharacters(in: .whitespaces).isEmpty && !busy
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("The name people see", text: $name)
                        .textContentType(.name)
                } footer: {
                    Text("You can change this later.")
                }

                Section {
                    TextField("@name", text: $handle)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                } header: {
                    Text("Your @name")
                } footer: {
                    // Said before it is chosen, not after.
                    Text(handleProblem ?? "This is permanent — it can't be changed once your account exists.")
                        .foregroundStyle(handleProblem == nil ? Theme.fog : Theme.danger)
                }

                Section {
                    Button {
                        Task { await onSubmit(name.trimmingCharacters(in: .whitespaces),
                                              handle.trimmingCharacters(in: .whitespaces)) }
                    } label: {
                        if busy { ProgressView().frame(maxWidth: .infinity) }
                        else { Text("Create my account").frame(maxWidth: .infinity) }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!canSubmit)
                }
            }
            .navigationTitle("Almost there")
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
