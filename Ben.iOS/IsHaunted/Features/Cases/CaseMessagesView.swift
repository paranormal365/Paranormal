import SwiftUI
import BenKit

/// The conversation between a client and the group working their case.
struct CaseMessagesView: View {
    @Environment(AppDependencies.self) private var dependencies

    let caseId: UUID
    @State private var store: CaseMessagesStore?
    @State private var draft = ""
    @State private var errorMessage: String?
    @State private var canDictate = false
    @State private var isListening = false
    @State private var listener: Task<Void, Never>?
    @FocusState private var writing: Bool

    var body: some View {
        VStack(spacing: 0) {
            conversation
            Divider()
            composer
        }
        .navigationTitle("Messages")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            let store = CaseMessagesStore(caseId: caseId, api: dependencies.api)
            self.store = store
            canDictate = await dependencies.fieldKit.sensors().dictation?.isAvailableOffline ?? false
            await store.load()
        }
        .onChange(of: dependencies.session.me?.userId) { _, _ in
            Task { await store?.load() }
        }
        .onDisappear { stopListening() }
    }

    // MARK: - Dictation

    private var dictation: DictationService? { dependencies.fieldKit.sensors().dictation }

    private func startDictating() {
        guard let dictation else { return }
        errorMessage = nil
        isListening = true
        // What was already typed is kept: dictation adds to a message rather than replacing it.
        let existing = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        listener = Task {
            do {
                for await update in try await dictation.start() {
                    draft = existing.isEmpty ? update.text : existing + " " + update.text
                }
            } catch {
                errorMessage = error.localizedDescription
            }
            isListening = false
        }
    }

    private func finishDictating() async {
        guard let dictation else { return }
        _ = await dictation.stop()
        isListening = false
        listener?.cancel()
        listener = nil
    }

    private func stopListening() {
        listener?.cancel()
        listener = nil
        guard isListening, let dictation else { return }
        isListening = false
        Task { await dictation.stop() }
    }

    @ViewBuilder
    private var conversation: some View {
        switch store?.state {
        case .loading, nil:
            Spacer(); ProgressView(); Spacer()

        case .signedOut:
            ContentUnavailableView("Sign in to read your messages",
                                   systemImage: "person.crop.circle.badge.questionmark")

        case .failed(let reason):
            // A refusal is not an empty conversation.
            ContentUnavailableView {
                Label("Couldn't load your messages", systemImage: "exclamationmark.triangle")
            } description: {
                Text(reason ?? "The server couldn't be reached.")
            } actions: {
                Button("Try again") { Task { await store?.load() } }
            }

        case .loaded:
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 10) {
                        if store?.messages.isEmpty == true {
                            Text("No messages yet. Ask your group anything about the case.")
                                .font(.callout).foregroundStyle(Theme.fog)
                                .multilineTextAlignment(.center)
                                .padding(.top, 40).padding(.horizontal, 32)
                        }
                        ForEach(store?.messages ?? []) { message in
                            MessageBubble(message: message).id(message.id)
                        }
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 12)
                }
                .onChange(of: store?.messages.count) { _, _ in
                    // The newest message is the reason the screen is open.
                    if let last = store?.messages.last {
                        withAnimation { proxy.scrollTo(last.id, anchor: .bottom) }
                    }
                }
                .onAppear {
                    if let last = store?.messages.last { proxy.scrollTo(last.id, anchor: .bottom) }
                }
            }
        }
    }

    private var composer: some View {
        VStack(spacing: 6) {
            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.danger)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            HStack(spacing: 8) {
                TextField(isListening ? "Listening…" : "Message your group",
                          text: $draft, axis: .vertical)
                    .lineLimit(1...5)
                    .textFieldStyle(.roundedBorder)
                    .focused($writing)
                    .accessibilityIdentifier("message-draft")

                // Only where this device can transcribe with NO connection. Offering dictation
                // that quietly needs a network would fail in exactly the building where somebody
                // is standing when they want to say something quickly.
                if canDictate {
                    Button {
                        if isListening { Task { await finishDictating() } } else { startDictating() }
                    } label: {
                        Image(systemName: isListening ? "stop.circle.fill" : "mic.circle")
                            .font(.title2)
                    }
                    .tint(isListening ? Theme.danger : Theme.ecto)
                    .accessibilityLabel(isListening ? "Stop dictating" : "Dictate this message")
                    .accessibilityIdentifier("dictate-message")
                }

                Button {
                    Task { await send() }
                } label: {
                    if store?.sending == true {
                        ProgressView()
                    } else {
                        Image(systemName: "arrow.up.circle.fill").font(.title2)
                    }
                }
                .disabled(draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                          || store?.sending == true)
                .accessibilityLabel("Send")
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private func send() async {
        if isListening { await finishDictating() }
        errorMessage = nil
        let body = draft
        // Cleared optimistically so the field is ready; put back on refusal rather than losing
        // what somebody typed.
        draft = ""
        switch await store?.send(body) {
        case .success:
            break
        case .failure(let error):
            draft = body
            errorMessage = error.message
        case nil:
            draft = body
        }
    }
}

private struct MessageBubble: View {
    let message: MyCaseMessage

    var body: some View {
        HStack {
            if message.isMine { Spacer(minLength: 48) }
            VStack(alignment: message.isMine ? .trailing : .leading, spacing: 3) {
                if !message.isMine {
                    // Which investigator is speaking matters; "the group" is not a person.
                    Text(message.authorDisplayName)
                        .font(.caption2).foregroundStyle(Theme.fog)
                }
                Text(message.body)
                    .padding(.horizontal, 12).padding(.vertical, 8)
                    .background(message.isMine ? Theme.ecto.opacity(0.22) : Theme.mist,
                                in: RoundedRectangle(cornerRadius: 14))
                Text(message.dateCreated.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption2).foregroundStyle(Theme.fog)
            }
            if !message.isMine { Spacer(minLength: 48) }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(message.isMine
                            ? "You said: \(message.body)"
                            : "\(message.authorDisplayName) said: \(message.body)")
    }
}
