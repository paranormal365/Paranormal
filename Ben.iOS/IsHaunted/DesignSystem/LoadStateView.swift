import SwiftUI
import BenKit

/// The shared wrapper every list/detail screen renders through. The four
/// outcomes are VISUALLY DISTINCT — the repo doctrine that a refusal must
/// never render as "nothing here" (README-refused-vs-empty.md).
struct LoadStateView<T: Sendable, Content: View>: View {
    let result: LoadResult<T>?
    /// Decides whether an `.ok` payload is genuinely empty.
    let isEmpty: (T) -> Bool
    let emptyTitle: String
    let emptyMessage: String
    let retry: (() async -> Void)?
    @ViewBuilder let content: (T) -> Content

    init(
        _ result: LoadResult<T>?,
        isEmpty: @escaping (T) -> Bool = { _ in false },
        emptyTitle: String = "Nothing here yet",
        emptyMessage: String = "",
        retry: (() async -> Void)? = nil,
        @ViewBuilder content: @escaping (T) -> Content
    ) {
        self.result = result
        self.isEmpty = isEmpty
        self.emptyTitle = emptyTitle
        self.emptyMessage = emptyMessage
        self.retry = retry
        self.content = content
    }

    var body: some View {
        switch result {
        case nil:
            ProgressView()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .ok(let value) where isEmpty(value):
            ContentUnavailableView {
                Label(emptyTitle, systemImage: "moon.stars")
            } description: {
                Text(emptyMessage)
            }
        case .ok(let value):
            content(value)
        case .failed(let reason):
            ContentUnavailableView {
                Label("Couldn't load this", systemImage: "exclamationmark.triangle")
                    .foregroundStyle(Theme.warning)
            } description: {
                // The server's prose when it wrote a sentence; the status text
                // fallback otherwise. Never a blank page.
                Text(reason ?? "The server couldn't be reached.")
            } actions: {
                if let retry {
                    Button("Try again") { Task { await retry() } }
                        .buttonStyle(.borderedProminent)
                }
            }
        case .sessionEnded:
            ContentUnavailableView {
                Label("Your session ended", systemImage: "person.crop.circle.badge.xmark")
            } description: {
                Text("Sign in again to keep going.")
            }
        case .rateLimited(let retryAfter):
            ContentUnavailableView {
                Label("Slow down a moment", systemImage: "hourglass")
            } description: {
                if let retryAfter, retryAfter > 0 {
                    Text("Too many requests — try again in \(Int(retryAfter.rounded(.up))) seconds.")
                } else {
                    Text("Too many requests — try again shortly.")
                }
            }
        }
    }
}
