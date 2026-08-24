import SwiftUI

/// The Feed section root until Slice 3 delivers the real feed. Points at the
/// Slice-1 live-API proof so the shell demonstrably talks to the backend.
struct FeedPlaceholderView: View {
    var body: some View {
        VStack(spacing: 16) {
            ContentUnavailableView {
                Label("Feed", systemImage: "sparkles.rectangle.stack")
            } description: {
                Text("The feed arrives in Slice 3.")
            }
            NavigationLink("API connection check — public events") {
                PublicEventsPreview()
            }
            .buttonStyle(.bordered)
            .padding(.bottom, 32)
        }
        .navigationTitle("Feed")
    }
}
