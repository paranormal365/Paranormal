import SwiftUI
import BenKit

/// Looking after the account (iOS Slice 8): the password, and two-step sign-in.
struct SecurityView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var status: TwoFactorStatus?
    @State private var setup: TwoFactorSetup?
    @State private var code = ""
    @State private var recoveryCodes: [String]?
    @State private var isBusy = false
    @State private var message: String?
    @State private var showPasswordChange = false

    var body: some View {
        List {
            Section {
                Button {
                    showPasswordChange = true
                } label: {
                    Label("Change password", systemImage: "key")
                }
            } header: {
                Text("Password")
            }

            Section {
                if let status {
                    LabeledContent("Two-step sign-in",
                                   value: status.enabled ? "On" : "Off")
                    if status.enabled {
                        LabeledContent("Recovery codes left", value: "\(status.recoveryCodesRemaining)")
                        if status.recoveryCodesRemaining == 0 {
                            // Nought left means the next lost phone is a locked-out account.
                            Label("Generate new codes on the website before you need them.",
                                  systemImage: "exclamationmark.triangle")
                                .font(.caption).foregroundStyle(Theme.warning)
                        }
                    }
                } else {
                    ProgressView()
                }

                if status?.enabled == false {
                    if let setup {
                        VStack(alignment: .leading, spacing: 10) {
                            Text("Add this key to your authenticator app:")
                                .font(.callout)
                            // The key, selectable and in a font where 0 and O differ — this
                            // gets typed by hand more often than anyone admits.
                            Text(setup.sharedKey)
                                .font(.system(.body, design: .monospaced))
                                .textSelection(.enabled)
                                .padding(8)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .background(Theme.mist, in: RoundedRectangle(cornerRadius: 8))

                            if let url = URL(string: setup.authenticatorUri) {
                                // On the phone the app IS the second device, so a QR code
                                // would have nothing to scan it. A link hands the secret
                                // straight to the authenticator instead.
                                Link(destination: url) {
                                    Label("Open in your authenticator app", systemImage: "arrow.up.forward.app")
                                }
                            }

                            TextField("6-digit code", text: $code)
                                .textContentType(.oneTimeCode)
                                .keyboardType(.numberPad)

                            Button {
                                Task { await enable() }
                            } label: {
                                if isBusy { ProgressView() } else { Text("Turn on two-step") }
                            }
                            .buttonStyle(.borderedProminent)
                            .disabled(code.isEmpty || isBusy)
                        }
                        .padding(.vertical, 4)
                    } else {
                        Button {
                            Task { await beginSetup() }
                        } label: {
                            Label("Set up two-step sign-in", systemImage: "lock.shield")
                        }
                        .disabled(isBusy)
                    }
                }

                if status?.enabled == true {
                    VStack(alignment: .leading, spacing: 8) {
                        TextField("6-digit code to turn it off", text: $code)
                            .textContentType(.oneTimeCode)
                            .keyboardType(.numberPad)
                        Button("Turn off two-step", role: .destructive) {
                            Task { await disable() }
                        }
                        .disabled(code.isEmpty || isBusy)
                    }
                    .padding(.vertical, 4)
                }
            } header: {
                Text("Two-step sign-in")
            } footer: {
                Text("A code from an authenticator app, on top of your password.")
            }

            if let recoveryCodes {
                Section {
                    // Shown ONCE. The server cannot produce them again, so the screen says so
                    // rather than letting somebody assume they can come back for them.
                    Label("Save these now — they are not shown again.",
                          systemImage: "exclamationmark.triangle")
                        .font(.callout).foregroundStyle(Theme.warning)
                    ForEach(recoveryCodes, id: \.self) { recoveryCode in
                        Text(recoveryCode)
                            .font(.system(.body, design: .monospaced))
                            .textSelection(.enabled)
                    }
                    ShareLink(item: recoveryCodes.joined(separator: "\n")) {
                        Label("Save or send them", systemImage: "square.and.arrow.up")
                    }
                } header: {
                    Text("Recovery codes")
                }
            }
        }
        .navigationTitle("Security")
        .sheet(isPresented: $showPasswordChange) {
            ChangePasswordView()
                .environment(dependencies)
        }
        .alert("Security", isPresented: Binding(
            get: { message != nil }, set: { if !$0 { message = nil } })
        ) {
            Button("OK") { message = nil }
        } message: {
            Text(message ?? "")
        }
        .task { status = await dependencies.accountActions.twoFactorStatus() }
    }

    private func beginSetup() async {
        isBusy = true
        defer { isBusy = false }
        setup = await dependencies.accountActions.beginTwoFactorSetup()
        if setup == nil { message = "Couldn't start setup — try again in a moment." }
    }

    private func enable() async {
        isBusy = true
        defer { isBusy = false }
        switch await dependencies.accountActions.enableTwoFactor(code: code) {
        case .success(let enabled):
            recoveryCodes = enabled.recoveryCodes
            code = ""
            setup = nil
            status = await dependencies.accountActions.twoFactorStatus()
        case .failure(let error):
            message = error.message
        }
    }

    private func disable() async {
        isBusy = true
        defer { isBusy = false }
        switch await dependencies.accountActions.disableTwoFactor(code: code) {
        case .success:
            code = ""
            recoveryCodes = nil
            status = await dependencies.accountActions.twoFactorStatus()
        case .failure(let error):
            message = error.message
        }
    }
}

struct ChangePasswordView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    @State private var current = ""
    @State private var updated = ""
    @State private var confirmation = ""
    @State private var isBusy = false
    @State private var errorMessage: String?

    private var canSubmit: Bool {
        !current.isEmpty && !updated.isEmpty && updated == confirmation && !isBusy
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    SecureField("Current password", text: $current)
                        .textContentType(.password)
                    SecureField("New password", text: $updated)
                        .textContentType(.newPassword)
                    SecureField("New password again", text: $confirmation)
                        .textContentType(.newPassword)
                } footer: {
                    // Said as soon as it's true, not after they press the button.
                    if !confirmation.isEmpty && updated != confirmation {
                        Text("Those two don't match.").foregroundStyle(Theme.danger)
                    }
                }

                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(Theme.danger).font(.callout)
                    }
                }
            }
            .navigationTitle("Change password")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(isBusy)
                }
                ToolbarItem(placement: .confirmationAction) {
                    if isBusy {
                        ProgressView()
                    } else {
                        Button("Save") { Task { await save() } }.disabled(!canSubmit)
                    }
                }
            }
        }
    }

    private func save() async {
        isBusy = true
        errorMessage = nil
        defer { isBusy = false }

        switch await dependencies.accountActions.changePassword(current: current, new: updated) {
        case .success:
            dismiss()
        case .failure(let error):
            // Identity's own wording — "must have at least one digit" is actionable.
            errorMessage = error.message
        }
    }
}
