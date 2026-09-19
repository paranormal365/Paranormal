import SwiftUI
import BenKit

/// What's waiting for you (iOS Slice 5). The same buckets, wording and order as the website's
/// notifications page — somebody who uses both should not have to learn the site twice.
struct NotificationsView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    private var store: NotificationsStore { dependencies.notifications }

    var body: some View {
        Group {
            switch store.state {
            case .idle, .loading:
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut:
                // A fact about who is asking, not a failure. The feed reads without an
                // account; this genuinely cannot.
                ContentUnavailableView {
                    Label("Sign in to see what's waiting", systemImage: "bell.slash")
                } description: {
                    Text("Notifications are about your cases, groups and gear — they need an account.")
                }

            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't load notifications", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await store.load() } }
                        .buttonStyle(.borderedProminent)
                }

            case .loaded where store.rows.isEmpty:
                ContentUnavailableView {
                    Label("Nothing waiting", systemImage: "checkmark.circle")
                } description: {
                    Text("You're all caught up.")
                }

            case .loaded:
                List {
                    Section {
                        ForEach(store.rows) { row in
                            NotificationRowView(row: row) {
                                if let destination = row.destination {
                                    router.open(destination)
                                }
                            }
                        }
                    } footer: {
                        Text("Counts come from the same place the website's bell does. "
                             + "Anything without a screen in the app yet is listed but not tappable.")
                            .font(.caption)
                    }
                }
                .listStyle(.insetGrouped)
            }
        }
        .navigationTitle("Notifications")
        .refreshable { await store.load() }
        .task { await store.load() }
        // Opened by deep link at launch, this screen's first load can beat sign-in and be
        // answered as a visitor — which renders "sign in to see what's waiting" to somebody
        // who is signing in. Re-ask when identity resolves rather than trusting a parent's
        // observer to win the race. (The feed learned the same lesson with CanPost.)
        .onChange(of: dependencies.session.me?.userId) { _, _ in
            Task { await store.load() }
        }
    }
}

/// One waiting item. Colour comes from AGE, never count — see `NotificationUrgency`.
struct NotificationRowView: View {
    let row: NotificationRow
    let onOpen: () -> Void

    var body: some View {
        Button(action: onOpen) {
            HStack(spacing: 12) {
                Image(systemName: row.systemImage)
                    .font(.title3)
                    .foregroundStyle(tint)
                    .frame(width: 28)

                VStack(alignment: .leading, spacing: 2) {
                    Text(row.title)
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(Theme.bone)
                        .multilineTextAlignment(.leading)
                    Text(row.detail)
                        .font(.caption)
                        .foregroundStyle(Theme.fog)
                        .multilineTextAlignment(.leading)
                }

                Spacer(minLength: 8)

                Text(NotificationText.badge(row.bucket.count))
                    .font(.caption.weight(.semibold).monospacedDigit())
                    .padding(.horizontal, 8).padding(.vertical, 3)
                    .background(tint.opacity(0.2), in: Capsule())
                    .foregroundStyle(tint)

                if row.destination != nil {
                    Image(systemName: "chevron.right")
                        .font(.caption)
                        .foregroundStyle(Theme.fog)
                }
            }
            .padding(.vertical, 4)
        }
        .buttonStyle(.plain)
        // A row with nowhere to go is not a button: VoiceOver and the eye both learn that
        // from the same fact rather than from a chevron that lies.
        .disabled(row.destination == nil)
        .accessibilityLabel("\(row.title). \(row.bucket.count) waiting. \(row.detail)")
        .accessibilityHint(row.destination == nil ? "Not available in the app yet" : "Opens")
    }

    private var tint: Color {
        switch row.urgency {
        case .overdue: Theme.danger
        case .aging: Theme.warning
        case .fresh: Theme.ecto
        case .none: Theme.fog
        }
    }
}
