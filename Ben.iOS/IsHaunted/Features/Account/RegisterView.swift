import SwiftUI
import BenKit

/// Signing up (iOS Slice 8). Deliberately short: an email, a password, the name people see,
/// and the @name that is permanent. Everything else can wait until they're in.
struct RegisterView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    @State private var email = ""
    @State private var password = ""
    @State private var displayName = ""
    @State private var handle = ""
    @State private var handleCheck: HandleAvailability?
    @State private var checkTask: Task<Void, Never>?
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @State private var sentMessage: String?

    private var canSubmit: Bool {
        !email.isEmpty && email.contains("@") && !password.isEmpty
            && displayName.trimmingCharacters(in: .whitespaces).count >= 2
            && !handle.isEmpty && handleCheck?.available != false && !isSubmitting
    }

    var body: some View {
        NavigationStack {
            Form {
                if let sentMessage {
                    Section {
                        // The end of the flow, not a step in it: nothing else to fill in.
                        VStack(alignment: .leading, spacing: 8) {
                            Label("Check your email", systemImage: "envelope.badge")
                                .font(.headline).foregroundStyle(Theme.ecto)
                            Text(sentMessage).font(.callout).foregroundStyle(Theme.bone)
                        }
                        .padding(.vertical, 4)
                    }
                } else {
                    Section {
                        TextField("Email", text: $email)
                            .textContentType(.emailAddress)
                            .keyboardType(.emailAddress)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                        SecureField("Password", text: $password)
                            .textContentType(.newPassword)
                    } footer: {
                        Text("You'll confirm your email before you can sign in.")
                    }

                    Section {
                        TextField("Name people see", text: $displayName)
                            .textContentType(.name)
                        HStack {
                            Text("@").foregroundStyle(Theme.fog)
                            TextField("name", text: $handle)
                                .textInputAutocapitalization(.never)
                                .autocorrectionDisabled()
                                .onChange(of: handle) { _, value in scheduleCheck(value) }
                            if let check = handleCheck {
                                Image(systemName: check.available ? "checkmark.circle.fill" : "xmark.circle.fill")
                                    .foregroundStyle(check.available ? Theme.success : Theme.danger)
                            }
                        }
                    } header: {
                        Text("Who you are")
                    } footer: {
                        // Said BEFORE they choose, because it cannot be undone afterwards.
                        if let reason = handleCheck?.reason, handleCheck?.available == false {
                            Text(reason).foregroundStyle(Theme.danger)
                        } else {
                            Text("Your @name is how people mention you. It's permanent, so choose kindly.")
                        }
                    }

                    if let errorMessage {
                        Section {
                            Label(errorMessage, systemImage: "exclamationmark.triangle")
                                .foregroundStyle(Theme.danger).font(.callout)
                        }
                    }

                    Section {
                        Button {
                            Task { await submit() }
                        } label: {
                            if isSubmitting {
                                ProgressView().frame(maxWidth: .infinity)
                            } else {
                                Text("Create account").frame(maxWidth: .infinity)
                            }
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(!canSubmit)
                    }
                }
            }
            .navigationTitle("Create an account")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(sentMessage == nil ? "Cancel" : "Done") { dismiss() }
                        .disabled(isSubmitting)
                }
            }
        }
        .onDisappear { checkTask?.cancel() }
    }

    /// Checked as they type, debounced — a request per keystroke would be rude to the server
    /// and would race its own answers back out of order.
    private func scheduleCheck(_ value: String) {
        checkTask?.cancel()
        handleCheck = nil
        let trimmed = value.trimmingCharacters(in: .whitespaces)
        guard trimmed.count >= 3 else { return }

        checkTask = Task {
            try? await Task.sleep(for: .milliseconds(400))
            guard !Task.isCancelled else { return }
            let answer = await dependencies.accountActions.handleAvailability(trimmed)
            guard !Task.isCancelled, trimmed == handle.trimmingCharacters(in: .whitespaces) else { return }
            handleCheck = answer
        }
    }

    private func submit() async {
        guard canSubmit else { return }
        isSubmitting = true
        errorMessage = nil
        defer { isSubmitting = false }

        let result = await dependencies.accountActions.register(RegisterRequest(
            email: email.trimmingCharacters(in: .whitespaces),
            password: password,
            displayName: displayName.trimmingCharacters(in: .whitespaces),
            handle: handle.trimmingCharacters(in: .whitespaces)))

        switch result {
        case .success(let response):
            // The server says the same thing whether or not the address was already
            // registered — it will not confirm to a stranger which addresses have accounts.
            sentMessage = response.message
        case .failure(let error):
            errorMessage = error.message
        }
    }
}

/// The other half of the emailed link (`/validate-email/{token}` and the website's
/// `/confirm-email?userId=&code=`). Confirms, then says plainly what happened.
struct ConfirmEmailView: View {
    @Environment(AppDependencies.self) private var dependencies

    let userId: UUID
    let code: String

    @State private var response: ConfirmEmailResponse?
    @State private var failedToReach = false

    var body: some View {
        Group {
            if failedToReach {
                ContentUnavailableView {
                    Label("Couldn't confirm right now", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text("The server couldn't be reached. Your link is still good — try again.")
                } actions: {
                    Button("Try again") { Task { await confirm() } }
                        .buttonStyle(.borderedProminent)
                }
            } else if let response {
                ContentUnavailableView {
                    Label(response.succeeded ? "Email confirmed" : "That link didn't work",
                          systemImage: response.succeeded ? "checkmark.seal" : "xmark.seal")
                        .foregroundStyle(response.succeeded ? Theme.success : Theme.warning)
                } description: {
                    Text(response.message)
                }
            } else {
                ProgressView("Confirming…")
            }
        }
        .navigationTitle("Confirm email")
        .navigationBarTitleDisplayMode(.inline)
        .task { await confirm() }
    }

    private func confirm() async {
        failedToReach = false
        // The endpoint answers 200 with succeeded:false for a spent link, so nil means the
        // request itself failed — a different thing, and a retry is worth offering.
        guard let answer = await dependencies.accountActions.confirmEmail(userId: userId, code: code) else {
            failedToReach = true
            return
        }
        response = answer
    }
}
